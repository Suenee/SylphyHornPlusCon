#requires -Version 7.2

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[psobject] $Manifest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Manifest.PackageIdentifier -cne "hwtnb.SylphyHornPlus" -or
	$Manifest.InstallerType -cne "zip" -or
	$Manifest.NestedInstallerType -cne "portable" -or
	$Manifest.UpgradeBehavior -cne "install") {
	throw "The current WinGet manifest no longer matches the supported package contract."
}

$nestedFiles = @($Manifest.NestedInstallerFiles)
if ($nestedFiles.Count -ne 1 -or
	$nestedFiles[0].PortableCommandAlias -cne "SylphyHornPlus") {
	throw "The current WinGet nested portable contract has changed."
}

$installerCount = @($Manifest.Installers).Count
$actualArchitectures = @(
	$Manifest.Installers |
		Select-Object -ExpandProperty Architecture |
		Sort-Object)
if ($installerCount -eq 1) {
	if ($nestedFiles[0].RelativeFilePath -cne "SylphyHorn/SylphyHorn.exe" -or
		@(Compare-Object @("x86") $actualArchitectures).Count -ne 0) {
		throw "The current one-architecture WinGet manifest is not the reviewed legacy migration source."
	}

	return [pscustomobject]@{
		InstallerCount = 1
		Topology = "LegacySingleArchitecture"
	}
}

if ($installerCount -eq 3) {
	if ($nestedFiles[0].RelativeFilePath -cne
		"SylphyHorn/SylphyHorn.WinGetLauncher.exe" -or
		@(Compare-Object @("arm64", "x64", "x86") $actualArchitectures).Count -ne 0) {
		throw "The current three-architecture WinGet manifest no longer matches the launcher contract."
	}

	return [pscustomobject]@{
		InstallerCount = 3
		Topology = "LauncherThreeArchitecture"
	}
}

throw "Unsupported current WinGet installer count: $installerCount"
