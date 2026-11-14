<#
    ecat_drone_host - build script (PowerShell)

    Compiles the C# WinForms host tool with the .NET Framework csc.exe.
    This machine has no dotnet SDK / MSBuild / NuGet, but every Windows with
    .NET Framework 4.x ships the command line C# compiler.

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Selftest
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Clean

    Exit code 0 = build (and optional selftest) succeeded.
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Selftest
)

$ErrorActionPreference = 'Stop'

# --- locate this script's directory (never rely on the current directory) -----
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrEmpty($scriptDir)) {
    Write-Host '[ERROR] cannot determine the script directory' -ForegroundColor Red
    exit 1
}
$scriptDir = (Resolve-Path -LiteralPath $scriptDir).Path

$srcDir = Join-Path $scriptDir 'src'
$outDir = Join-Path $scriptDir 'build'
$outExe = Join-Path $outDir 'ecat_drone_host.exe'

Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host ' ecat_drone_host build (C# WinForms, .NET Framework csc.exe)' -ForegroundColor Cyan
Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host (" project : " + $scriptDir)
Write-Host ''

if (-not (Test-Path -LiteralPath $srcDir)) {
    Write-Host ("[ERROR] source directory not found: " + $srcDir) -ForegroundColor Red
    exit 1
}

# --- find csc.exe -------------------------------------------------------------
$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v3.5\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v3.5\csc.exe')
)
$csc = $null
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
        $csc = $candidate
        break
    }
}
if ($null -eq $csc) {
    Write-Host '[ERROR] csc.exe (.NET Framework C# compiler) not found.' -ForegroundColor Red
    Write-Host '        Install .NET Framework 4.x or build on a machine with Visual Studio.' -ForegroundColor Red
    exit 1
}
Write-Host (" compiler: " + $csc)

# --- output directory ---------------------------------------------------------
if ($Clean -and (Test-Path -LiteralPath $outDir)) {
    Write-Host ' clean   : removing build directory'
    Remove-Item -LiteralPath $outDir -Recurse -Force
}
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

# --- collect sources ----------------------------------------------------------
$sources = @(Get-ChildItem -LiteralPath $srcDir -Recurse -Filter '*.cs' |
             Sort-Object FullName |
             ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    Write-Host '[ERROR] no C# sources under src\' -ForegroundColor Red
    exit 1
}
Write-Host (" sources : " + $sources.Count + " file(s)")

# --- references ---------------------------------------------------------------
# On .NET Framework, System.IO.Ports.SerialPort lives in System.dll (there is no
# separate System.IO.Ports.dll).  The optional reference keeps the script
# forward compatible with a .NET Framework installation that has the facade.
$referenceNames = @(
    'System.dll',
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    'System.Core.dll',
    'Microsoft.CSharp.dll'
)
$cscDir = Split-Path -Parent $csc
$referenceArgs = @()
foreach ($name in $referenceNames) {
    $referenceArgs += ('/r:' + $name)
    if (-not (Test-Path -LiteralPath (Join-Path $cscDir $name))) {
        # resolved from the framework / GAC by csc itself; only warn for the
        # assemblies that must exist next to the compiler
        if ($name -eq 'System.Windows.Forms.dll' -or $name -eq 'System.Drawing.dll') {
            Write-Host ("[WARN] " + $name + " not found next to csc.exe; the compiler may still resolve it") -ForegroundColor Yellow
        }
    }
}
$portsFacade = Join-Path $cscDir 'System.IO.Ports.dll'
if (Test-Path -LiteralPath $portsFacade) {
    $referenceArgs += ('/r:' + $portsFacade)
    Write-Host ' note    : referencing System.IO.Ports.dll facade (SerialPort inside)'
}

# --- compile ------------------------------------------------------------------
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/warn:4',
    '/optimize+',
    '/utf8output',
    ('/out:' + $outExe)
)
$cscArgs += $referenceArgs
$cscArgs += $sources

Write-Host ''
Write-Host ' compiling ...'
$output = & $csc $cscArgs 2>&1
$exitCode = $LASTEXITCODE
if ($output) {
    $output | ForEach-Object { Write-Host $_ }
}
if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host ('[BUILD FAILED] csc.exe exit code ' + $exitCode) -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath $outExe)) {
    Write-Host '[BUILD FAILED] csc.exe did not produce the executable' -ForegroundColor Red
    exit 1
}

$exeInfo = Get-Item -LiteralPath $outExe
Write-Host ''
Write-Host ('[BUILD OK] ' + $outExe) -ForegroundColor Green
Write-Host ('           size: ' + $exeInfo.Length + ' bytes, written: ' + $exeInfo.LastWriteTime)
Write-Host ''

# --- optional self test -------------------------------------------------------
if ($Selftest) {
    Write-Host ' running --selftest ...'
    $log = Join-Path $outDir 'selftest_console.txt'
    $errLog = Join-Path $outDir 'selftest_console.err.txt'
    $process = Start-Process -FilePath $outExe -ArgumentList '--selftest' -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError $errLog
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    }
    if ((Test-Path -LiteralPath $errLog) -and ((Get-Item -LiteralPath $errLog).Length -gt 0)) {
        Write-Host '--- stderr ---' -ForegroundColor Yellow
        Get-Content -LiteralPath $errLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    }
    if ($process.ExitCode -ne 0) {
        Write-Host ''
        Write-Host ('[SELFTEST FAIL] exit code ' + $process.ExitCode) -ForegroundColor Red
        exit 1
    }
    Write-Host ''
    Write-Host '[SELFTEST PASS]' -ForegroundColor Green
}

exit 0
