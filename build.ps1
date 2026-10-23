<#
.SYNOPSIS
    Build, test, and package SystemReport.

.DESCRIPTION
    Orchestrates the dependency-free bootstrap build (Build.bat), verifies that the
    version constants agree, optionally runs the smoke tests, and optionally creates
    a release zip with a SHA-256 checksum.

    The compiler is the csc.exe that ships with the .NET Framework 4.x. No SDK,
    NuGet, Visual Studio, or administrator rights are required.

.PARAMETER Clean
    Remove the previous executable and the artifacts directory before building.

.PARAMETER Test
    Run test.ps1 after a successful build.

.PARAMETER Package
    Create artifacts\SystemReport-<version>-win.zip and a .sha256 file.

.PARAMETER Quiet
    Print only errors and the final artifact paths.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Clean -Test -Package

.EXAMPLE
    .\build.ps1 -q
#>
[CmdletBinding()]
param(
    [Alias('c')][switch]$Clean,
    [Alias('t')][switch]$Test,
    [Alias('p')][switch]$Package,
    [Alias('q')][switch]$Quiet,
    [Alias('h', '?')][switch]$Help
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Root = $PSScriptRoot
if ([string]::IsNullOrEmpty($script:Root)) {
    $script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$script:SourcePath = Join-Path $script:Root 'SystemReport.cs'
$script:ExePath = Join-Path $script:Root 'SystemReport.exe'
$script:ArtifactsPath = Join-Path $script:Root 'artifacts'

function Write-Step {
    param([string]$Message)
    if (-not $Quiet) { Write-Host $Message }
}

function Stop-Build {
    param([string]$Message)
    throw $Message
}

function Show-Help {
    Write-Host @'
SystemReport build system

Usage:
  .\build.ps1 [options]

Options:
  -Clean, -c       remove the previous executable and artifacts before building
  -Test, -t        run the smoke tests after building
  -Package, -p     create a release zip and SHA-256 checksum in artifacts\
  -Quiet, -q       minimal output (errors and final paths only)
  -Help, -h        show this help

Examples:
  .\build.ps1
  .\build.ps1 -Clean -Test
  .\build.ps1 -Clean -Test -Package

Build.bat remains the low-level bootstrap compiler (Build.bat -q for scripts).
'@
}

function Get-SourceVersion {
    if (-not (Test-Path -LiteralPath $script:SourcePath)) {
        Stop-Build "Source file not found: $script:SourcePath"
    }

    $text = [System.IO.File]::ReadAllText($script:SourcePath)
    $display = [regex]::Matches($text, 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"')
    $assembly = [regex]::Matches($text, '\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]')
    $file = [regex]::Matches($text, '\[assembly:\s*AssemblyFileVersion\("([^"]+)"\)\]')

    if ($display.Count -ne 1) { Stop-Build 'Could not find exactly one Cfg.Version constant in SystemReport.cs.' }
    if ($assembly.Count -ne 1) { Stop-Build 'Could not find exactly one AssemblyVersion attribute in SystemReport.cs.' }
    if ($file.Count -ne 1) { Stop-Build 'Could not find exactly one AssemblyFileVersion attribute in SystemReport.cs.' }

    [pscustomobject]@{
        Display  = $display[0].Groups[1].Value
        Assembly = $assembly[0].Groups[1].Value
        File     = $file[0].Groups[1].Value
    }
}

function Assert-Version {
    param($Version)

    if ($Version.Display -notmatch '^\d+\.\d+\.\d+$') {
        Stop-Build "Cfg.Version must be a three-part version such as 1.1.0 (found '$($Version.Display)')."
    }

    $expectedAssembly = "$($Version.Display).0"
    if ($Version.Assembly -ne $expectedAssembly) {
        Stop-Build "AssemblyVersion is '$($Version.Assembly)' but Cfg.Version '$($Version.Display)' requires '$expectedAssembly'."
    }
    if ($Version.File -ne $expectedAssembly) {
        Stop-Build "AssemblyFileVersion is '$($Version.File)' but Cfg.Version '$($Version.Display)' requires '$expectedAssembly'."
    }
}

function Get-CompilerPath {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Get-GitValue {
    param([string[]]$GitArgs)

    try {
        $output = & git -C $script:Root @GitArgs 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return (($output | Out-String).Trim())
    }
    catch { return $null }
}

function Invoke-Clean {
    Write-Step '  Cleaning previous build output ...'
    $pdbPath = [System.IO.Path]::ChangeExtension($script:ExePath, '.pdb')
    foreach ($path in @($script:ExePath, $pdbPath, $script:ArtifactsPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-BootstrapBuild {
    $buildBat = Join-Path $script:Root 'Build.bat'
    if (-not (Test-Path -LiteralPath $buildBat)) {
        Stop-Build "Build.bat not found: $buildBat"
    }

    Write-Step '  Compiling with the in-box .NET Framework compiler ...'
    Push-Location $script:Root
    try {
        & $buildBat -q
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($code -ne 0) { Stop-Build "Build.bat failed with exit code $code." }
    if (-not (Test-Path -LiteralPath $script:ExePath)) {
        Stop-Build "Build.bat reported success but $script:ExePath was not created."
    }
}

function Assert-BinaryVersion {
    param($Version)

    $output = (& $script:ExePath --version 2>&1 | Out-String).Trim()
    $code = $LASTEXITCODE
    if ($code -ne 0) { Stop-Build "SystemReport.exe --version failed with exit code $code." }

    $expected = "SystemReport $($Version.Display)"
    if ($output -ne $expected) {
        Stop-Build "Version check failed: expected '$expected', got '$output'."
    }
}

function Invoke-SmokeTests {
    $testScript = Join-Path $script:Root 'test.ps1'
    if (-not (Test-Path -LiteralPath $testScript)) {
        Stop-Build "test.ps1 not found: $testScript"
    }

    Write-Step '  Running smoke tests ...'
    & $testScript -ExePath $script:ExePath
    $code = $LASTEXITCODE
    if ($code -ne 0) { Stop-Build "Smoke tests failed with exit code $code." }
}

function New-Package {
    param($Version)

    Write-Step '  Creating release package ...'
    if (-not (Test-Path -LiteralPath $script:ArtifactsPath)) {
        New-Item -ItemType Directory -Path $script:ArtifactsPath | Out-Null
    }

    $stage = Join-Path $script:ArtifactsPath ('stage-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null

    try {
        $files = @(
            'SystemReport.exe',
            'SystemReport.cs',
            'Build.bat',
            'build.ps1',
            'test.ps1',
            'release.ps1',
            'README.md',
            'CHANGELOG.md',
            'CONTRIBUTING.md'
        )
        foreach ($name in $files) {
            $source = Join-Path $script:Root $name
            if (Test-Path -LiteralPath $source) {
                Copy-Item -LiteralPath $source -Destination $stage
            }
        }

        $docs = Join-Path $script:Root 'docs'
        if (Test-Path -LiteralPath $docs) {
            Copy-Item -LiteralPath $docs -Destination $stage -Recurse
        }

        $compiler = Get-CompilerPath
        $commit = Get-GitValue @('rev-parse', '--short', 'HEAD')
        if ([string]::IsNullOrEmpty($commit)) { $commit = 'not a git checkout' }
        $describe = Get-GitValue @('describe', '--tags', '--always', '--dirty')
        if ([string]::IsNullOrEmpty($describe)) { $describe = $commit }

        $info = @(
            "SystemReport $($Version.Display)",
            "Built:    $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))",
            "Commit:   $describe",
            "Compiler: $compiler",
            "PowerShell: $($PSVersionTable.PSVersion)",
            "OS:       $([System.Environment]::OSVersion.VersionString)"
        ) -join [Environment]::NewLine

        $infoPath = Join-Path $script:ArtifactsPath 'build-info.txt'
        $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
        [System.IO.File]::WriteAllText($infoPath, $info + [Environment]::NewLine, $utf8NoBom)
        Copy-Item -LiteralPath $infoPath -Destination $stage

        $zipName = "SystemReport-$($Version.Display)-win.zip"
        $zipPath = Join-Path $script:ArtifactsPath $zipName
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal

        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $hashPath = $zipPath + '.sha256'
        [System.IO.File]::WriteAllText($hashPath, "$hash  $zipName" + [Environment]::NewLine, $utf8NoBom)

        Write-Step ''
        Write-Step "  Package: $zipPath"
        Write-Step "  SHA-256: $hashPath"
        Write-Step "  Info:    $infoPath"
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Help) {
    Show-Help
    exit 0
}

try {
    $version = Get-SourceVersion
    Assert-Version $version

    if (-not $Quiet) {
        Write-Host ''
        Write-Host "  SystemReport $($version.Display) build"
        Write-Host "  Source: $script:SourcePath"
        Write-Host ''
    }

    if ($Clean) { Invoke-Clean }

    Invoke-BootstrapBuild
    Assert-BinaryVersion $version
    Write-Step "  Built: $script:ExePath"

    if ($Test) { Invoke-SmokeTests }
    if ($Package) { New-Package $version }

    if (-not $Quiet) {
        Write-Host ''
        Write-Host "  Done. Version $($version.Display)."
        Write-Host ''
    }
    exit 0
}
catch {
    Write-Host ''
    Write-Host "  BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    exit 1
}
