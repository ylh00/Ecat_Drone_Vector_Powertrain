# Copyright (c) 2026 HPMicro
# SPDX-License-Identifier: BSD-3-Clause
#
# Build helper for the HPM SDK Windows environment.
#
# Usage (PowerShell):
#   .\build.ps1                        # build the default project (debug)
#   .\build.ps1 -Clean                 # remove the build directory first
#   .\build.ps1 -Config release        # CMAKE_BUILD_TYPE=release
#   .\build.ps1 -BuildType ram         # HPM_BUILD_TYPE (ram / flash_xip / ...)
#   .\build.ps1 -Project ..\projects\ecat_drone_powertrain -Board ecat_drone_5e3y
#
# If script execution is disabled on this machine use build.cmd instead, or:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#
# The script sets HPM_SDK_BASE / GNURISCV_TOOLCHAIN_PATH / PATH exactly like
# start_cmd.cmd does, so it can be called from any PowerShell window.

[CmdletBinding()]
param(
    [string]$SdkEnv = '',
    [string]$Project = '',
    [string]$Board = 'ecat_drone_5e3y',
    [string]$BuildType = 'flash_xip',
    [string]$Config = 'debug',
    [string]$BuildDir = '',
    [switch]$Clean,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = (Get-Location).Path }

# --- environment -----------------------------------------------------------
if ([string]::IsNullOrEmpty($SdkEnv)) {
    $SdkEnv = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
}
if ([string]::IsNullOrEmpty($Project)) {
    $Project = (Resolve-Path (Join-Path $scriptDir '..\projects\ecat_drone_powertrain')).Path
}

$sdkBase   = Join-Path $SdkEnv 'hpm_sdk'
$toolchain = Join-Path $SdkEnv 'toolchains\rv32imac_zicsr_zifencei_multilib_b_ext-win'
$cmakeExe  = Join-Path $SdkEnv 'tools\cmake\bin\cmake.exe'
$ninjaDir  = Join-Path $SdkEnv 'tools\ninja'
$pythonDir = Join-Path $SdkEnv 'tools\python3'

foreach ($p in @($sdkBase, $toolchain, $cmakeExe, $ninjaDir, $pythonDir)) {
    if (-not (Test-Path $p)) { throw "not found: $p (is -SdkEnv pointing to the sdk_env root?)" }
}

$env:HPM_SDK_BASE = $sdkBase
$env:GNURISCV_TOOLCHAIN_PATH = $toolchain
$env:HPM_SDK_TOOLCHAIN_VARIANT = ''
$env:PATH = "$(Join-Path $SdkEnv 'tools\cmake\bin');$ninjaDir;$pythonDir;$toolchain\bin;$env:PATH"
$env:PYTHONPATH = Join-Path $pythonDir 'Lib\site-packages'

$boardSearchPath = Join-Path $SdkEnv 'user_template\boards'

if ([string]::IsNullOrEmpty($BuildDir)) {
    $BuildDir = Join-Path $Project "build_${Board}_${BuildType}_${Config}"
}

if ($Clean -and (Test-Path $BuildDir)) {
    Write-Host "== removing $BuildDir"
    Remove-Item -Recurse -Force $BuildDir
}

Write-Host "== sdk env    : $SdkEnv"
Write-Host "== project    : $Project"
Write-Host "== board      : $Board"
Write-Host "== build type : $BuildType / $Config"
Write-Host "== build dir  : $BuildDir"

# --- configure -------------------------------------------------------------
$configureArgs = @(
    '-G', 'Ninja',
    '-S', $Project,
    '-B', $BuildDir,
    "-DBOARD=$Board",
    "-DBOARD_SEARCH_PATH=$boardSearchPath",
    "-DHPM_BUILD_TYPE=$BuildType",
    "-DCMAKE_BUILD_TYPE=$Config"
)
if ($VerboseOutput) { $configureArgs += '-DCMAKE_VERBOSE_MAKEFILE=ON' }

& $cmakeExe @configureArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($LASTEXITCODE)" }

# --- build -----------------------------------------------------------------
& $cmakeExe --build $BuildDir
if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($LASTEXITCODE)" }

$elf = Get-ChildItem -Path $BuildDir -Recurse -Filter '*.elf' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($elf) {
    Write-Host ""
    Write-Host "== BUILD OK: $($elf.FullName)"
    Write-Host ("== ELF size: {0:N0} bytes" -f (Get-Item $elf.FullName).Length)
    & (Join-Path $toolchain 'bin\riscv32-unknown-elf-size.exe') $elf.FullName
} else {
    throw "build finished but no .elf was produced"
}
