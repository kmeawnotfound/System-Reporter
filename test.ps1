<#
.SYNOPSIS
    Smoke tests for a built SystemReport.exe.

.DESCRIPTION
    Exercises the command-line contract without touching the network, elevation,
    or the interactive menu: version/help, option errors, text/minimal/JSON output,
    redaction, nested output folders, and exit codes.

    The tests write only to a temporary directory. On success that directory is
    removed; on failure it is kept so the generated reports can be inspected.

.PARAMETER ExePath
    Path to SystemReport.exe. Defaults to the executable next to this script.

.PARAMETER KeepArtifacts
    Keep the temporary test directory even when all tests pass.

.EXAMPLE
    .\test.ps1

.EXAMPLE
    .\test.ps1 -ExePath .\SystemReport.exe -KeepArtifacts
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Root = $PSScriptRoot
if ([string]::IsNullOrEmpty($script:Root)) {
    $script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrEmpty($ExePath)) {
    $ExePath = Join-Path $script:Root 'SystemReport.exe'
}
$script:ExePath = [System.IO.Path]::GetFullPath($ExePath)
$script:SourcePath = Join-Path $script:Root 'SystemReport.cs'

$script:Passed = 0
$script:Failed = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message (expected '$Expected', got '$Actual')"
    }
}

function Assert-Match {
    param([string]$Pattern, [string]$Text, [string]$Message)
    if ($Text -notmatch $Pattern) {
        throw "$Message (pattern '$Pattern' not found in '$Text')"
    }
}

function Assert-File {
    param([string]$Path, [string]$Message)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "$Message (missing file: $Path)"
    $length = (Get-Item -LiteralPath $Path).Length
    Assert-True ($length -gt 0) "$Message (file is empty: $Path)"
}

function Invoke-Exe {
    param([string[]]$Arguments)
    $output = & $script:ExePath @Arguments 2>&1
    $code = $LASTEXITCODE
    [pscustomobject]@{
        Output = (($output | Out-String).TrimEnd())
        Code   = $code
    }
}

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:Passed++
        Write-Host "  [PASS] $Name" -ForegroundColor Green
    }
    catch {
        $script:Failed++
        $message = $_.Exception.Message
        $script:Failures.Add("$Name : $message")
        Write-Host "  [FAIL] $Name" -ForegroundColor Red
        Write-Host "         $message" -ForegroundColor DarkGray
    }
}

if (-not (Test-Path -LiteralPath $script:ExePath -PathType Leaf)) {
    Write-Host "SystemReport.exe not found: $script:ExePath" -ForegroundColor Red
    Write-Host 'Build it first with .\Build.bat or .\build.ps1.' -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath $script:SourcePath -PathType Leaf)) {
    Write-Host "Source file not found: $script:SourcePath" -ForegroundColor Red
    exit 1
}

