param(
    [ValidateSet('main','devel')]
    [string]$Branch = 'devel'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root
$LogsDirectory = Join-Path $Root 'logs'
New-Item -ItemType Directory -Path $LogsDirectory -Force | Out-Null
$Log = Join-Path $LogsDirectory 'install.log'
Remove-Item $Log -Force -ErrorAction SilentlyContinue
Start-Transcript -Path $Log -Force | Out-Null

try {
    Write-Host 'SylphyHornPlusCon first-run installer'
    Write-Host "Repository: $Root"
    Write-Host "Branch:     $Branch"
    Write-Host ''

    . (Join-Path $Root 'scripts\Environment.ps1')
    $git = Ensure-Git
    $sdkVersion = Get-RequiredDotNetVersion $Root
    $dotnet = Ensure-DotNetSdk -Version $sdkVersion -Root $Root

    if (-not (Test-Path (Join-Path $Root '.git'))) {
        throw 'This folder is not a Git working copy. Clone Suenee/SylphyHornPlusCon first, then run install.cmd from the repository root.'
    }

    Write-Step 'Verifying repository and branch.'
    & $git -c safe.directory=* remote set-url origin 'https://github.com/Suenee/SylphyHornPlusCon.git'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to configure Git origin.' }

    & $git -c safe.directory=* fetch origin $Branch
    if ($LASTEXITCODE -ne 0) { throw 'Unable to fetch the requested branch.' }

    $dirty = (& $git -c safe.directory=* status --porcelain --untracked-files=no)
    if ($dirty) { throw 'Tracked local changes are present. Commit or revert them before installation.' }

    $current = (& $git -c safe.directory=* branch --show-current).Trim()
    if ($current -ne $Branch) {
        & $git -c safe.directory=* checkout $Branch 2>$null
        if ($LASTEXITCODE -ne 0) {
            & $git -c safe.directory=* checkout -b $Branch --track "origin/$Branch"
            if ($LASTEXITCODE -ne 0) { throw "Unable to switch to branch $Branch." }
        }
    }

    Write-Step 'Initializing and updating Git submodules.'
    & $git -c safe.directory=* submodule sync --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule sync failed.' }
    & $git -c safe.directory=* submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule update failed.' }

    Write-Step 'Restoring NuGet packages.'
    & $dotnet restore 'source\SylphyHorn.sln' --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Step 'Building Release x64.'
    & $dotnet build 'source\SylphyHorn.sln' -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    Write-Step 'Running unit tests.'
    & $dotnet test 'source\SylphyHorn.Tests\SylphyHorn.Tests.csproj' -c Release -p:Platform=x64 -p:RunSylphyHornPostBuild=false -p:SolutionDir="$Root\source\" --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    Write-Host ''
    Write-Host 'INSTALL COMPLETED SUCCESSFULLY'
    Write-Host "Git:     $git"
    Write-Host ".NET SDK: $sdkVersion"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch {}
}
