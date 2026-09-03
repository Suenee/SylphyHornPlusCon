@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon upgrade bootstrap
rem Version: 0.23
rem Stage 0 always transfers control to the current remote upgrade.cmd in TEMP.
rem Stage 1 then downloads and runs the authoritative upgrade.ps1 in TEMP.

set "REPO_DIR=%~dp0"
if /I "%~1"=="--temp-run" set "REPO_DIR=%~2"
if /I "%~1"=="--current-bootstrap" goto :current_bootstrap
if "!REPO_DIR:~-1!"=="\" set "REPO_DIR=!REPO_DIR:~0,-1!"

goto :stage0

:stage0
where git.exe >NUL 2>NUL
if errorlevel 1 goto :git_missing_stage0
where powershell.exe >NUL 2>NUL
if errorlevel 1 goto :powershell_missing_stage0

call :ensure_git_repository
if errorlevel 1 exit /b 1
call :read_branch
if errorlevel 1 exit /b 1

git -C "!REPO_DIR!" remote set-url origin "https://github.com/Suenee/SylphyHornPlusCon.git" >NUL 2>NUL
if errorlevel 1 goto :origin_fail_stage0
git -C "!REPO_DIR!" fetch --prune origin "!BRANCH!" >NUL 2>NUL
if errorlevel 1 goto :fetch_fail_stage0

set "REMOTE_CMD_RAW=%TEMP%\SHPC-upgrade-remote-%RANDOM%-%RANDOM%.raw"
set "REMOTE_CMD=%TEMP%\SHPC-upgrade-remote-%RANDOM%-%RANDOM%.cmd"
git -C "!REPO_DIR!" show "origin/!BRANCH!:upgrade.cmd" >"!REMOTE_CMD_RAW!" 2>NUL
if errorlevel 1 goto :remote_cmd_fail

