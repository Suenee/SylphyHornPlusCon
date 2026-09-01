@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon updater
rem Pure CMD implementation. No PowerShell scripts are used.
rem Runs from TEMP so the repository copy of upgrade.cmd can update safely.

if /I "%~1"=="--temp-run" goto :temp_run

set "REPO_DIR=%~dp0"
if "!REPO_DIR:~-1!"=="\" set "REPO_DIR=!REPO_DIR:~0,-1!"
set "UPGRADE_TEMP=%TEMP%\sylphyhornpluscon-upgrade-%RANDOM%-%RANDOM%.cmd"
copy /y "%~f0" "!UPGRADE_TEMP!" >NUL
if errorlevel 1 exit /b 1
call "!UPGRADE_TEMP!" --temp-run "!REPO_DIR!"
set "UPGRADE_RC=!ERRORLEVEL!"
del /q "!UPGRADE_TEMP!" >NUL 2>&1
exit /b !UPGRADE_RC!

:temp_run
set "REPO_DIR=%~2"
set "REPO_URL=https://github.com/Suenee/SylphyHornPlusCon.git"
set "LOG_DIR=%REPO_DIR%\logs"
set "LOG=%LOG_DIR%\upgrade.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >NUL 2>&1
>"%LOG%" echo SylphyHornPlusCon upgrade log
>>"%LOG%" echo Started: %DATE% %TIME%
>>"%LOG%" echo Repository: %REPO_DIR%

pushd "%REPO_DIR%" >NUL 2>&1
if errorlevel 1 goto :repo_error

where git.exe >NUL 2>&1
if errorlevel 1 goto :git_missing
if not exist ".git" goto :not_git

set "BRANCH="
for /f "delims=" %%B in ('git -c safe.directory^=* branch --show-current 2^>NUL') do set "BRANCH=%%B"
if not defined BRANCH goto :branch_error
if /I not "!BRANCH!"=="main" if /I not "!BRANCH!"=="devel" goto :branch_error
>>"%LOG%" echo Branch: !BRANCH!

echo ============================================
echo SylphyHornPlusCon - UPGRADE
echo ============================================
echo Branch: !BRANCH!
echo.

echo [1/7] Checking updater and remote branch...
>>"%LOG%" echo [1/7] Checking updater and remote branch...
git -c safe.directory=* remote set-url origin "%REPO_URL%" >>"%LOG%" 2>&1
if errorlevel 1 goto :git_fail
git -c safe.directory=* fetch --prune origin "!BRANCH!" >>"%LOG%" 2>&1
if errorlevel 1 goto :git_fail

set "REMOTE_HASH="
set "LOCAL_HASH="
for /f "delims=" %%H in ('git -c safe.directory^=* rev-parse "origin/!BRANCH!:upgrade.cmd" 2^>NUL') do set "REMOTE_HASH=%%H"
for /f "delims=" %%H in ('git -c safe.directory^=* hash-object --path=upgrade.cmd "upgrade.cmd" 2^>NUL') do set "LOCAL_HASH=%%H"
if not defined REMOTE_HASH goto :self_update_fail
if not defined LOCAL_HASH goto :self_update_fail
if /I not "!LOCAL_HASH!"=="!REMOTE_HASH!" if not defined SHPC_REMOTE_UPGRADE_RUNNING (
    echo A newer upgrade.cmd is available. Running it first...
    >>"%LOG%" echo Remote upgrade.cmd differs; transferring control to remote version.
    set "REMOTE_UPGRADE=%TEMP%\sylphyhornpluscon-remote-upgrade-%RANDOM%-%RANDOM%.cmd"
    git -c safe.directory=* show "origin/!BRANCH!:upgrade.cmd" > "!REMOTE_UPGRADE!" 2>>"%LOG%"
    if errorlevel 1 goto :self_update_fail
    set "SHPC_REMOTE_UPGRADE_RUNNING=1"
    call "!REMOTE_UPGRADE!" --temp-run "%REPO_DIR%"
    set "REMOTE_RC=!ERRORLEVEL!"
    del /q "!REMOTE_UPGRADE!" >NUL 2>&1
    popd
    exit /b !REMOTE_RC!
)

echo [2/7] Checking tracked local changes...
>>"%LOG%" echo [2/7] Checking tracked local changes...
call :check_tracked_clean
if errorlevel 1 goto :dirty_repo

echo [3/7] Fast-forwarding source tree...
>>"%LOG%" echo [3/7] Fast-forwarding source tree...
git -c safe.directory=* merge --ff-only "origin/!BRANCH!" >>"%LOG%" 2>&1
if errorlevel 1 goto :git_fail

set "HEAD_SHA="
set "ORIGIN_SHA="
for /f "delims=" %%H in ('git -c safe.directory^=* rev-parse HEAD') do set "HEAD_SHA=%%H"
for /f "delims=" %%H in ('git -c safe.directory^=* rev-parse "origin/!BRANCH!"') do set "ORIGIN_SHA=%%H"
if /I not "!HEAD_SHA!"=="!ORIGIN_SHA!" goto :git_state_fail

echo [4/7] Synchronizing Git submodules...
>>"%LOG%" echo [4/7] Synchronizing Git submodules...
git -c safe.directory=* submodule sync --recursive >>"%LOG%" 2>&1
if errorlevel 1 goto :submodule_fail
git -c safe.directory=* submodule update --init --recursive >>"%LOG%" 2>&1
if errorlevel 1 goto :submodule_fail

