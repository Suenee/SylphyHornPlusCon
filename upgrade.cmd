@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "BRANCH="
for /f "delims=" %%B in ('git -c safe.directory^=* branch --show-current 2^>nul') do set "BRANCH=%%B"
if not defined BRANCH set "BRANCH=devel"
if /I not "%BRANCH%"=="main" if /I not "%BRANCH%"=="devel" set "BRANCH=devel"

set "REMOTE=https://raw.githubusercontent.com/Suenee/SylphyHornPlusCon/%BRANCH%/upgrade.ps1"
set "RUNNER=%TEMP%\sylphyhornpluscon-upgrade-%RANDOM%-%RANDOM%.ps1"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing '%REMOTE%' -OutFile '%RUNNER%'"
if errorlevel 1 (
    echo ERROR: Unable to download the current upgrade runner from GitHub.
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNNER%" -Root "%~dp0" -Branch "%BRANCH%"
set "EXITCODE=%ERRORLEVEL%"
del /q "%RUNNER%" >nul 2>&1

if not "%EXITCODE%"=="0" (
    echo.
    echo UPGRADE FAILED. See upgrade.log for details.
    pause
)

exit /b %EXITCODE%
