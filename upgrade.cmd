@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon upgrade bootstrap
rem Version: 0.21
rem Keep this launcher intentionally small. The authoritative runner is upgrade.ps1.

set "REPO_DIR=%~dp0"
if /I "%~1"=="--temp-run" set "REPO_DIR=%~2"
if "!REPO_DIR:~-1!"=="\" set "REPO_DIR=!REPO_DIR:~0,-1!"

if not exist "!REPO_DIR!\logs" mkdir "!REPO_DIR!\logs" >NUL 2>NUL
set "LOG=!REPO_DIR!\logs\upgrade.log"
>"!LOG!" echo SylphyHornPlusCon upgrade bootstrap 0.21
>>"!LOG!" echo Repository: !REPO_DIR!

where git.exe >NUL 2>NUL
if errorlevel 1 (
    >>"!LOG!" echo ERROR: Git was not found in PATH.
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Git was not found in PATH. Run install.cmd first.
    exit /b 1
)

where powershell.exe >NUL 2>NUL
if errorlevel 1 (
    >>"!LOG!" echo ERROR: Windows PowerShell was not found in PATH.
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Windows PowerShell was not found in PATH.
    exit /b 1
)

set "GIT_DETECT_ERR=%TEMP%\SHPC-git-detect-%RANDOM%-%RANDOM%.log"
git -C "!REPO_DIR!" rev-parse --is-inside-work-tree >NUL 2>"!GIT_DETECT_ERR!"
if errorlevel 1 goto :repo_detect_failed
goto :repo_ready

:repo_detect_failed
findstr /I /C:"detected dubious ownership" "!GIT_DETECT_ERR!" >NUL 2>NUL
if errorlevel 1 goto :repo_detect_fatal

set "SAFE_LINE="
for /f "usebackq delims=" %%L in (`findstr /I /C:"safe.directory" "!GIT_DETECT_ERR!"`) do if not defined SAFE_LINE set "SAFE_LINE=%%L"
if not defined SAFE_LINE goto :safe_parse_failed
set "SAFE_DIR=!SAFE_LINE:*safe.directory =!"
set "SAFE_DIR=!SAFE_DIR:'=!"
set "SAFE_DIR=!SAFE_DIR:"=!"
if not defined SAFE_DIR goto :safe_parse_failed

echo Git marked this repository as dubious ownership. Registering exact safe.directory...
>>"!LOG!" echo Git dubious ownership detected.
>>"!LOG!" echo Registering safe.directory: !SAFE_DIR!
git config --global --add safe.directory "!SAFE_DIR!" >>"!LOG!" 2>&1
if errorlevel 1 goto :safe_register_failed

git -C "!REPO_DIR!" rev-parse --is-inside-work-tree >NUL 2>>"!LOG!"
if errorlevel 1 goto :safe_retry_failed
goto :repo_ready

:repo_detect_fatal
>>"!LOG!" echo ERROR: Repository detection failed before bootstrap.
>>"!LOG!" type "!GIT_DETECT_ERR!"
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
type "!GIT_DETECT_ERR!"
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
exit /b 1

:safe_parse_failed
>>"!LOG!" echo ERROR: Git reported dubious ownership, but its exact safe.directory path could not be parsed.
>>"!LOG!" type "!GIT_DETECT_ERR!"
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Git reported dubious ownership, but its exact safe.directory path could not be parsed.
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
exit /b 1

:safe_register_failed
>>"!LOG!" echo ERROR: Could not register Git safe.directory: !SAFE_DIR!
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Could not register Git safe.directory: !SAFE_DIR!
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
exit /b 1

:safe_retry_failed
>>"!LOG!" echo ERROR: Repository is still rejected by Git after safe.directory registration.
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Repository is still rejected by Git after safe.directory registration.
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
exit /b 1

:repo_ready
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL

set "BRANCH="
for /f "delims=" %%B in ('git -C "!REPO_DIR!" branch --show-current 2^>NUL') do set "BRANCH=%%B"
if not defined BRANCH (
    >>"!LOG!" echo ERROR: Cannot determine current Git branch.
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Cannot determine current Git branch.
    exit /b 1
)
if /I not "!BRANCH!"=="main" if /I not "!BRANCH!"=="devel" (
    >>"!LOG!" echo ERROR: Unsupported branch !BRANCH!.
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Upgrade supports only main or devel branches. Current: !BRANCH!
    exit /b 1
)
>>"!LOG!" echo Branch: !BRANCH!

git -C "!REPO_DIR!" remote set-url origin "https://github.com/Suenee/SylphyHornPlusCon.git" >NUL 2>>"!LOG!"
if errorlevel 1 (
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Cannot set the expected Git origin. See !LOG!
    exit /b 1
)

git -C "!REPO_DIR!" fetch --prune origin "!BRANCH!" >NUL 2>>"!LOG!"
if errorlevel 1 (
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: git fetch failed before bootstrap. See !LOG!
    exit /b 1
)

set "RUNNER_TEMP=%TEMP%\SHPC-upgrade-%RANDOM%-%RANDOM%.ps1"
git -C "!REPO_DIR!" show "origin/!BRANCH!:upgrade.ps1" >"!RUNNER_TEMP!" 2>>"!LOG!"
if errorlevel 1 (
    >>"!LOG!" echo ERROR: Could not extract origin/!BRANCH!:upgrade.ps1.
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Could not extract the authoritative upgrade.ps1 from origin/!BRANCH!.
    del /q "!RUNNER_TEMP!" >NUL 2>NUL
    exit /b 1
)

set "SHPC_UPGRADE_REPO=!REPO_DIR!"
set "SHPC_UPGRADE_BRANCH=!BRANCH!"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!RUNNER_TEMP!"
set "UPGRADE_RC=!ERRORLEVEL!"
del /q "!RUNNER_TEMP!" >NUL 2>NUL
set "SHPC_UPGRADE_REPO="
set "SHPC_UPGRADE_BRANCH="
exit /b !UPGRADE_RC!
