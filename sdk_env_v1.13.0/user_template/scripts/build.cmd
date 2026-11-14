@echo off
rem Copyright (c) 2026 HPMicro
rem SPDX-License-Identifier: BSD-3-Clause
rem
rem Build wrapper for the HPM SDK Windows environment (works with the default
rem restricted PowerShell execution policy).
rem
rem Usage:
rem   build.cmd                  build ecat_drone_powertrain (debug)
rem   build.cmd clean            clean rebuild
rem   build.cmd release          release build
rem   build.cmd ram              build to RAM (debugger download only)
rem   build.cmd clean release ram
rem
rem Any other arguments are forwarded to build.ps1 (e.g. -Board xxx -Project yyy).
setlocal
rem NOTE: "shift" also shifts %0, so the script directory MUST be saved first.
set "SCRIPT_DIR=%~dp0"
set "ARGS="

:parse
if "%~1"=="" goto run
if /I "%~1"=="clean"     ( set "ARGS=%ARGS% -Clean" & goto next )
if /I "%~1"=="release"   ( set "ARGS=%ARGS% -Config release" & goto next )
if /I "%~1"=="debug"     ( set "ARGS=%ARGS% -Config debug" & goto next )
if /I "%~1"=="ram"       ( set "ARGS=%ARGS% -BuildType ram" & goto next )
if /I "%~1"=="flash_xip" ( set "ARGS=%ARGS% -BuildType flash_xip" & goto next )
set "ARGS=%ARGS% %~1"
:next
shift
goto parse

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build.ps1" %ARGS%
if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)
echo.
echo BUILD OK
exit /b 0
