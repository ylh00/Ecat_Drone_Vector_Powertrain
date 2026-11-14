<#
.SYNOPSIS
    Add HPM5E3Y build support to hpm_sdk v1.13.0 (optional).

.DESCRIPTION
    hpm_sdk v1.13.0 ships the complete header set for the HPM5E3Y SoC
    (soc\HPM5E00\HPM5E3Y\hpm_soc.h, hpm_iomux.h, hpm_soc_ip.h, ...), which are
    byte-identical to the HPM5E31 headers, but it does not ship the build
    support files (system.c, hpm_clock_drv.c, linker scripts, boot header,
    CMakeLists.txt).

    The board package `ecat_drone_5e3y` therefore uses `soc: HPM5E31` in
    ecat_drone_5e3y.yaml, which builds the exact same IP with the HPM5E31
    support files.

    Run this script ONLY if you want the build to use the HPM5E3Y directory
    explicitly:  it copies every missing file from HPM5E31 into HPM5E3Y, then
    change `soc: HPM5E31` to `soc: HPM5E3Y` in the board yaml.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File add_hpm5e3y_soc_support.ps1
#>
[CmdletBinding()]
param(
    [string]$SdkEnv = ''
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if ([string]::IsNullOrEmpty($SdkEnv)) {
    $SdkEnv = (Resolve-Path (Join-Path $scriptDir '..')).Path
}

$src = Join-Path $SdkEnv 'hpm_sdk\soc\HPM5E00\HPM5E31'
$dst = Join-Path $SdkEnv 'hpm_sdk\soc\HPM5E00\HPM5E3Y'

if (-not (Test-Path $src)) { throw "source not found: $src" }
if (-not (Test-Path $dst)) { throw "destination not found: $dst" }

Write-Host "== HPM5E31 : $src"
Write-Host "== HPM5E3Y : $dst"

# files that must exist for a CMake build
$required = @(
    'CMakeLists.txt',
    'system.c',
    'hpm_soc_feature.h',
    'hpm_clock_drv.c',
    'hpm_clock_drv.h',
    'hpm_otp_drv.c',
    'hpm_otp_drv.h',
    'hpm_pdgo_drv.c',
    'hpm_pdgo_drv.h',
    'hpm_pcfg_drv.h',
    'hpm_plic_drv.h',
    'hpm_ppor_drv.h',
    'hpm_qeiv2_soc_drv.c',
    'hpm_qeiv2_soc_drv.h',
    'hpm_sysctl_drv.c',
    'hpm_sysctl_drv.h',
    'hpm_trgm_soc_drv.c',
    'hpm_trgm_soc_drv.h',
    'hpm_enet_soc_drv.h',
    'hpm_gpiom_soc_drv.h',
    'hpm_mcan_soc.h',
    'hpm_misc.h',
    'hpm_romapi.h',
    'hpm_romapi_xpi_soc_def.h'
)

$copied = 0
foreach ($f in $required) {
    $s = Join-Path $src $f
    $d = Join-Path $dst $f
    if (-not (Test-Path $s)) { Write-Host "WARN missing in HPM5E31: $f"; continue }
    if (Test-Path $d) { Write-Host "skip (exists): $f"; continue }
    Copy-Item $s $d
    Write-Host "copied: $f"
    $copied++
}

# sub directories: boot/ toolchains/
foreach ($dir in @('boot', 'toolchains')) {
    $s = Join-Path $src $dir
    $d = Join-Path $dst $dir
    if (-not (Test-Path $s)) { continue }
    Get-ChildItem -Path $s -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($s.Length).TrimStart('\')
        $target = Join-Path $d $rel
        if (Test-Path $target) { return }
        $parent = Split-Path -Parent $target
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        Copy-Item $_.FullName $target
        Write-Host "copied: $dir\$rel"
        $script:copied++
    }
}

Write-Host ""
Write-Host "== $copied file(s) copied"
Write-Host "== now edit ..\..\user_template\boards\ecat_drone_5e3y\ecat_drone_5e3y.yaml"
Write-Host "   and set:  soc: HPM5E3Y"
Write-Host "== then rebuild with scripts\build.cmd"
