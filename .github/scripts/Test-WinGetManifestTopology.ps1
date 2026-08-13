#requires -Version 7.2

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$topologyScript = Join-Path $PSScriptRoot "Get-WinGetManifestTopology.ps1"

function New-Manifest {
	param(
		[Parameter(Mandatory = $true)]
		[string] $RelativeFilePath,

		[Parameter(Mandatory = $true)]
		[string[]] $Architectures
	)

	return [pscustomobject]@{
		PackageIdentifier = "hwtnb.SylphyHornPlus"
		InstallerType = "zip"
		NestedInstallerType = "portable"
		UpgradeBehavior = "install"
		NestedInstallerFiles = @([pscustomobject]@{
			RelativeFilePath = $RelativeFilePath
			PortableCommandAlias = "SylphyHornPlus"
		})
		Installers = @($Architectures | ForEach-Object {
			[pscustomobject]@{ Architecture = $_ }
		})
	}
}

$legacy = & $topologyScript -Manifest (
	New-Manifest "SylphyHorn/SylphyHorn.exe" @("x86"))
if ($legacy.InstallerCount -ne 1 -or
	$legacy.Topology -cne "LegacySingleArchitecture") {
	throw "The legacy one-architecture fixture was not classified correctly."
}

$launcher = & $topologyScript -Manifest (
	New-Manifest `
		"SylphyHorn/SylphyHorn.WinGetLauncher.exe" `
		@("x86", "x64", "arm64"))
if ($launcher.InstallerCount -ne 3 -or
	$launcher.Topology -cne "LauncherThreeArchitecture") {
	throw "The launcher three-architecture fixture was not classified correctly."
}

foreach ($invalidManifest in @(
	(New-Manifest "SylphyHorn/SylphyHorn.WinGetLauncher.exe" @("x86")),
	(New-Manifest "SylphyHorn/SylphyHorn.exe" @("x86", "x64", "arm64"))
)) {
	$failed = $false
	try {
		$null = & $topologyScript -Manifest $invalidManifest
	}
	catch {
		$failed = $true
	}
	if (-not $failed) {
		throw "An invalid mixed WinGet migration topology was accepted."
	}
}

"WinGet manifest topology fixtures passed."