set "SHPC_BOOTSTRAP_SOURCE=!REMOTE_CMD_RAW!"
set "SHPC_BOOTSTRAP_TARGET=!REMOTE_CMD!"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$text=[IO.File]::ReadAllText($env:SHPC_BOOTSTRAP_SOURCE); $text=[Text.RegularExpressions.Regex]::Replace($text,'\r?\n',\"`r`n\"); [IO.File]::WriteAllText($env:SHPC_BOOTSTRAP_TARGET,$text,(New-Object Text.UTF8Encoding($false)))"
if errorlevel 1 goto :remote_cmd_normalize_fail

del /q "!REMOTE_CMD_RAW!" >NUL 2>NUL
set "SHPC_BOOTSTRAP_SOURCE="
set "SHPC_BOOTSTRAP_TARGET="

call "!REMOTE_CMD!" --current-bootstrap "!REPO_DIR!" "!BRANCH!"
set "BOOTSTRAP_RC=!ERRORLEVEL!"
del /q "!REMOTE_CMD!" >NUL 2>NUL
exit /b !BOOTSTRAP_RC!

:current_bootstrap
set "REPO_DIR=%~2"
set "BRANCH=%~3"
if "!REPO_DIR:~-1!"=="\" set "REPO_DIR=!REPO_DIR:~0,-1!"
if not defined BRANCH call :read_branch
if errorlevel 1 exit /b 1

if not exist "!REPO_DIR!\logs" mkdir "!REPO_DIR!\logs" >NUL 2>NUL
set "LOG=!REPO_DIR!\logs\upgrade.log"
>"!LOG!" echo SylphyHornPlusCon upgrade bootstrap 0.23
>>"!LOG!" echo Repository: !REPO_DIR!
>>"!LOG!" echo Branch: !BRANCH!
>>"!LOG!" echo Launcher: current origin/!BRANCH!:upgrade.cmd running from TEMP

where git.exe >NUL 2>NUL
if errorlevel 1 goto :git_missing_stage1
where powershell.exe >NUL 2>NUL
if errorlevel 1 goto :powershell_missing_stage1

call :ensure_git_repository
if errorlevel 1 exit /b 1

git -C "!REPO_DIR!" remote set-url origin "https://github.com/Suenee/SylphyHornPlusCon.git" >NUL 2>>"!LOG!"
if errorlevel 1 goto :origin_fail_stage1
git -C "!REPO_DIR!" fetch --prune origin "!BRANCH!" >NUL 2>>"!LOG!"
if errorlevel 1 goto :fetch_fail_stage1

rem Ask SHPC 0.34+ to perform its own graceful shutdown before the runner inspects processes.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { $e=[Threading.EventWaitHandle]::OpenExisting('Local\SylphyHornPlusCon.UpgradeShutdown'); [void]$e.Set(); $e.Dispose() } catch { }" >NUL 2>NUL

set "RUNNER_TEMP=%TEMP%\SHPC-upgrade-%RANDOM%-%RANDOM%.ps1"
git -C "!REPO_DIR!" show "origin/!BRANCH!:upgrade.ps1" >"!RUNNER_TEMP!" 2>>"!LOG!"
if errorlevel 1 goto :runner_extract_fail

set "SHPC_UPGRADE_REPO=!REPO_DIR!"
set "SHPC_UPGRADE_BRANCH=!BRANCH!"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!RUNNER_TEMP!"
set "UPGRADE_RC=!ERRORLEVEL!"
del /q "!RUNNER_TEMP!" >NUL 2>NUL
set "SHPC_UPGRADE_REPO="
set "SHPC_UPGRADE_BRANCH="
exit /b !UPGRADE_RC!

:ensure_git_repository
set "GIT_DETECT_ERR=%TEMP%\SHPC-git-detect-%RANDOM%-%RANDOM%.log"
git -C "!REPO_DIR!" rev-parse --is-inside-work-tree >NUL 2>"!GIT_DETECT_ERR!"
if not errorlevel 1 goto :git_repo_ok
findstr /I /C:"detected dubious ownership" "!GIT_DETECT_ERR!" >NUL 2>NUL
if errorlevel 1 goto :git_repo_fatal
set "SAFE_LINE="
for /f "usebackq delims=" %%L in (`findstr /I /C:"safe.directory" "!GIT_DETECT_ERR!"`) do if not defined SAFE_LINE set "SAFE_LINE=%%L"
if not defined SAFE_LINE goto :git_safe_parse_fail
set "SAFE_DIR=!SAFE_LINE:*safe.directory =!"
set "SAFE_DIR=!SAFE_DIR:'=!"
set "SAFE_DIR=!SAFE_DIR:"=!"
if not defined SAFE_DIR goto :git_safe_parse_fail
echo Git marked this repository as dubious ownership. Registering exact safe.directory...
git config --global --add safe.directory "!SAFE_DIR!" >NUL 2>NUL
if errorlevel 1 goto :git_safe_register_fail
git -C "!REPO_DIR!" rev-parse --is-inside-work-tree >NUL 2>NUL
if errorlevel 1 goto :git_safe_retry_fail
:git_repo_ok
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
exit /b 0

:git_repo_fatal
type "!GIT_DETECT_ERR!"
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
echo ERROR: Repository detection failed.
exit /b 1

:git_safe_parse_fail
type "!GIT_DETECT_ERR!"
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
echo ERROR: Git reported dubious ownership, but its exact safe.directory path could not be parsed.
exit /b 1

:git_safe_register_fail
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
echo ERROR: Could not register Git safe.directory: !SAFE_DIR!
exit /b 1

:git_safe_retry_fail
del /q "!GIT_DETECT_ERR!" >NUL 2>NUL
echo ERROR: Repository is still rejected after exact safe.directory registration.
exit /b 1

:read_branch
set "BRANCH="
for /f "delims=" %%B in ('git -C "!REPO_DIR!" branch --show-current 2^>NUL') do set "BRANCH=%%B"
if not defined BRANCH goto :branch_fail
if /I "!BRANCH!"=="main" exit /b 0
if /I "!BRANCH!"=="devel" exit /b 0
goto :branch_fail

:branch_fail
echo ERROR: Upgrade supports only main or devel branches.
exit /b 1

:git_missing_stage0
echo ERROR: Git was not found in PATH. Run install.cmd first.
exit /b 1
:powershell_missing_stage0
echo ERROR: Windows PowerShell was not found in PATH.
exit /b 1
:origin_fail_stage0
echo ERROR: Cannot set the expected Git origin before launcher self-update.
exit /b 1
:fetch_fail_stage0
echo ERROR: Cannot fetch origin/!BRANCH! before launcher self-update.
exit /b 1
:remote_cmd_fail
echo ERROR: Cannot extract current origin/!BRANCH!:upgrade.cmd.
del /q "!REMOTE_CMD_RAW!" >NUL 2>NUL
exit /b 1
:remote_cmd_normalize_fail
echo ERROR: Cannot materialize current upgrade.cmd with CRLF line endings.
del /q "!REMOTE_CMD_RAW!" >NUL 2>NUL
del /q "!REMOTE_CMD!" >NUL 2>NUL
exit /b 1

:git_missing_stage1
>>"!LOG!" echo ERROR: Git was not found in PATH.
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Git was not found in PATH.
exit /b 1
:powershell_missing_stage1
>>"!LOG!" echo ERROR: Windows PowerShell was not found in PATH.
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Windows PowerShell was not found in PATH.
exit /b 1
:origin_fail_stage1
>>"!LOG!" echo ERROR: Cannot set expected Git origin.
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Cannot set expected Git origin. See !LOG!
exit /b 1
:fetch_fail_stage1
>>"!LOG!" echo ERROR: git fetch origin/!BRANCH! failed.
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: git fetch failed. See !LOG!
exit /b 1
:runner_extract_fail
>>"!LOG!" echo ERROR: Could not extract origin/!BRANCH!:upgrade.ps1.
>>"!LOG!" echo STATUS: FAILED - phase=SELF-UPDATE/BOOTSTRAP
echo ERROR: Could not extract authoritative upgrade.ps1. See !LOG!
del /q "!RUNNER_TEMP!" >NUL 2>NUL
exit /b 1
