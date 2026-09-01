param(
    [Parameter(Mandatory=$true)][string]$Root,
    [ValidateSet('main','devel')][string]$Branch = 'devel'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path.TrimEnd('\')
Set-Location $Root
$LogsDirectory = Join-Path $Root 'logs'
New-Item -ItemType Directory -Path $LogsDirectory -Force | Out-Null
$Log = Join-Path $LogsDirectory 'upgrade.log'
$Phase = 'SELF-UPDATE'
$StatusWritten = $false
Remove-Item $Log -Force -ErrorAction SilentlyContinue
Start-Transcript -Path $Log -Force | Out-Null

function Complete-Status([string]$Status, [string]$PhaseName) {
    if ($script:StatusWritten) { return }
    Write-Host "STATUS: $Status - phase=$PhaseName"
    $script:StatusWritten = $true
}

try {
    Write-Host 'SylphyHornPlusCon upgrade'
    Write-Host "Repository: $Root"
    Write-Host "Branch:     $Branch"
    Write-Host ''

    if (-not (Test-Path (Join-Path $Root '.git'))) {
        throw 'This installation is not a Git working copy. Run install.cmd from a cloned repository first.'
    }

    $Phase = 'DEPENDENCIES'
    . (Join-Path $Root 'scripts\Environment.ps1')
    $git = Ensure-Git

    $Phase = 'SYNC'
    Write-Step 'Checking tracked local changes.'
    $tracked = & $git -c safe.directory=* status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the Git working tree.' }
    if ($tracked) { throw 'Tracked local changes are present. Commit or revert them before upgrade.' }

    & $git -c safe.directory=* remote set-url origin 'https://github.com/Suenee/SylphyHornPlusCon.git'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to configure Git origin.' }

    Write-Step "Fetching origin/$Branch."
    & $git -c safe.directory=* fetch --prune origin $Branch
    if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }

    $current = (& $git -c safe.directory=* branch --show-current).Trim()
    if ($current -ne $Branch) {
        throw "Current branch is '$current', but upgrade targets '$Branch'. Switch branches explicitly before upgrading."
    }

    Write-Step 'Fast-forwarding the local branch.'
    & $git -c safe.directory=* merge --ff-only "origin/$Branch"
    if ($LASTEXITCODE -ne 0) { throw 'Fast-forward update failed. Local history diverges from origin.' }

    $head = (& $git -c safe.directory=* rev-parse HEAD).Trim()
    $remoteHead = (& $git -c safe.directory=* rev-parse "origin/$Branch").Trim()
    if ($head -ne $remoteHead) { throw 'Repository verification failed: HEAD does not match origin branch.' }

    $Phase = 'SUBMODULES'
    Write-Step 'Synchronizing Git submodules.'
    & $git -c safe.directory=* submodule sync --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule sync failed.' }
    & $git -c safe.directory=* submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule update failed.' }

    $Phase = 'DEPENDENCIES'
    # Reload the helper after synchronization so upgrades always use its newest version.
    . (Join-Path $Root 'scripts\Environment.ps1')
    $sdkVersion = Get-RequiredDotNetVersion $Root
    $dotnet = Ensure-DotNetSdk -Version $sdkVersion -Root $Root

    $Phase = 'VERIFY'
    Write-Step 'Restoring NuGet packages.'
    & $dotnet restore 'source\SylphyHorn.sln' --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Step 'Building Release x64.'
    & $dotnet build 'source\SylphyHorn.sln' -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    Write-Step 'Running unit tests.'
    & $dotnet test 'source\SylphyHorn.Tests\SylphyHorn.Tests.csproj' -c Release -p:Platform=x64 -p:RunSylphyHornPostBuild=false -p:SolutionDir="$Root\source\" --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    $Phase = 'COMPLETE'
    Write-Host ''
    Write-Host 'UPGRADE COMPLETED SUCCESSFULLY'
    Write-Host "Commit: $head"
    Complete-Status 'SUCCESS' 'COMPLETE'
    exit 0
}
catch {
    Write-Error $_
    Complete-Status 'FAILED' $Phase
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch {}
}
