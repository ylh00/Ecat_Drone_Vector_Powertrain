@echo off
rem ---------------------------------------------------------------------------
rem ecat_drone_host - run.cmd
rem   run.cmd                  start the GUI
rem   run.cmd --simulate       start against the built-in device simulator
rem   run.cmd --help           print the command line help (waits for the tool)
rem ---------------------------------------------------------------------------
setlocal
set "SCRIPT_DIR=%~dp0"

set "EXE=%SCRIPT_DIR%build\ecat_drone_host.exe"
if not exist "%EXE%" (
    echo [ERROR] "%EXE%" not found.
    echo         Build it first:  build.cmd
    exit /b 1
)

rem console-style modes must be waited for so their output is not lost
if /i "%~1"=="--help" goto waitmode
if /i "%~1"=="-h" goto waitmode
if /i "%~1"=="--selftest" goto waitmode
if /i "%~1"=="--uitest" goto waitmode

start "" "%EXE%" %*
exit /b 0

:waitmode
start "" /wait "%EXE%" %*
exit /b %ERRORLEVEL%
