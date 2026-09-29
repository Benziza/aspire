---
name: bump-aspire-version
description: Bumps the Aspire repository product version in eng/Versions.props using previous version-bump commits as guidance. Use when asked to bump Aspire branding, advance the repository to a new major or minor version, or create a version-bump PR. Not for external dependency, SDK, or container-image updates.
---

# Bump the Aspire repository version

The product version is defined in the first property group of `eng/Versions.props`.
Keep the target branch's automatic milestone assignment aligned in
`.github/policies/milestoneAssignment.prClosed.yml`. Do not replace every occurrence
of the old version throughout the repository.

## Inspect the target and history

1. Establish the requested version and target branch. Interpret `X.Y` as `X.Y.0`.
   Ask if the target version is missing; do not infer it from the calendar or SDK.
2. Read the working-tree status and preserve unrelated changes. Use the current
   session branch and follow any app-managed branch naming requirements.
3. Read both files and inspect previous bumps, including their complete diffs so
   related automation changes are not missed:

   ```bash
   git log -8 --oneline -G '<MajorVersion>|<MinorVersion>|<PatchVersion>|AspireDashboardImageTag' -- eng/Versions.props
   git log -5 --oneline -- .github/policies/milestoneAssignment.prClosed.yml
   git show <relevant-commit>
   ```

   Useful precedents are #19139 (`be77aa36da`, 13.5 to 13.6) and #20037
   (`1fd72c6d05`, 13.6 to 14.0 with an unpublished dashboard-image workaround).
   Prefer the latest applicable history over copying an old diff blindly.
4. Confirm the exact target milestone exists and is open in GitHub. If it is
   missing or closed, ask before creating or reopening it; do not leave the policy
   pointing at a milestone the bot cannot assign.

## Make the focused edit

- Set `MajorVersion`, `MinorVersion`, and `PatchVersion` to the requested version.
  Reset the lower components when advancing to a new major or minor release.
- Keep `VersionPrefix` composed from those three properties.
- Preserve `PreReleaseVersionLabel`, `StabilizePackageVersion`, and
  `DotNetFinalVersionKind` unless the user explicitly requests a prerelease or
  stabilization change. Bumping to `X.Y` does not mean publishing a stable release.
- Preserve an existing `AspireDashboardImageTag` override unless changing it is
  explicitly in scope and the replacement image is confirmed published. A product
  version bump does not publish a dashboard image. Keep its explanatory comment
  accurate without implying that the pinned image matches the new product version.
- Update the milestone policy rule for the target branch to the confirmed milestone
  title. Preserve other branch rules unless a release-branch transition is explicitly
  in scope; do not infer one from a major version bump. The old
  `.github/workflows/milestone-assignment.yml` was removed in #20390 in favor of the
  policy bot; do not recreate it.
- Do not update dependency versions, target frameworks, `global.json`,
  `NuGet.config`, package manifests, generated API baselines, snapshots, or sample
  version literals merely because they contain the old version.
- If evidence shows another file must change, explain the dependency before
  expanding the scope; do not perform a repository-wide version replacement.

## Validate

Review `git diff --check` and the complete diff. Confirm only the intended version
properties, milestone assignment, and any directly related comments changed.

For a metadata-only bump, parse the XML and assert that the major, minor, and patch
properties match the requested version and `VersionPrefix` still composes those
properties. This does not require restoring or building the entire repository.

Parse the milestone policy as YAML and verify the target branch maps to the
confirmed open milestone and all other rules are unchanged.

Check the diff separately to confirm prerelease settings and the dashboard-image
pin were preserved. If the change extends beyond metadata, run focused validation
for that behavior and follow the repository's SDK setup instructions when needed.

## Create the PR when requested

Use the `create-pr` skill and the repository PR template. Commit only the intended
files, include required commit trailers, push without force, and use the available
PR creation tool when the environment requires it. Describe the old and new
versions, milestone routing, unchanged prerelease status, any retained
dashboard-image override, and the validation actually performed.
Do not claim a full build or test run unless
one was performed. Do not merge automatically.
