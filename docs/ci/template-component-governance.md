# Template dependency manifest

The repository-root [`cgmanifest.json`](../../cgmanifest.json) records the external NuGet and npm packages used by every template dependency configuration. Keeping it in source control makes it visible to Component Governance's default source scan, including jobs that do not build templates.

## Update and verify

Build the current managed packages, update the manifest, and commit it with the dependency change:

```powershell
.\build.cmd -restore -build -pack /p:SkipNativeBuild=true /p:SkipBundleDeps=true /p:SkipTestProjects=true /p:SkipPlaygroundProjects=true
pwsh eng/scripts/update-template-cgmanifest.ps1 -Update
```

Use `./build.sh` on Linux/macOS. Pass `-Configuration Release` to the manifest script when using Release packages. The script reads the version from the same-build `Aspire.ProjectTemplates` package, so PR/daily version suffixes do not need to be copied manually. It rejects ambiguous stale template packages in the output directory.

Run the script without `-Update` to verify. Verification does not overwrite the checked-in manifest; on drift it fails, writes `cgmanifest.generated.json` under the intermediate restore directory, and prints the update command. The underlying MSBuild targets are `GenerateTemplateCgManifest` and `VerifyTemplateCgManifest`.

Repo-built package identities are excluded from the manifest because their versions vary by build. They are still restored from the current build's package feed, and **all external transitives remain included**. Exclusion matches the exact ID/version pairs in the build's shipping nupkgs, not all packages with an `Aspire.*` prefix. The repository's approved external feeds are retained. Neither the repository NuGet configuration nor the user's template hive is modified.

Restore uses a private extraction cache, refreshing the locally built identities on each run so repeated `-dev` versions cannot reuse stale packages. The normal NuGet cache serves as a read-only package source for external dependencies. This preserves package reuse without modifying the user's cache.

## CI and no-op behavior

GitHub's unconditional package-build job verifies the manifest before cleaning its build outputs. The internal official and unofficial managed-build jobs perform the same verification. This replaces the internal basic template test stage and its SDK setup/native artifact downloads; regular GitHub template tests remain unchanged.

CI passes `-ChangesOnly`. The script compares commits against [`eng/template-cg-inputs.txt`](../../eng/template-cg-inputs.txt), which includes templates, package version pins, restore/SDK metadata, the generator, CI wiring, and the manifest itself. Ordinary library/application code and unrelated changes return successfully **before invoking dotnet or accessing package outputs**.

PR merge checkouts compare their first parent (the actual target revision) with the merge result, covering the whole PR. GitHub pushes supply `event.before` to cover the complete pushed range; other CI commits compare their first parent. The checkout retains two commits, fetching a supplied push base if necessary. Missing history or a missing manifest causes verification, never a skip. Local verification is unconditional unless `-ChangesOnly` is explicitly supplied; `-BaseRef` and `-HeadRef` allow checking a specific range.

## Independent graphs, one restore invocation

`tools/GenerateTemplateManifest` uses the repository SDK's template engine in process. It renders only dependency-bearing files into a temporary directory; it does not generate application source, install templates into the user's hive, or execute post-actions.

The generator discovers finite boolean/choice parameters used by project inputs, framework selection, source exclusions, and computed/generated symbols. Parameters affecting only application content, ports, or launch settings do not multiply the restore work. The template engine evaluates conditions and substitutions, including optional Redis, source-file exclusions, and all test-framework choices.

The resulting project graphs retain their SDKs, framework references, package metadata, project-reference edges, and restore-affecting properties. This also preserves implicit dependencies introduced by SDK targets. Equivalent projects are shared across templates/options, and identical inputs across target frameworks become one multi-targeted project. A generated solution submits all distinct graphs in **one `dotnet restore` invocation**. NuGet resolves each graph independently; the generator unions their package/version identities from the resulting assets files.

Combining every package into a single graph, or restoring only direct versions missing from a "latest-first" graph, is insufficient: different options can select different **transitive** versions even with the same direct package versions. The generator therefore never drops a configuration merely because its direct references appear in another graph.

npm registrations come from the existing template lockfiles, including transitive, development, and optional platform packages. No npm install is needed. Versioned SDK packages are registered separately because SDK resolution does not include the SDK itself in `project.assets.json`.

## Outputs and limitations

The committed output is the repository-root `cgmanifest.json`. For a default Debug build, intermediate outputs are under `artifacts/obj/Aspire.ProjectTemplates/Debug/net8.0/`:

- `template-cg-restore/TemplateDependencies.slnx`: the independently restored graphs.
- `template-cg-restore/Graph*/obj/project.assets.json`: resolved dependencies for each graph.
- `template-cg-restore/cgmanifest.generated.json`: proposed contents when verification fails.

Set `-p:TemplateCgManifestPath=<path>` to place the manifest elsewhere. Only graphs in the current plan contribute packages; stale generated directories are not scanned. Template render failures, restore failures, missing npm lockfiles, and unbounded dependency parameters fail generation rather than silently omitting coverage. Shared template `.props`/`.targets` inputs need explicit support before they can be introduced. The generated projects are restore-only, not buildable test applications.

The helper accepts `--plan-only` after its six positional arguments to inspect graph deduplication without restoring. Discovery tests exercise option handling and graph sharing without requiring package feeds.

This verification does not change the shipping template package. Coverage is evaluated with the invoking repository SDK; it is not a replacement for SDK compatibility or behavioral template tests.