call :read_sdk_version
if errorlevel 1 goto :sdk_version_fail
call :ensure_dotnet_sdk
if errorlevel 1 goto :dotnet_fail
call :ensure_net48_devpack
if errorlevel 1 goto :net48_fail

echo [5/7] Restoring NuGet packages...
>>"%LOG%" echo [5/7] Restoring NuGet packages...
dotnet restore "source\SylphyHorn.sln" --locked-mode >>"%LOG%" 2>&1
if errorlevel 1 goto :restore_fail

echo [6/7] Building Release x64...
>>"%LOG%" echo [6/7] Building Release x64...
dotnet build "source\SylphyHorn.sln" -c Release -p:Platform=x64 --no-restore >>"%LOG%" 2>&1
if errorlevel 1 goto :build_fail

echo [7/7] Running unit tests...
>>"%LOG%" echo [7/7] Running unit tests...
dotnet test "source\SylphyHorn.Tests\SylphyHorn.Tests.csproj" -c Release -p:Platform=x64 -p:RunSylphyHornPostBuild=false -p:SolutionDir="%REPO_DIR%\source\" --no-restore >>"%LOG%" 2>&1
if errorlevel 1 goto :test_fail

>>"%LOG%" echo STATUS: SUCCESS - phase=COMPLETE
popd
echo.
echo ============================================
echo UPGRADE OK
echo ============================================
echo Commit: !HEAD_SHA!
echo Log:    %LOG%
exit /b 0

:read_sdk_version
set "SDK_VERSION="
for /f "tokens=2 delims=:" %%V in ('findstr /i /c:"version" "global.json"') do if not defined SDK_VERSION set "SDK_VERSION=%%V"
if not defined SDK_VERSION exit /b 1
set "SDK_VERSION=!SDK_VERSION: =!"
set "SDK_VERSION=!SDK_VERSION:"=!"
set "SDK_VERSION=!SDK_VERSION:,=!"
if not defined SDK_VERSION exit /b 1
>>"%LOG%" echo Required .NET SDK: !SDK_VERSION!
exit /b 0

:ensure_dotnet_sdk
where dotnet.exe >NUL 2>&1
if not errorlevel 1 (
  dotnet --list-sdks 2>NUL | findstr /b /c:"!SDK_VERSION! [" >NUL
  if not errorlevel 1 exit /b 0
)
echo .NET SDK !SDK_VERSION! not found. Installing with WinGet...
>>"%LOG%" echo Installing Microsoft.DotNet.SDK.10 version !SDK_VERSION!...
where winget.exe >NUL 2>&1
if errorlevel 1 exit /b 1
winget install --id Microsoft.DotNet.SDK.10 --exact --version !SDK_VERSION! --accept-package-agreements --accept-source-agreements --silent >>"%LOG%" 2>&1
if errorlevel 1 exit /b 1
set "PATH=%ProgramFiles%\dotnet;%PATH%"
dotnet --list-sdks 2>NUL | findstr /b /c:"!SDK_VERSION! [" >NUL
if errorlevel 1 exit /b 1
exit /b 0

:ensure_net48_devpack
if exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll" exit /b 0
echo .NET Framework developer pack not found. Installing with WinGet...
>>"%LOG%" echo Installing Microsoft.DotNet.Framework.DeveloperPack_4...
where winget.exe >NUL 2>&1
if errorlevel 1 exit /b 1
winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 --exact --accept-package-agreements --accept-source-agreements --silent >>"%LOG%" 2>&1
if errorlevel 1 exit /b 1
if not exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll" exit /b 1
exit /b 0

:check_tracked_clean
set "TRACKED_DIRTY=0"
for /f "delims=" %%S in ('git -c safe.directory^=* status --porcelain --untracked-files^=no') do set "TRACKED_DIRTY=1"
if "!TRACKED_DIRTY!"=="1" exit /b 1
exit /b 0

:repo_error
echo ERROR: Unable to enter repository directory.
goto :fail
:git_missing
echo ERROR: Git was not found. Run install.cmd first.
goto :fail_pop
:not_git
echo ERROR: This directory is not a Git working copy. Run install.cmd first.
goto :fail_pop
:branch_error
echo ERROR: Upgrade supports only main or devel branches.
goto :fail_pop
:self_update_fail
echo ERROR: upgrade.cmd self-update check failed.
goto :fail_pop
:dirty_repo
echo ERROR: Tracked local changes exist. Commit or revert them before upgrade.
goto :fail_pop
:git_fail
echo ERROR: Git synchronization failed.
goto :fail_pop
:git_state_fail
echo ERROR: Local HEAD differs from origin/!BRANCH!.
goto :fail_pop
:submodule_fail
echo ERROR: Git submodule synchronization failed.
goto :fail_pop
:sdk_version_fail
echo ERROR: Unable to read required SDK version from global.json.
goto :fail_pop
:dotnet_fail
echo ERROR: Required .NET SDK installation/verification failed.
goto :fail_pop
:net48_fail
echo ERROR: .NET Framework developer pack installation/verification failed.
goto :fail_pop
:restore_fail
echo ERROR: NuGet restore failed.
goto :fail_pop
:build_fail
echo ERROR: Release build failed.
goto :fail_pop
:test_fail
echo ERROR: Unit tests failed.
goto :fail_pop

:fail_pop
popd >NUL 2>&1
:fail
>>"%LOG%" echo STATUS: FAILED
echo.
echo ============================================
echo UPGRADE FAILED
echo ============================================
echo See: %LOG%
pause
exit /b 1
