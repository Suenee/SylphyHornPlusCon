@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "BRANCH=devel"
set "RAWBASE=https://raw.githubusercontent.com/Suenee/SylphyHornPlusCon/%BRANCH%"

if not exist "logs" mkdir "logs" >nul 2>&1

where curl.exe >nul 2>&1
if errorlevel 1 (
    echo INSTALL FAILED: curl.exe is required to bootstrap the installer.
    pause
    exit /b 1
)

echo [INSTALL] Downloading installer components from %BRANCH%...
curl.exe -fL --retry 3 --connect-timeout 15 -o "%~dp0install.ps1.tmp" "%RAWBASE%/install.ps1"
if errorlevel 1 goto :download_failed

if not exist "%~dp0scripts" mkdir "%~dp0scripts" >nul 2>&1
curl.exe -fL --retry 3 --connect-timeout 15 -o "%~dp0scripts\Environment.ps1.tmp" "%RAWBASE%/scripts/Environment.ps1"
if errorlevel 1 goto :download_failed

move /y "%~dp0install.ps1.tmp" "%~dp0install.ps1" >nul
if errorlevel 1 goto :prepare_failed
move /y "%~dp0scripts\Environment.ps1.tmp" "%~dp0scripts\Environment.ps1" >nul
if errorlevel 1 goto :prepare_failed

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Branch "%BRANCH%" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo INSTALL FAILED. See logs\install.log for details.
    pause
)

exit /b %EXITCODE%

:download_failed
del /q "%~dp0install.ps1.tmp" >nul 2>&1
del /q "%~dp0scripts\Environment.ps1.tmp" >nul 2>&1
echo.
echo INSTALL FAILED: unable to download installer components from GitHub.
pause
exit /b 1

:prepare_failed
echo.
echo INSTALL FAILED: unable to prepare installer components.
pause
exit /b 1
