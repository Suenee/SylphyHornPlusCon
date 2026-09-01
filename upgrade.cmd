@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon upgrade bootstrap
rem Version: 0.20
rem Keep this launcher intentionally small. The authoritative runner is upgrade.ps1.

set "REPO_DIR=%~dp0"
if /I "%~1"=="--temp-run" set "REPO_DIR=%~2"
if "!REPO_DIR:~-1!"=="\" set "REPO_DIR=!REPO_DIR:~0,-1!"

where git.exe >NUL 2>NUL
if errorlevel 1 (
    echo ERROR: Git was not found in PATH. Run install.cmd first.
    exit /b 1
)

if not exist "!REPO_DIR!\logs" mkdir "!REPO_DIR!\logs" >NUL 2>NUL
set "LOG=!REPO_DIR!\logs\upgrade.log"

set "GIT_DETECT_ERR=%TEMP%\SHPC-git-detect-%RANDOM%-%RANDOM%.log"
git -C "!REPO_DIR!" rev-parse --is-inside-work-tree >NUL 2>"!GIT_DETECT_ERR!"
if errorlevel 1 (
    findstr /I /C:"detected dubious ownership" "!GIT_DETECT_ERR!" >NUL 2>NUL
    if errorlevel 1 (
        >"!LOG!" echo ERROR: Repository detection failed before bootstrap.
        >>"!LOG!" type "!GIT_DETECT_ERR!"
        >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
        type "!GIT_DETECT_ERR!"
        del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
        exit /b 1
    )
    set "SHPC_GIT_DETECT_ERR=!GIT_DETECT_ERR!"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$text=[IO.File]::ReadAllText($env:SHPC_GIT_DETECT_ERR); $m=[regex]::Match($text, \"safe\.directory\s+'([^']+)'\"); if(-not $m.Success){ Write-Host 'ERROR: Git reported dubious ownership, but its exact safe.directory path could not be parsed.' -ForegroundColor Red; exit 3 }; $safe=$m.Groups[1].Value; & git.exe config --global --add safe.directory $safe; $rc=$LASTEXITCODE; if($rc -ne 0){ Write-Host ('ERROR: Could not register Git safe.directory: ' + $safe) -ForegroundColor Red; exit $rc }; Write-Host ('Git safe.directory registered: ' + $safe) -ForegroundColor Yellow"
    set "SAFE_RC=!ERRORLEVEL!"
    set "SHPC_GIT_DETECT_ERR="
    if not "!SAFE_RC!"=="0" (
        del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
        exit /b !SAFE_RC!
    )
    git -C "!REPO_DIR!" rev-parse --is-inside-work-tree >NUL 2>NUL
    if errorlevel 1 (
        echo ERROR: Repository is still rejected by Git after safe.directory registration.
        del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
        exit /b 1
    )
)
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL

set "BRANCH="
for /f "delims=" %%B in ('git -C "!REPO_DIR!" branch --show-current 2^>NUL') do set "BRANCH=%%B"
if not defined BRANCH (
    echo ERROR: Cannot determine current Git branch.
    exit /b 1
)
if /I not "!BRANCH!"=="main" if /I not "!BRANCH!"=="devel" (
    echo ERROR: Upgrade supports only main or devel branches. Current: !BRANCH!
    exit /b 1
)

git -C "!REPO_DIR!" remote set-url origin "https://github.com/Suenee/SylphyHornPlusCon.git" >NUL 2>"!LOG!"
if errorlevel 1 (
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: Cannot set the expected Git origin. See !LOG!
    exit /b 1
)

git -C "!REPO_DIR!" fetch --prune origin "!BRANCH!" >NUL 2>"!LOG!"
if errorlevel 1 (
    >>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
    echo ERROR: git fetch failed before bootstrap. See !LOG!
    exit /b 1
)

set "RUNNER_TEMP=%TEMP%\SHPC-upgrade-%RANDOM%-%RANDOM%.ps1"
git -C "!REPO_DIR!" show "origin/!BRANCH!:upgrade.ps1" >"!RUNNER_TEMP!" 2>>"!LOG!"
if errorlevel 1 (
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
