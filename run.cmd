@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

rem SylphyHornPlusCon development launcher
rem Version: 0.05

set "APP=%~dp0source\SylphyHorn\bin\x64\Release\net10.0-windows10.0.26100.0\SylphyHorn.exe"
set "APPDIR=%~dp0source\SylphyHorn\bin\x64\Release\net10.0-windows10.0.26100.0"
set "PIDFILE=%TEMP%\shpc-run-%RANDOM%-%RANDOM%.pid"

if not exist "%APP%" (
    echo ERROR: Release build was not found:
    echo %APP%
    echo.
    echo Run upgrade.cmd first.
    exit /b 1
)

rem Identify an existing instance by its exact executable path. PowerShell writes
rem the result to a temporary file instead of returning it through FOR /F command
rem substitution. This avoids CMD parsing/output-capture failures seen when the
rem repository is launched from mapped network drives.
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

rem Start-Process -PassThru gives us the PID of the process we actually launched.
rem Validate that exact PID and executable path after the startup delay instead
rem of scanning command output and accidentally reporting a false launch failure.
del /q "%PIDFILE%" >NUL 2>&1
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $p=Start-Process -FilePath $env:APP -WorkingDirectory $env:APPDIR -PassThru; [IO.File]::WriteAllText($env:PIDFILE,[string]$p.Id,[Text.Encoding]::ASCII)" >NUL 2>&1
if errorlevel 1 (
    echo ERROR: SylphyHorn could not be started.
    echo Check the latest startup trace under:
    echo %%LocalAppData%%\hwtnb.net\SylphyHornPlus\StartupTrace
    del /q "%PIDFILE%" >NUL 2>&1
    exit /b 1
)

set "APP_PID="
if exist "%PIDFILE%" set /p APP_PID=<"%PIDFILE%"
del /q "%PIDFILE%" >NUL 2>&1
if not defined APP_PID (
    echo ERROR: SylphyHorn launch did not return a process ID.
    exit /b 1
)

timeout /t 2 /nobreak >NUL
call :verify_app_pid !APP_PID!
if errorlevel 1 (
    echo ERROR: SylphyHorn process for this build was not found after launch.
    echo Check the latest startup trace under:
    echo %%LocalAppData%%\hwtnb.net\SylphyHornPlus\StartupTrace
    exit /b 1
)

echo SylphyHorn started. PID !APP_PID!
exit /b 0

:find_app_pid
set "APP_PID="
del /q "%PIDFILE%" >NUL 2>&1
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$target=[IO.Path]::GetFullPath($env:APP); $p=Get-Process -Name 'SylphyHorn' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq $target) } catch { $false } } | Select-Object -First 1; if($p){[IO.File]::WriteAllText($env:PIDFILE,[string]$p.Id,[Text.Encoding]::ASCII)}" >NUL 2>&1
if exist "%PIDFILE%" set /p APP_PID=<"%PIDFILE%"
del /q "%PIDFILE%" >NUL 2>&1
exit /b 0

:verify_app_pid
set "VERIFY_PID=%~1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$target=[IO.Path]::GetFullPath($env:APP); $p=Get-Process -Id $env:VERIFY_PID -ErrorAction SilentlyContinue; if(-not $p){exit 1}; try { if(-not $p.Path -or ([IO.Path]::GetFullPath($p.Path) -ine $target)){exit 2} } catch { exit 3 }; exit 0" >NUL 2>&1
exit /b %ERRORLEVEL%
