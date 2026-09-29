# Template dependency manifest

GitHub CI/PR and internal managed-package jobs generate `artifacts/cg/templates/cgmanifest.json` after packing the current build's packages. It records the external NuGet and npm dependencies used by the template configurations without building or running generated applications.

The manifest is a build artifact, not a checked-in file. Every package build generates it, so there is no manual update requirement, comparison baseline, or changed-file gate. The existing `artifacts/` gitignore rule covers it.

## Build ordering and Component Governance

Generation must run after **all managed packages have been packed**, not as an `AfterTargets="Build"` or `AfterTargets="Pack"` hook on the template project alone. The other Aspire packages needed for restore might not exist yet when that individual project finishes. The `GenerateTemplateCgManifest` target lives in `Aspire.ProjectTemplates.csproj`; the shared PowerShell wrapper invokes it after the managed build/pack command in both CI systems.

The internal managed job's existing CG scan runs afterward and searches the repository checkout, including `artifacts`. The manifest must remain there until scanning completes. No CG tasks, scan roots, or exclusions are changed. Other jobs continue their existing scans; they do not need a second copy of this manifest for the managed job to register the template dependencies. The manifest is outside `artifacts/bin` and `artifacts/obj`, so GitHub's post-build cleanup does not remove it.

Generation errors fail the build rather than accepting stale dependency data. Generation also runs on clean checkouts with no template changes, because there is no persisted manifest to reuse.

## Local generation

Build the current managed packages, then generate the manifest from the repository root:

```powershell
.\build.cmd -restore -build -pack /p:SkipNativeBuild=true /p:SkipBundleDeps=true /p:SkipTestProjects=true /p:SkipPlaygroundProjects=true
pwsh eng/scripts/generate-template-cgmanifest.ps1
```

Use `./build.sh` on Linux/macOS. Pass `-Configuration Release` to the generator script when using Release packages. The script reads the version from the same-build `Aspire.ProjectTemplates` package, so PR/daily version suffixes do not need to be copied manually. It rejects ambiguous stale template packages in the output directory.

Repo-built package identities are excluded, but those packages are still restored from the current build and **all external transitives remain included**. Exclusion matches exact ID/version pairs in the build's shipping nupkgs, not all packages with an `Aspire.*` prefix. The repository's approved external feeds are retained. Neither the repository NuGet configuration nor the user's template hive is modified.

Restore uses a private extraction cache, refreshing the locally built identities on each run so repeated `-dev` versions cannot reuse stale packages. Each graph is force-reevaluated: a sibling graph repopulating the shared cache must not make stale assets appear up to date. The normal NuGet cache serves as a read-only package source for external dependencies.

## Independent graphs, one restore invocation

`tools/GenerateTemplateManifest` uses the repository SDK's template engine in process. It renders only dependency-bearing files into a temporary directory; it does not generate application source, install templates into the user's hive, or execute post-actions.

The generator discovers finite boolean/choice parameters used by project inputs, framework selection, source exclusions, and computed/generated symbols. Parameters affecting only application content, ports, or launch settings do not multiply the restore work. The template engine evaluates conditions and substitutions, including optional Redis, source-file exclusions, and all test-framework choices.

The resulting project graphs retain their SDKs, framework references, package metadata, project-reference edges, and restore-affecting properties. This preserves implicit dependencies introduced by SDK targets. Equivalent projects are shared across templates/options, and identical inputs across target frameworks become one multi-targeted project. A generated solution submits all distinct graphs in **one `dotnet restore` invocation**. NuGet resolves each graph independently; the generator unions their package/version identities from the assets files.

Combining every package into a single graph, or restoring only direct versions missing from a "latest-first" graph, is insufficient: different options can select different **transitive** versions even with the same direct package versions. The generator never drops a configuration merely because its direct references appear in another graph.

npm registrations come from existing template lockfiles, including transitive, development, and optional platform packages. No npm install is needed. Versioned SDK packages are registered separately because SDK resolution does not include the SDK itself in `project.assets.json`.

## Outputs and limitations

- `artifacts/cg/templates/cgmanifest.json`: generated external dependency manifest.
- `artifacts/obj/Aspire.ProjectTemplates/<Configuration>/net8.0/template-cg-restore/TemplateDependencies.slnx`: the independently restored graphs.
- `artifacts/obj/Aspire.ProjectTemplates/<Configuration>/net8.0/template-cg-restore/Graph*/obj/project.assets.json`: resolved dependencies for each graph.

Set `-p:TemplateCgManifestPath=<path>` when invoking the MSBuild target to override the manifest location. Only graphs in the current plan contribute packages; stale generated directories are not scanned by the generator. Missing npm lockfiles and unbounded dependency parameters fail generation rather than silently omitting coverage. Shared template `.props`/`.targets` inputs need explicit support before they can be introduced. The generated projects are restore-only, not buildable test applications.

The helper's `--help` describes its six required positional arguments and `--plan-only` option. Use `--plan-only` to inspect graph deduplication without restoring.

This generation step replaces the internal basic template test stage, but does not change the shipping template package or regular GitHub template tests. Coverage is evaluated with the invoking repository SDK; it is not a replacement for SDK compatibility or behavioral template tests.
