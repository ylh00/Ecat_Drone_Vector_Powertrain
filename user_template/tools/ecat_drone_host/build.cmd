@echo off
rem ---------------------------------------------------------------------------
rem ecat_drone_host - build.cmd
rem
rem Rebuilds build\ecat_drone_host.exe with the .NET Framework C# compiler.
rem The actual build logic lives in build.ps1 (a PowerShell script, because it
rem has to enumerate the sources and check the compiler); this wrapper invokes
rem it with -ExecutionPolicy Bypass so it also works on machines where script
rem execution is restricted by default (that is the case on the target PC).
rem
rem Usage:
rem   build.cmd              build only
rem   build.cmd -Selftest    build, then run --selftest
rem   build.cmd -Clean       remove build\ first, then build
rem ---------------------------------------------------------------------------
setlocal

rem %~dp0 ends with a backslash.  Capture it BEFORE any argument handling so the
rem script keeps working if more parameters are added later.
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR%"=="" set "SCRIPT_DIR=.\"

set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" set "PS=powershell.exe"

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build.ps1" %*
if errorlevel 1 (
    echo.
    echo [BUILD FAILED] see the errors above.
    exit /b 1
)

echo.
echo Done. Start the tool with run.cmd  (or: build\ecat_drone_host.exe --simulate^)
exit /b 0
