<#
.SYNOPSIS
    Prepare and tag a SystemReport release.

.DESCRIPTION
    Updates the version constants in SystemReport.cs, verifies that CHANGELOG.md
    already contains an entry for the new version, runs a clean build and the smoke
    tests, commits the result, creates the release package, and creates an annotated
    Git tag.

    The working tree must be clean before the release starts. Use -DryRun to see the
    plan without changing anything.

.PARAMETER Version
    New semantic version, for example 1.2.0 (a leading "v" is accepted).

.PARAMETER SkipTests
    Build and package without running test.ps1. Not recommended.

.PARAMETER NoTag
    Commit the release but do not create the Git tag.

.PARAMETER DryRun
    Validate the version and changelog, print the plan, and make no changes.

.PARAMETER Force
    Allow re-releasing the version that is already in the source. The Git tag must
    still be new.

.PARAMETER Message
    Custom Git commit message. Defaults to "Release v<version>".

.EXAMPLE
    .\release.ps1 -Version 1.2.0 -DryRun

.EXAMPLE
    .\release.ps1 -Version 1.2.0

.NOTES
    Before running, add a CHANGELOG.md section:

        ## [1.2.0] - 2026-10-23

    Then run the release. Push the commit and tag with:

        git push origin main --tags
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Version,
    [switch]$SkipTests,
    [switch]$NoTag,
    [switch]$DryRun,
    [switch]$Force,
    [string]$Message
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Root = $PSScriptRoot
if ([string]::IsNullOrEmpty($script:Root)) {
    $script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$script:SourcePath = Join-Path $script:Root 'SystemReport.cs'
$script:ChangelogPath = Join-Path $script:Root 'CHANGELOG.md'
$script:BuildScript = Join-Path $script:Root 'build.ps1'

function Stop-Release {
    param([string]$Message)
    Write-Host ''
    Write-Host "  RELEASE FAILED: $Message" -ForegroundColor Red
    Write-Host ''
    exit 1
}

function Get-VersionValue {
    param([string]$Text, [string]$Pattern, [string]$Label)
    $matches = [regex]::Matches($Text, $Pattern)
    if ($matches.Count -ne 1) {
        Stop-Release "Could not find exactly one $Label in SystemReport.cs."
    }
    return $matches[0].Groups[1].Value
}

function Set-VersionValue {
    param([string]$Text, [string]$Pattern, [string]$NewValue, [string]$Label)
    $matches = [regex]::Matches($Text, $Pattern)
    if ($matches.Count -ne 1) {
        Stop-Release "Could not find exactly one $Label in SystemReport.cs."
    }
    $group = $matches[0].Groups[1]
    return $Text.Substring(0, $group.Index) + $NewValue + $Text.Substring($group.Index + $group.Length)
}

$Version = $Version.Trim()
if ($Version.StartsWith('v') -or $Version.StartsWith('V')) {
    $Version = $Version.Substring(1)
}
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    Stop-Release "Version must be semantic version core such as 1.2.0 (got '$Version')."
}
$assemblyVersion = "$Version.0"

foreach ($path in @($script:SourcePath, $script:ChangelogPath, $script:BuildScript)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Stop-Release "Required file not found: $path"
    }
}

$repoCheck = (& git -C $script:Root rev-parse --is-inside-work-tree 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $repoCheck -ne 'true') {
    Stop-Release 'This directory is not a Git repository. Run git init first.'
}

$sourceText = [System.IO.File]::ReadAllText($script:SourcePath)
$currentVersion = Get-VersionValue $sourceText 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"' 'Cfg.Version constant'
$currentAssembly = Get-VersionValue $sourceText '\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]' 'AssemblyVersion attribute'
$currentFile = Get-VersionValue $sourceText '\[assembly:\s*AssemblyFileVersion\("([^"]+)"\)\]' 'AssemblyFileVersion attribute'

if ($currentAssembly -ne "$currentVersion.0" -or $currentFile -ne "$currentVersion.0") {
    Stop-Release "Current version constants are inconsistent: Cfg.Version=$currentVersion, AssemblyVersion=$currentAssembly, AssemblyFileVersion=$currentFile."
}

if ($currentVersion -eq $Version -and -not $Force) {
    Stop-Release "SystemReport.cs is already at $Version. Use -Force only if you intentionally want to re-release it."
}

$changelogText = [System.IO.File]::ReadAllText($script:ChangelogPath)
$changelogPattern = '(?m)^##\s*\[' + [regex]::Escape($Version) + '\]'
if ($changelogText -notmatch $changelogPattern) {
    Stop-Release "CHANGELOG.md has no '## [$Version]' section. Add the release notes first."
}

if (-not $NoTag) {
    & git -C $script:Root rev-parse -q --verify "refs/tags/v$Version" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Stop-Release "Git tag v$Version already exists."
    }
}

