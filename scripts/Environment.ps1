Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) { Write-Host "==> $Message" }

function Ensure-Git {
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($git) { return $git.Source }

    Write-Step 'Git for Windows is missing; installing it.'
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw 'Git is missing and WinGet is not available. Install Microsoft App Installer/WinGet and run install.cmd again.'
    }

    & $winget.Source install --id Git.Git --exact --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw "Git installation failed with exit code $LASTEXITCODE." }

    $candidates = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
        "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    if (-not $candidates) { throw 'Git was installed but git.exe could not be located.' }
    $gitPath = $candidates[0]
    $env:Path = "$(Split-Path $gitPath);$env:Path"
    return $gitPath
}

function Ensure-DotNetSdk([string]$Version, [string]$Root) {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($dotnet) {
        $sdks = & $dotnet.Source --list-sdks 2>$null
        if ($sdks -match "^$([regex]::Escape($Version))\s") { return $dotnet.Source }
    }

    $installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $localDotnet = Join-Path $installDir 'dotnet.exe'
    if (Test-Path $localDotnet) {
        $sdks = & $localDotnet --list-sdks 2>$null
        if ($sdks -match "^$([regex]::Escape($Version))\s") {
            $env:DOTNET_ROOT = $installDir
            $env:Path = "$installDir;$env:Path"
            return $localDotnet
        }
    }

    Write-Step ".NET SDK $Version is missing; installing the exact Microsoft SDK for the current user."
    $bootstrap = Join-Path $env:TEMP "dotnet-install-$PID.ps1"
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $bootstrap
    try {
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $bootstrap -Version $Version -InstallDir $installDir -NoPath
        if ($LASTEXITCODE -ne 0) { throw ".NET SDK installation failed with exit code $LASTEXITCODE." }
    }
    finally { Remove-Item $bootstrap -Force -ErrorAction SilentlyContinue }

    if (-not (Test-Path $localDotnet)) { throw '.NET installer completed but dotnet.exe is missing.' }
    $env:DOTNET_ROOT = $installDir
    $env:Path = "$installDir;$env:Path"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $installDir) {
        $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $installDir } else { "$installDir;$userPath" }
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    }
    [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $installDir, 'User')
    return $localDotnet
}

function Get-RequiredDotNetVersion([string]$Root) {
    $globalJson = Join-Path $Root 'global.json'
    if (-not (Test-Path $globalJson)) { throw 'global.json is missing.' }
    $cfg = Get-Content $globalJson -Raw | ConvertFrom-Json
    return [string]$cfg.sdk.version
}