$sourceText = [System.IO.File]::ReadAllText($script:SourcePath)
$versionMatch = [regex]::Match($sourceText, 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"')
if (-not $versionMatch.Success) {
    Write-Host 'Could not read Cfg.Version from SystemReport.cs.' -ForegroundColor Red
    exit 1
}
$expectedVersion = $versionMatch.Groups[1].Value
$expectedTool = "SystemReport $expectedVersion"

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SystemReport-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

Write-Host ''
Write-Host "  SystemReport smoke tests" -ForegroundColor Cyan
Write-Host "  Exe:  $script:ExePath"
Write-Host "  Temp: $tempRoot"
Write-Host ''

try {
    Test-Case '--version prints the source version' {
        $r = Invoke-Exe @('--version')
        Assert-Equal 0 $r.Code 'exit code'
        Assert-Equal $expectedTool $r.Output 'version text'
    }

    Test-Case '--help lists the documented options' {
        $r = Invoke-Exe @('--help')
        Assert-Equal 0 $r.Code 'exit code'
        foreach ($needle in @('--minimal', '--json', '--redact', '--out', '--elevate', 'Exit codes')) {
            Assert-True ($r.Output.Contains($needle)) "help is missing '$needle'"
        }
    }

    Test-Case 'unknown option exits with code 2' {
        $r = Invoke-Exe @('--definitely-not-an-option')
        Assert-Equal 2 $r.Code 'exit code'
        Assert-True ($r.Output.Contains('Unknown option')) 'error message is missing'
    }

    Test-Case 'missing -o value exits with code 2' {
        $r = Invoke-Exe @('-o')
        Assert-Equal 2 $r.Code 'exit code'
        Assert-True ($r.Output.Contains('Missing file name')) 'error message is missing'
    }

    Test-Case 'full text report is written' {
        $path = Join-Path $tempRoot 'full.txt'
        $r = Invoke-Exe @('-n', '-o', $path)
        Assert-Equal 0 $r.Code 'exit code'
        Assert-File $path 'full report'
        $text = [System.IO.File]::ReadAllText($path)
        Assert-True ($text.Contains('S Y S T E M   R E P O R T')) 'report header is missing'
        Assert-True ($text.ToUpperInvariant().Contains('SUMMARY')) 'summary section is missing'
        Assert-True ($text.ToUpperInvariant().Contains('OPERATING SYSTEM')) 'OS section is missing'
        Assert-True ($text.Contains('End of report')) 'report footer is missing'
    }

    Test-Case 'minimal report is compact and contains health lines' {
        $path = Join-Path $tempRoot 'minimal.txt'
        $r = Invoke-Exe @('-m', '-n', '-o', $path)
        Assert-Equal 0 $r.Code 'exit code'
        Assert-File $path 'minimal report'
        $text = [System.IO.File]::ReadAllText($path)
        Assert-True ($text.Contains('Health:')) 'health line is missing'
        Assert-True ($text.Contains('OS:')) 'OS line is missing'
        Assert-True (-not $text.Contains('S Y S T E M   R E P O R T')) 'minimal output should not contain the boxed header'
    }

    Test-Case 'JSON report parses and contains the core fields' {
        $path = Join-Path $tempRoot 'report.json'
        $r = Invoke-Exe @('-j', '-n', '-o', $path)
        Assert-Equal 0 $r.Code 'exit code'
        Assert-File $path 'JSON report'
        $json = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        Assert-Equal $expectedTool $json.tool 'tool field'
        Assert-True ($json.computer -is [string]) 'computer field is missing'
        Assert-True ($json.admin -is [bool]) 'admin field is not a boolean'
        Assert-True ($json.duration_sec -is [double] -or $json.duration_sec -is [decimal] -or $json.duration_sec -is [int] -or $json.duration_sec -is [long]) 'duration_sec is not numeric'
        Assert-True ($null -ne $json.sections.Summary) 'Summary section is missing'
        Assert-True ($null -ne $json.sections.'Operating System') 'Operating System section is missing'
        Assert-True ($null -ne $json.warnings) 'warnings array is missing'
        Assert-True ($null -ne $json.notes) 'notes array is missing'
    }

    Test-Case 'redaction hides the PC name and personal fields' {
        $path = Join-Path $tempRoot 'redacted.json'
        $r = Invoke-Exe @('-j', '-r', '-n', '-o', $path)
        Assert-Equal 0 $r.Code 'exit code'
        Assert-File $path 'redacted JSON report'
        $json = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        Assert-Equal '[redacted]' $json.computer 'computer field'

        $os = $json.sections.'Operating System'
        if ($os.PSObject.Properties.Name -contains 'Current user') {
            Assert-Equal '[redacted]' $os.'Current user' 'current user'
        }

        $machine = $json.sections.'Machine & Firmware'
        if ($machine.PSObject.Properties.Name -contains 'Serial number') {
            $serial = $machine.'Serial number'
            Assert-True ($serial -eq '[redacted]' -or $serial -eq 'N/A') "machine serial number was not redacted (got '$serial')"
        }

        $network = $json.sections.Network
        foreach ($property in $network.PSObject.Properties) {
            if ($property.Value -isnot [PSCustomObject]) { continue }
            foreach ($field in @('MAC address', 'IPv4', 'IPv6', 'Gateway', 'DNS', 'DNS suffix', 'Connected to', 'BSSID')) {
                if ($property.Value.PSObject.Properties.Name -notcontains $field) { continue }
                $value = $property.Value.$field
                if ($value -eq 'N/A') { continue }
                Assert-Equal '[redacted]' $value "$($property.Name) / $field"
            }
        }
    }

    Test-Case 'nested output folders are created' {
        $path = Join-Path (Join-Path $tempRoot 'nested folder') (Join-Path 'deeper' 'report.json')
        $r = Invoke-Exe @('-j', '-n', '-o', $path)
        Assert-Equal 0 $r.Code 'exit code'
        Assert-File $path 'nested JSON report'
    }

    Test-Case '--out=FILE syntax is accepted' {
        $path = Join-Path $tempRoot 'out-equals.txt'
        $r = Invoke-Exe @("--out=$path", '-n')
        Assert-Equal 0 $r.Code 'exit code'
        Assert-File $path 'report written with --out='
    }

    Test-Case 'a directory as the output path fails with code 1' {
        $r = Invoke-Exe @('-n', '-o', $tempRoot)
        Assert-Equal 1 $r.Code 'exit code'
        Assert-True ($r.Output.Contains('Could not write')) 'error message is missing'
    }
}
finally {
    $summaryColor = if ($script:Failed -eq 0) { 'Green' } else { 'Red' }
    Write-Host ''
    Write-Host "  $($script:Passed) passed, $($script:Failed) failed" -ForegroundColor $summaryColor

    if ($script:Failed -gt 0) {
        Write-Host ''
        foreach ($failure in $script:Failures) {
            Write-Host "  - $failure" -ForegroundColor Red
        }
    }

    if ($KeepArtifacts -or $script:Failed -gt 0) {
        Write-Host "  Test files kept: $tempRoot"
    }
    else {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host ''
}

if ($script:Failed -gt 0) { exit 1 }
exit 0
