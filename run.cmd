@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon development launcher
rem Version: 0.04

set "APP=%~dp0source\SylphyHorn\bin\x64\Release\net10.0-windows10.0.26100.0\SylphyHorn.exe"
set "APPDIR=%~dp0source\SylphyHorn\bin\x64\Release\net10.0-windows10.0.26100.0"

if not exist "%APP%" (
    echo ERROR: Release build was not found:
    echo %APP%
    echo.
    echo Run upgrade.cmd first.
    exit /b 1
)

rem Do not depend on the Windows process image name. The historical project
rem metadata/product name can differ from the executable path/name. Identify
rem the running instance by its executable path instead.
call :find_app_pid
if defined APP_PID (
    echo SylphyHorn is running. Restarting...
    taskkill /PID !APP_PID! /T >NUL 2>&1
    timeout /t 2 /nobreak >NUL
    call :find_app_pid
    if defined APP_PID taskkill /F /PID !APP_PID! /T >NUL 2>&1
    timeout /t 1 /nobreak >NUL
) else (
    echo Starting SylphyHorn...
)

start "" /D "%APPDIR%" "%APP%"
timeout /t 2 /nobreak >NUL

call :find_app_pid
if not defined APP_PID (
    echo ERROR: SylphyHorn process for this build was not found after launch.
    echo Check the latest startup trace under:
    echo %%LocalAppData%%\hwtnb.net\SylphyHornPlus\StartupTrace
    exit /b 1
)

echo SylphyHorn started. PID !APP_PID!
exit /b 0

:find_app_pid
set "APP_PID="
for /f "usebackq tokens=*" %%P in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$target=[IO.Path]::GetFullPath($env:APP); Get-Process -ErrorAction SilentlyContinue ^| ForEach-Object { try { if ([IO.Path]::GetFullPath($_.Path) -ieq $target) { $_.Id } } catch {} } ^| Select-Object -First 1"`) do set "APP_PID=%%P"
exit /b 0
