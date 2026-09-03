$ErrorActionPreference = 'Stop'

$Version = '0.27'
$Revision = '0.27-lockfile-dirty-gate'
$Repo = $env:SHPC_UPGRADE_REPO
$TargetBranch = $env:SHPC_UPGRADE_BRANCH
$ExpectedRemote = 'https://github.com/Suenee/SylphyHornPlusCon.git'
$TargetFramework = 'net10.0-windows10.0.26100.0'

if ([string]::IsNullOrWhiteSpace($Repo)) { $Repo = (Get-Location).ProviderPath }
$Repo = [IO.Path]::GetFullPath($Repo).TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($TargetBranch)) { $TargetBranch = 'devel' }
if ($TargetBranch -notin @('main', 'devel')) { throw "Unsupported target branch: $TargetBranch" }

$LogsDir = Join-Path $Repo 'logs'
New-Item -ItemType Directory -Path $LogsDir -Force | Out-Null
$Log = Join-Path $LogsDir 'upgrade.log'
$Utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $Utf8
$OutputEncoding = $Utf8
try { & chcp.com 65001 *> $null } catch { }
[IO.File]::WriteAllText($Log, '', $Utf8)

$HadWarning = $false
$FailPhase = 'BOOTSTRAP'
$AppWasRunning = $false
$RuntimeRestored = $false
$AppExe = Join-Path $Repo 'source\SylphyHorn\bin\x64\Release\net10.0-windows10.0.26100.0\SylphyHorn.exe'

