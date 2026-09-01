@echo off
cls
setlocal EnableExtensions

rem SylphyHornPlusCon development launcher
rem Version: 0.01

set "APP=%~dp0source\SylphyHorn\bin\x64\Release\net10.0-windows10.0.26100.0\SylphyHorn.exe"

if not exist "%APP%" (
    echo ERROR: Release build was not found:
    echo %APP%
    echo.
    echo Run upgrade.cmd first.
    exit /b 1
)

tasklist /FI "IMAGENAME eq SylphyHorn.exe" 2>NUL | find /I "SylphyHorn.exe" >NUL
if not errorlevel 1 (
    echo SylphyHorn is running. Restarting...
    taskkill /IM SylphyHorn.exe /T >NUL 2>&1
    timeout /t 2 /nobreak >NUL
    tasklist /FI "IMAGENAME eq SylphyHorn.exe" 2>NUL | find /I "SylphyHorn.exe" >NUL
    if not errorlevel 1 taskkill /F /IM SylphyHorn.exe /T >NUL 2>&1
    timeout /t 1 /nobreak >NUL
) else (
    echo Starting SylphyHorn...
)

start "" "%APP%"
if errorlevel 1 (
    echo ERROR: SylphyHorn could not be started.
    exit /b 1
)

echo SylphyHorn started.
exit /b 0
