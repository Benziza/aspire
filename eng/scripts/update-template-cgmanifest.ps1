<#
.SYNOPSIS
Updates or verifies the checked-in template component manifest using packages from this build.
.DESCRIPTION
Verification is the default. CI passes -ChangesOnly to skip when the compared commits have
no manifest inputs in common with eng/template-cg-inputs.txt. Unknown history verifies conservatively.
#>
[CmdletBinding()]
param(
    [switch]$Update,
    [switch]$ChangesOnly,
    [string]$BaseRef = $env:TEMPLATE_CG_BASE_SHA,
    [string]$HeadRef = 'HEAD',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$PackageDirectory,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
)

$ErrorActionPreference = 'Stop'
if ($Update -and $ChangesOnly) {
    throw '-Update and -ChangesOnly cannot be combined.'
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$manifestPath = Join-Path $RepositoryRoot 'cgmanifest.json'

function Resolve-Commit([string]$Ref) {
    $value = & git -C $RepositoryRoot rev-parse --verify --end-of-options "${Ref}^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0) { return ($value | Select-Object -Last 1) }
    return $null
}

if ($ChangesOnly -and (Test-Path -LiteralPath $manifestPath)) {
    $head = Resolve-Commit $HeadRef
    if ([string]::IsNullOrWhiteSpace($BaseRef)) {
        # On PR merge checkouts, the first parent is the actual target revision: this compares
        # the whole PR, not just its final commit. On ordinary CI commits it compares the parent.
        $BaseRef = "$HeadRef^1"
    }
    $base = Resolve-Commit $BaseRef
    if (-not $base -and $BaseRef -match '^[0-9a-fA-F]{40}$' -and $BaseRef -notmatch '^0+$') {
        # A multi-commit GitHub push supplies event.before, which may be outside a depth-two clone.
        & git -C $RepositoryRoot fetch --no-tags --depth=1 origin $BaseRef
        if ($LASTEXITCODE -eq 0) { $base = Resolve-Commit $BaseRef }
    }
    if ($base -and $head) {
        $paths = @(& git -C $RepositoryRoot diff --name-only --no-renames "$base..$head" --)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine changed manifest inputs.' }
        $patterns = @(Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/template-cg-inputs.txt') |
            ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
        if ($patterns.Count -eq 0) { throw 'The template manifest input list is empty.' }
        $expressions = @($patterns | ForEach-Object {
            # **/ matches zero or more directories; * matches within one directory.
            '^' + [regex]::Escape($_).Replace('\*\*/', '(?:.*/)?').Replace('\*\*', '.*').Replace('\*', '[^/]*') + '$'
        })
        $changed = @($paths | Where-Object {
            $path = $_
            @($expressions | Where-Object { $path -cmatch $_ }).Count -gt 0
        })
        if ($changed.Count -eq 0) {
            Write-Host 'Template manifest inputs unchanged; skipping verification.'
            exit 0
        }
        Write-Host "Changed template manifest inputs: $($changed -join ', ')"
    } else {
        Write-Warning 'Commit history is unavailable; verifying the template manifest rather than skipping.'
    }
}

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $RepositoryRoot 'artifacts' 'packages' $Configuration 'Shipping'
}
if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Shipping packages not found at '$PackageDirectory'. Run the managed build with -pack first."
}
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$templatePackages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'Aspire.ProjectTemplates.*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' })
if ($templatePackages.Count -ne 1) {
    throw "Expected exactly one Aspire.ProjectTemplates package in '$PackageDirectory'; found $($templatePackages.Count). Remove stale template package versions."
}

# Read the version from the same-build package, not the environment's PR/daily suffix.
# No builds, installs, or restores of templated applications are performed here.
$archive = [IO.Compression.ZipFile]::OpenRead($templatePackages[0].FullName)
try {
    $entries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
    if ($entries.Count -ne 1) { throw 'Template package must contain exactly one nuspec.' }
    $stream = $entries[0].Open()
    try {
        $nuspec = [Xml.Linq.XDocument]::Load($stream)
        $ns = $nuspec.Root.Name.Namespace
        $version = $nuspec.Root.Element($ns + 'metadata').Element($ns + 'version').Value
    } finally { $stream.Dispose() }
} finally { $archive.Dispose() }

$target = if ($Update) { 'GenerateTemplateCgManifest' } else { 'VerifyTemplateCgManifest' }
$project = Join-Path $RepositoryRoot 'src' 'Aspire.ProjectTemplates' 'Aspire.ProjectTemplates.csproj'
$dotnet = if ($IsWindows) { Join-Path $RepositoryRoot 'dotnet.cmd' } else { Join-Path $RepositoryRoot 'dotnet.sh' }
& $dotnet msbuild $project "-target:$target" "-property:Configuration=$Configuration" "-property:PackageVersion=$version" `
    "-property:TemplateCgPackageDirectory=$PackageDirectory" -verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Template manifest $target failed. To update: pwsh eng/scripts/update-template-cgmanifest.ps1 -Update -Configuration $Configuration"
}