function Write-Line([string]$Text, [ConsoleColor]$Color = [ConsoleColor]::Gray) {
    [IO.File]::AppendAllText($Log, $Text + [Environment]::NewLine, $Utf8)
    Write-Host $Text -ForegroundColor $Color
}
function Info([string]$Text) { Write-Line $Text Gray }
function Warn([string]$Text) { $script:HadWarning = $true; Write-Line ('WARNING: ' + $Text) Yellow }
function Fail([string]$Phase, [string]$Text) {
    $script:FailPhase = $Phase
    Write-Line ('ERROR: ' + $Text) Red
    throw [InvalidOperationException]::new($Text)
}
function Run-Native {
    param(
        [Parameter(Mandatory=$true)][string]$Phase,
        [Parameter(Mandatory=$true)][string]$Exe,
        [Parameter(Mandatory=$true)][string[]]$ArgumentList,
        [switch]$AllowFailure,
        [switch]$SuppressOutput
    )
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Exe @ArgumentList 2>&1 | ForEach-Object {
            if ($SuppressOutput) { return }
            $line = [string]$_
            if ($line -match '(?i)\b(error|failed|fatal)\b|MSB\d+.*\berror\b') { Write-Line $line Red }
            elseif ($line -match '(?i)\bwarning\b') { Write-Line $line Yellow }
            else { Write-Line $line Gray }
        }
        $rc = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
    if ($rc -ne 0 -and -not $AllowFailure) { Fail $Phase ("$Exe failed with exit code $rc") }
    return $rc
}
function Get-GitText([string[]]$Arguments, [string]$Phase = 'GIT') {
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & git.exe @Arguments 2>&1
        $rc = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
    if ($rc -ne 0) {
        foreach ($line in $output) { Write-Line ([string]$line) Red }
        Fail $Phase ("git.exe failed with exit code $rc")
    }
    return (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
}
function Test-ProtectedTrackedDirty {
    $pathspec = @(
        '.',
        ':(exclude)upgrade.cmd',
        ':(exclude)upgrade.ps1',
        ':(exclude)run.cmd',
        ':(exclude)source/SylphyHorn/packages.lock.json',
        ':(exclude)source/SylphyHorn.Tests/packages.lock.json'
    )
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & git.exe diff --quiet --ignore-submodules=untracked -- @pathspec
        $worktreeDirty = ($LASTEXITCODE -ne 0)
        & git.exe diff --cached --quiet --ignore-submodules=untracked -- @pathspec
        $indexDirty = ($LASTEXITCODE -ne 0)
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
    return ($worktreeDirty -or $indexDirty)
}
function Show-ProtectedTrackedChanges {
    Info 'Tracked changes that block upgrade:'
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = & git.exe status --short --untracked-files=no --ignore-submodules=untracked 2>&1
        $rc = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
    if ($rc -ne 0) { return }
    foreach ($line in $lines) {
        $text = [string]$line
        if ($text -notmatch '^.. upgrade\.cmd$' -and
            $text -notmatch '^.. upgrade\.ps1$' -and
            $text -notmatch '^.. run\.cmd$' -and
            $text -notmatch '^.. source/SylphyHorn/packages\.lock\.json$' -and
            $text -notmatch '^.. source/SylphyHorn\.Tests/packages\.lock\.json$') {
            Write-Line $text Yellow
        }
    }
}
function Restore-TrackedLockFiles {
    foreach ($path in @('source/SylphyHorn/packages.lock.json', 'source/SylphyHorn.Tests/packages.lock.json')) {
        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & git.exe ls-files --error-unmatch $path *> $null
            $tracked = ($LASTEXITCODE -eq 0)
        }
        finally {
            $ErrorActionPreference = $savedPreference
        }
        if ($tracked) {
            Run-Native -Phase 'VALIDATION-CLEANUP' -Exe 'git.exe' -ArgumentList @('restore', '--source=HEAD', '--staged', '--worktree', '--', $path) -SuppressOutput | Out-Null
        }
    }
}
function Read-RequiredSdkVersion {
    $globalJson = Join-Path $Repo 'global.json'
    if (-not (Test-Path -LiteralPath $globalJson)) { Fail 'DEPENDENCIES' 'global.json is missing.' }
    try { $json = Get-Content -LiteralPath $globalJson -Raw | ConvertFrom-Json }
    catch { Fail 'DEPENDENCIES' ("Cannot parse global.json: $($_.Exception.Message)") }
    $version = [string]$json.sdk.version
    if ([string]::IsNullOrWhiteSpace($version)) { Fail 'DEPENDENCIES' 'global.json does not define sdk.version.' }
    return $version.Trim()
}
function Ensure-DotNetSdk([string]$RequiredVersion) {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($dotnet) {
        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $sdks = & $dotnet.Source --list-sdks 2>&1
            $rc = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $savedPreference }
        if ($rc -eq 0 -and ($sdks | Where-Object { ([string]$_) -match ('^' + [regex]::Escape($RequiredVersion) + '\s+\[') })) {
            return $dotnet.Source
        }
    }

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { Fail 'DEPENDENCIES' (".NET SDK $RequiredVersion is missing and WinGet was not found.") }
    Info ("[DEPENDENCIES] Installing Microsoft.DotNet.SDK.10 $RequiredVersion with WinGet...")
    Run-Native -Phase 'DEPENDENCIES' -Exe $winget.Source -ArgumentList @('install','--id','Microsoft.DotNet.SDK.10','--exact','--version',$RequiredVersion,'--accept-package-agreements','--accept-source-agreements','--silent') | Out-Null

    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { $dotnetPath = $candidate }
    else {
        $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
        if (-not $dotnet) { Fail 'DEPENDENCIES' 'dotnet.exe is still unavailable after WinGet installation.' }
        $dotnetPath = $dotnet.Source
    }
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $sdks = & $dotnetPath --list-sdks 2>&1
        $rc = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedPreference }
    if ($rc -ne 0 -or -not ($sdks | Where-Object { ([string]$_) -match ('^' + [regex]::Escape($RequiredVersion) + '\s+\[') })) {
        Fail 'DEPENDENCIES' ("Required .NET SDK $RequiredVersion is still unavailable after installation.")
    }
    return $dotnetPath
}
function Get-SylphyHornProcesses {
    if (-not (Test-Path -LiteralPath $script:AppExe)) { return @() }
    $target = [IO.Path]::GetFullPath($script:AppExe)
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq $target)
        }
        catch {
            $false
        }
    })
}
function Test-SylphyHornRunning {
    return ((Get-SylphyHornProcesses).Count -gt 0)
}
function Stop-SylphyHorn {
    $running = @(Get-SylphyHornProcesses)
    if ($running.Count -eq 0) { return }
    Info ("[RUNTIME] Stopping SylphyHorn PID(s): " + (($running | ForEach-Object { $_.Id }) -join ', '))
    foreach ($proc in $running) {
        try { [void]$proc.CloseMainWindow() } catch { }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ((Test-SylphyHornRunning) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (Test-SylphyHornRunning) {
        Warn 'SylphyHorn did not exit within 5 seconds; forcing process termination before upgrade.'
        Get-SylphyHornProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ((Test-SylphyHornRunning) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
        if (Test-SylphyHornRunning) { Fail 'STOP-RUNTIME' 'SylphyHorn is still running and could keep build artifacts locked.' }
    }
}
function Restore-SylphyHornRuntime([switch]$FailurePath) {
    if (-not $script:AppWasRunning -or $script:RuntimeRestored) { return }
    if (-not (Test-Path -LiteralPath $script:AppExe)) {
        if ($FailurePath) { Warn ("Previous SylphyHorn runtime cannot be restored because executable is missing: $($script:AppExe)") }
        else { Fail 'RESTART' ("Built SylphyHorn executable is missing: $($script:AppExe)") }
        return
    }
    try {
        Info '[RUNTIME] Restoring SylphyHorn because it was running before upgrade...'
        Start-Process -FilePath $script:AppExe -WorkingDirectory (Split-Path -Parent $script:AppExe) | Out-Null
        Start-Sleep -Seconds 1
        if (-not (Test-SylphyHornRunning)) {
            if ($FailurePath) { Warn 'SylphyHorn could not be restarted after the failed upgrade.' }
            else { Fail 'RESTART' 'SylphyHorn did not remain running after restart.' }
            return
        }
        $script:RuntimeRestored = $true
        Info '[RUNTIME] SylphyHorn restarted successfully.'
    }
    catch {
        if ($FailurePath) { Warn ("SylphyHorn restart failed after upgrade failure: $($_.Exception.Message)") }
        else { Fail 'RESTART' ("SylphyHorn restart failed: $($_.Exception.Message)") }
    }
}

try {
    Set-Location -LiteralPath $Repo

    Info '============================================================'
    Info 'SylphyHornPlusCon upgrade diagnostic log'
    Info ("Version:    $Revision")
    Info ('Started:    ' + [DateTime]::Now.ToString('dd.MM.yyyy HH:mm:ss.fff'))
    Info ("Repository: $Repo")
    Info ("Branch:     $TargetBranch")
    $startingCommit = Get-GitText @('rev-parse','HEAD') 'SELF-UPDATE'
    Info ("Commit:     $startingCommit")
    Info 'Runner:     temporary origin/<branch> upgrade.ps1; upgrade.cmd is bootstrap launcher only'
    Info '============================================================'

    $FailPhase = 'STOP-RUNTIME'
    $AppWasRunning = Test-SylphyHornRunning
    if ($AppWasRunning) {
        Info '[RUNTIME] SylphyHorn was running before upgrade.'
        Stop-SylphyHorn
    }
    else {
        Info '[RUNTIME] SylphyHorn was not running before upgrade.'
    }

    $FailPhase = 'SELF-UPDATE'
    if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { Fail $FailPhase 'Git was not found in PATH.' }

    Run-Native -Phase $FailPhase -Exe 'git.exe' -ArgumentList @('remote','set-url','origin',$ExpectedRemote) -SuppressOutput | Out-Null
    Run-Native -Phase $FailPhase -Exe 'git.exe' -ArgumentList @('fetch','--prune','origin',$TargetBranch) | Out-Null

    $currentBranch = Get-GitText @('branch','--show-current') $FailPhase
    if ($currentBranch -ne $TargetBranch) { Fail $FailPhase ("Current branch '$currentBranch' does not match target branch '$TargetBranch'.") }

    if (Test-ProtectedTrackedDirty) {
        Show-ProtectedTrackedChanges
        Fail $FailPhase 'Tracked local changes outside maintenance-owned launchers exist. Commit or revert them before upgrade.'
    }

    Info ("[BOOTSTRAP] Synchronizing tracked tree to origin/$TargetBranch.")
    Info '[BOOTSTRAP] upgrade.cmd, upgrade.ps1, run.cmd and generated NuGet lockfiles are authoritative maintenance state.'
    Run-Native -Phase $FailPhase -Exe 'git.exe' -ArgumentList @('reset','--hard',"origin/$TargetBranch") | Out-Null

    $head = Get-GitText @('rev-parse','HEAD') $FailPhase
    $remoteHead = Get-GitText @('rev-parse',"origin/$TargetBranch") $FailPhase
    if (-not $head -or -not $remoteHead -or $head -ne $remoteHead) { Fail $FailPhase ("Local HEAD does not match origin/$TargetBranch after synchronization.") }
    $runnerHeadBlob = Get-GitText @('rev-parse','HEAD:upgrade.ps1') $FailPhase
    $runnerRemoteBlob = Get-GitText @('rev-parse',"origin/$TargetBranch`:upgrade.ps1") $FailPhase
    if ($runnerHeadBlob -ne $runnerRemoteBlob) { Fail $FailPhase 'Repository upgrade.ps1 does not match the authoritative remote runner.' }
    Info '[BOOTSTRAP] Authoritative temporary runner verified against the fetched branch.'
    Info ("[GIT] Build commit: $head")

    $FailPhase = 'SUBMODULES'
    Info '[1/5] Synchronizing Git submodules...'
    Run-Native -Phase $FailPhase -Exe 'git.exe' -ArgumentList @('submodule','sync','--recursive') | Out-Null
    Run-Native -Phase $FailPhase -Exe 'git.exe' -ArgumentList @('submodule','update','--init','--recursive','--force') | Out-Null

    $FailPhase = 'DEPENDENCIES'
    Info '[2/5] Checking required .NET SDK...'
    $requiredSdk = Read-RequiredSdkVersion
    $dotnet = Ensure-DotNetSdk $requiredSdk
    Info ("[DEPENDENCIES] .NET SDK: $requiredSdk")

    $FailPhase = 'RESTORE'
    Info '[3/5] Restoring .NET 10 projects...'
    Run-Native -Phase $FailPhase -Exe $dotnet -ArgumentList @('restore','source\SylphyHorn\SylphyHorn.csproj',"-p:TargetFramework=$TargetFramework",'--force-evaluate') | Out-Null
    Run-Native -Phase $FailPhase -Exe $dotnet -ArgumentList @('restore','source\SylphyHorn.Tests\SylphyHorn.Tests.csproj',"-p:TargetFramework=$TargetFramework",'--force-evaluate') | Out-Null

    $FailPhase = 'BUILD'
    Info '[4/5] Building Release x64 for .NET 10...'
    Run-Native -Phase $FailPhase -Exe $dotnet -ArgumentList @('build','source\SylphyHorn\SylphyHorn.csproj','-c','Release','-f',$TargetFramework,'-p:Platform=x64','-p:RunSylphyHornPostBuild=false','--no-restore') | Out-Null

    $FailPhase = 'TEST'
    Info '[5/5] Running .NET 10 unit tests...'
    $solutionDir = (Join-Path $Repo 'source') + '\'
    Run-Native -Phase $FailPhase -Exe $dotnet -ArgumentList @('test','source\SylphyHorn.Tests\SylphyHorn.Tests.csproj','-c','Release','-f',$TargetFramework,'-p:Platform=x64','-p:RunSylphyHornPostBuild=false',("-p:SolutionDir=$solutionDir"),'--no-restore') | Out-Null

    $FailPhase = 'VALIDATION-CLEANUP'
    Restore-TrackedLockFiles
    if (Test-ProtectedTrackedDirty) {
        Show-ProtectedTrackedChanges
        Fail $FailPhase 'Upgrade validation generated unexpected tracked changes.'
    }

    $FailPhase = 'RESTART'
    Restore-SylphyHornRuntime

    Info '============================================================'
    if ($HadWarning) {
        Write-Line 'UPGRADE OK WITH WARNINGS' Yellow
        Info 'STATUS: WARNING - phase=COMPLETE'
    }
    else {
        Write-Line 'UPGRADE OK' Green
        Info 'STATUS: SUCCESS - phase=COMPLETE'
    }
    Info (".NET SDK: $requiredSdk")
    Info ("Target:   $TargetFramework")
    Info ("Commit:   $head")
    Info ("Log:      $Log")
    exit 0
}
catch {
    $message = $_.Exception.Message
    if ($message -and -not ($message -match '^Tracked local changes outside maintenance-owned launchers exist\.' )) {
        if (-not ($message -match '^git\.exe failed|^dotnet\.exe failed|^winget\.exe failed')) { Write-Line ('ERROR DETAIL: ' + $message) Red }
    }
    Restore-SylphyHornRuntime -FailurePath
    Write-Line ("STATUS: FAILED - phase=$FailPhase") Red
    Write-Line ("Log: $Log") Red
    exit 1
}