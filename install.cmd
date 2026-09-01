@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon fresh-PC installer
rem Pure CMD implementation. No PowerShell bootstrap scripts are used.
rem Safe for mapped/network drives because the installer runs from TEMP.

if /I "%~1"=="--temp-run" goto :temp_run

set "TARGET=%~dp0"
if "!TARGET:~-1!"=="\" set "TARGET=!TARGET:~0,-1!"
set "INSTALL_TEMP=%TEMP%\sylphyhornpluscon-install-%RANDOM%-%RANDOM%.cmd"
copy /y "%~f0" "!INSTALL_TEMP!" >NUL
if errorlevel 1 exit /b 1
call "!INSTALL_TEMP!" --temp-run "!TARGET!"
set "INSTALL_RC=!ERRORLEVEL!"
del /q "!INSTALL_TEMP!" >NUL 2>&1
exit /b !INSTALL_RC!

:temp_run
set "TARGET=%~2"
set "REPO_URL=https://github.com/Suenee/SylphyHornPlusCon.git"
set "BRANCH=devel"
set "TARGET_FRAMEWORK=net10.0-windows10.0.26100.0"
set "LOG_DIR=%TARGET%\logs"
set "LOG=%LOG_DIR%\install.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >NUL 2>&1
>"%LOG%" echo SylphyHornPlusCon install log
>>"%LOG%" echo Started: %DATE% %TIME%
>>"%LOG%" echo Target: %TARGET%
>>"%LOG%" echo Branch: %BRANCH%
>>"%LOG%" echo Validation target: %TARGET_FRAMEWORK%

echo ============================================
echo SylphyHornPlusCon - FRESH INSTALL
echo ============================================
echo Target: %TARGET%
echo Branch: %BRANCH%
echo.

call :ensure_git
if errorlevel 1 goto :fail

if not exist "%TARGET%" mkdir "%TARGET%"
if errorlevel 1 goto :target_error
pushd "%TARGET%"
if errorlevel 1 goto :target_error

rem Remove files from the abandoned PowerShell-based bootstrap if present.
del /q "install.ps1" "upgrade.ps1" >NUL 2>&1
if exist "scripts\Environment.ps1" del /q "scripts\Environment.ps1" >NUL 2>&1

if exist ".git" goto :existing_repo
call :bootstrap_folder_check
if errorlevel 1 goto :unsafe_folder

echo [1/6] Creating DEVEL working copy...
>>"%LOG%" echo [1/6] Creating DEVEL working copy...
git -c safe.directory=* init >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
git -c safe.directory=* remote add origin "%REPO_URL%" >>"%LOG%" 2>&1
git -c safe.directory=* remote set-url origin "%REPO_URL%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
git -c safe.directory=* fetch origin "%BRANCH%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
git -c safe.directory=* checkout -f -B "%BRANCH%" "origin/%BRANCH%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
goto :repo_ready

:existing_repo
echo [1/6] Checking existing DEVEL working copy...
>>"%LOG%" echo [1/6] Checking existing DEVEL working copy...
git -c safe.directory=* rev-parse --is-inside-work-tree >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
git -c safe.directory=* remote set-url origin "%REPO_URL%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
call :check_tracked_clean
if errorlevel 1 goto :dirty_repo
git -c safe.directory=* fetch origin "%BRANCH%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
git -c safe.directory=* checkout "%BRANCH%" >>"%LOG%" 2>&1
if errorlevel 1 git -c safe.directory=* checkout -b "%BRANCH%" --track "origin/%BRANCH%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail
git -c safe.directory=* merge --ff-only "origin/%BRANCH%" >>"%LOG%" 2>&1
if errorlevel 1 goto :repo_fail

:repo_ready
echo [2/6] Initializing Git submodules...
>>"%LOG%" echo [2/6] Initializing Git submodules...
git -c safe.directory=* submodule sync --recursive >>"%LOG%" 2>&1
if errorlevel 1 goto :submodule_fail
git -c safe.directory=* submodule update --init --recursive >>"%LOG%" 2>&1
if errorlevel 1 goto :submodule_fail

echo [3/6] Checking required .NET SDK...
>>"%LOG%" echo [3/6] Checking required .NET SDK...
call :read_sdk_version
if errorlevel 1 goto :sdk_version_fail
call :ensure_dotnet_sdk
if errorlevel 1 goto :dotnet_fail

echo [4/6] Restoring .NET 10 projects...
>>"%LOG%" echo [4/6] Restoring target %TARGET_FRAMEWORK%...
dotnet restore "source\SylphyHorn\SylphyHorn.csproj" -p:TargetFramework=%TARGET_FRAMEWORK% --locked-mode >>"%LOG%" 2>&1
if errorlevel 1 goto :restore_fail
dotnet restore "source\SylphyHorn.Tests\SylphyHorn.Tests.csproj" -p:TargetFramework=%TARGET_FRAMEWORK% --locked-mode >>"%LOG%" 2>&1
if errorlevel 1 goto :restore_fail

