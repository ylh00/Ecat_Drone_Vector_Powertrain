@echo off
rem ---------------------------------------------------------------------------
rem ecat_drone_host - selftest.cmd
rem Runs the built-in protocol/UI self test and prints the report.
rem Exit code 0 = every check passed.
rem ---------------------------------------------------------------------------
setlocal
set "SCRIPT_DIR=%~dp0"

set "EXE=%SCRIPT_DIR%build\ecat_drone_host.exe"
if not exist "%EXE%" (
    echo [ERROR] "%EXE%" not found.
    echo         Build it first:  build.cmd
    exit /b 1
)

set "OUT=%SCRIPT_DIR%build\selftest_console.txt"
set "ERR=%SCRIPT_DIR%build\selftest_console.err.txt"

set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" set "PS=powershell.exe"

"%PS%" -NoProfile -ExecutionPolicy Bypass -Command ^
  "$p = Start-Process -FilePath '%EXE%' -ArgumentList '--selftest' -NoNewWindow -Wait -PassThru -RedirectStandardOutput '%OUT%' -RedirectStandardError '%ERR%';" ^
  "if (Test-Path -LiteralPath '%OUT%') { Get-Content -LiteralPath '%OUT%' -Encoding UTF8 };" ^
  "if ((Test-Path -LiteralPath '%ERR%') -and ((Get-Item -LiteralPath '%ERR%').Length -gt 0)) { Write-Host '--- stderr ---'; Get-Content -LiteralPath '%ERR%' -Encoding UTF8 };" ^
  "exit $p.ExitCode"

if errorlevel 1 (
    echo.
    echo [SELFTEST FAIL] see the report above / build\selftest.log
    exit /b 1
)

echo.
echo [SELFTEST PASS]
exit /b 0