if (-not $DryRun) {
    $status = (& git -C $script:Root status --porcelain 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { Stop-Release 'Could not read Git status.' }
    if (-not [string]::IsNullOrEmpty($status)) {
        Stop-Release "The working tree is not clean. Commit or stash changes first:`n$status"
    }
}

Write-Host ''
Write-Host "  SystemReport release $Version" -ForegroundColor Cyan
Write-Host "  Current source version: $currentVersion"
Write-Host "  New assembly version:   $assemblyVersion"
Write-Host ''

if ($DryRun) {
    $plannedMessage = if ([string]::IsNullOrEmpty($Message)) { "Release v$Version" } else { $Message }
    Write-Host '  Plan (no changes made):'
    Write-Host '    1. Update Cfg.Version, AssemblyVersion, and AssemblyFileVersion in SystemReport.cs'
    Write-Host "    2. Run build.ps1 -Clean$(if ($SkipTests) { '' } else { ' -Test' })"
    Write-Host "    3. Commit as '$plannedMessage'"
    Write-Host "    4. Create artifacts\SystemReport-$Version-win.zip and its SHA-256 file"
    if ($NoTag) { Write-Host '    5. Skip the Git tag (-NoTag)' }
    else { Write-Host "    5. Create annotated tag v$Version" }
    Write-Host ''
    Write-Host '  Dry run complete. No files were changed.' -ForegroundColor Green
    Write-Host ''
    exit 0
}

$updated = $sourceText
$updated = Set-VersionValue $updated 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"' $Version 'Cfg.Version constant'
$updated = Set-VersionValue $updated '\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]' $assemblyVersion 'AssemblyVersion attribute'
$updated = Set-VersionValue $updated '\[assembly:\s*AssemblyFileVersion\("([^"]+)"\)\]' $assemblyVersion 'AssemblyFileVersion attribute'

$bytes = [System.IO.File]::ReadAllBytes($script:SourcePath)
$hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
$encoding = New-Object System.Text.UTF8Encoding -ArgumentList $hasBom
[System.IO.File]::WriteAllText($script:SourcePath, $updated, $encoding)
Write-Host "  Updated SystemReport.cs to $Version (assembly $assemblyVersion)."

$hostExe = $null
try { $hostExe = (Get-Process -Id $PID).Path } catch { }
if ([string]::IsNullOrEmpty($hostExe)) { $hostExe = 'powershell.exe' }

Write-Host $(if ($SkipTests) { '  Building ...' } else { '  Building and testing ...' })
$buildArgs = @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:BuildScript, '-Clean')
if (-not $SkipTests) { $buildArgs += '-Test' }
$buildCode = 1
try {
    & $hostExe @buildArgs
    $buildCode = $LASTEXITCODE
}
catch {
    $buildCode = 1
}
if ($buildCode -ne 0) {
    [System.IO.File]::WriteAllText($script:SourcePath, $sourceText, $encoding)
    Stop-Release 'Build or tests failed. SystemReport.cs was restored to its previous version; fix the problem and run the release again.'
}

Write-Host '  Committing ...'
& git -C $script:Root add -A
if ($LASTEXITCODE -ne 0) { Stop-Release 'git add failed. The version bump is still in the working tree; inspect git status.' }

$commitMessage = if ([string]::IsNullOrEmpty($Message)) { "Release v$Version" } else { $Message }
& git -C $script:Root commit -m $commitMessage
if ($LASTEXITCODE -ne 0) { Stop-Release 'git commit failed. The version bump is still in the working tree; inspect git status.' }

$commit = (& git -C $script:Root rev-parse --short HEAD | Out-String).Trim()

Write-Host '  Creating release package ...'
$packagePath = Join-Path (Join-Path $script:Root 'artifacts') "SystemReport-$Version-win.zip"
$packageArgs = @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:BuildScript, '-Package', '-Quiet')
& $hostExe @packageArgs
if ($LASTEXITCODE -ne 0) {
    Stop-Release "Commit $commit was created, but packaging failed. Fix the problem, run .\build.ps1 -Package, then create tag v$Version manually."
}

if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    Stop-Release "Expected package was not created: $packagePath"
}

if (-not $NoTag) {
    Write-Host "  Tagging v$Version ..."
    & git -C $script:Root tag -a "v$Version" -m "SystemReport $Version"
    if ($LASTEXITCODE -ne 0) {
        Stop-Release "Commit $commit and the package were created, but tag v$Version could not be created."
    }
}

$branch = (& git -C $script:Root rev-parse --abbrev-ref HEAD | Out-String).Trim()
$remote = (& git -C $script:Root remote | Out-String).Trim()

Write-Host ''
Write-Host "  Released SystemReport $Version" -ForegroundColor Green
Write-Host "  Commit:  $commit"
if ($NoTag) { Write-Host '  Tag:     skipped (-NoTag)' }
else { Write-Host "  Tag:     v$Version" }
Write-Host "  Package: $packagePath"
Write-Host "  SHA-256: ${packagePath}.sha256"
Write-Host ''
if ([string]::IsNullOrEmpty($remote)) {
    Write-Host '  No Git remote is configured. Add one with:'
    Write-Host '    git remote add origin <url>'
}
Write-Host "  Push with: git push origin $branch --tags"
Write-Host ''

exit 0