echo [5/6] Building Release x64 for .NET 10...
>>"%LOG%" echo [5/6] Building Release x64 for %TARGET_FRAMEWORK%...
dotnet build "source\SylphyHorn\SylphyHorn.csproj" -c Release -f %TARGET_FRAMEWORK% -p:Platform=x64 -p:RunSylphyHornPostBuild=false --no-restore >>"%LOG%" 2>&1
if errorlevel 1 goto :build_fail

echo [6/6] Running .NET 10 unit tests and verifying Git state...
>>"%LOG%" echo [6/6] Running unit tests for %TARGET_FRAMEWORK%...
dotnet test "source\SylphyHorn.Tests\SylphyHorn.Tests.csproj" -c Release -f %TARGET_FRAMEWORK% -p:Platform=x64 -p:RunSylphyHornPostBuild=false -p:SolutionDir="%TARGET%\source\" --no-restore >>"%LOG%" 2>&1
if errorlevel 1 goto :test_fail

set "HEAD_SHA="
set "ORIGIN_SHA="
for /f "delims=" %%H in ('git -c safe.directory^=* rev-parse HEAD') do set "HEAD_SHA=%%H"
for /f "delims=" %%H in ('git -c safe.directory^=* rev-parse origin/%BRANCH%') do set "ORIGIN_SHA=%%H"
if /I not "!HEAD_SHA!"=="!ORIGIN_SHA!" goto :git_state_fail

>>"%LOG%" echo STATUS: SUCCESS - phase=COMPLETE
popd
echo.
echo ============================================
echo INSTALL OK
echo ============================================
echo .NET SDK: !SDK_VERSION!
echo Target:   %TARGET_FRAMEWORK%
echo Commit:   !HEAD_SHA!
echo Log:      %LOG%
exit /b 0

:ensure_git
where git.exe >NUL 2>&1
if not errorlevel 1 exit /b 0
echo Git for Windows not found. Installing with WinGet...
>>"%LOG%" echo Git for Windows not found. Installing with WinGet...
where winget.exe >NUL 2>&1
if errorlevel 1 exit /b 1
winget install --id Git.Git --exact --accept-package-agreements --accept-source-agreements --silent >>"%LOG%" 2>&1
if errorlevel 1 exit /b 1
set "PATH=%ProgramFiles%\Git\cmd;%LOCALAPPDATA%\Programs\Git\cmd;%PATH%"
where git.exe >NUL 2>&1
if errorlevel 1 exit /b 1
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
where dotnet.exe >NUL 2>&1
if errorlevel 1 exit /b 1
dotnet --list-sdks 2>NUL | findstr /b /c:"!SDK_VERSION! [" >NUL
if errorlevel 1 exit /b 1
exit /b 0

:bootstrap_folder_check
set "UNSAFE=0"
for /f "delims=" %%F in ('dir /b /a 2^>NUL') do call :check_bootstrap_item "%%F"
if "!UNSAFE!"=="1" exit /b 1
exit /b 0

:check_bootstrap_item
if /I "%~1"=="install.cmd" exit /b 0
if /I "%~1"=="logs" exit /b 0
if /I "%~1"=="scripts" exit /b 0
set "UNSAFE=1"
exit /b 0

:check_tracked_clean
set "TRACKED_DIRTY=0"
for /f "delims=" %%S in ('git -c safe.directory^=* status --porcelain --untracked-files^=no') do set "TRACKED_DIRTY=1"
if "!TRACKED_DIRTY!"=="1" exit /b 1
exit /b 0

:target_error
echo ERROR: Cannot create or enter %TARGET%.
goto :fail
:unsafe_folder
echo ERROR: Target folder contains unrelated files and is not a Git repository.
goto :fail_pop
:dirty_repo
echo ERROR: Tracked local changes exist. Commit or revert them first.
goto :fail_pop
:repo_fail
echo ERROR: Git repository setup/update failed.
goto :fail_pop
:submodule_fail
echo ERROR: Git submodule initialization failed.
goto :fail_pop
:sdk_version_fail
echo ERROR: Unable to read the required SDK version from global.json.
goto :fail_pop
:dotnet_fail
echo ERROR: Required .NET SDK installation/verification failed.
goto :fail_pop
:restore_fail
echo ERROR: .NET 10 NuGet restore failed.
goto :fail_pop
:build_fail
echo ERROR: .NET 10 Release build failed.
goto :fail_pop
:test_fail
echo ERROR: .NET 10 unit tests failed.
goto :fail_pop
:git_state_fail
echo ERROR: Local HEAD differs from origin/%BRANCH%.
goto :fail_pop

:fail_pop
popd >NUL 2>&1
:fail
>>"%LOG%" echo STATUS: FAILED
echo.
echo ============================================
echo INSTALL FAILED
echo ============================================
echo See: %LOG%
pause
exit /b 1
