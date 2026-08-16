#requires -Version 7.2

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script = Join-Path $PSScriptRoot "Get-WinGetReleaseNotes.ps1"
$setScript = Join-Path $PSScriptRoot "SetWinGetReleaseNotes.ps1"
function Assert-Notes {
	param(
		[Parameter(Mandatory = $true)]
		[string] $Body,

		[Parameter(Mandatory = $true)]
		[string] $Expected
	)

	$actual = (& $script -ReleaseBody $Body) -join "`n"
	if ($actual -cne $Expected) {
		throw "Unexpected release notes. Expected '$Expected'; actual '$actual'."
	}
}

function Assert-Rejected {
	param(
		[Parameter(Mandatory = $true)]
		[AllowEmptyString()]
		[string] $Body
	)

	$failed = $false
	try {
		$null = & $script -ReleaseBody $Body
	}
	catch {
		$failed = $true
	}
	if (-not $failed) {
		throw "An invalid release body was accepted."
	}
}

Assert-Notes `
	-Body "## Highlights`r`n`r`n<!-- winget-release-notes:start -->`r`n- First`r`n- Second`r`n<!-- winget-release-notes:end -->`r`n`r`n## Notes`r`nOther" `
	-Expected "- First`n- Second"
Assert-Notes `
	-Body "> [!IMPORTANT]`n> Notice`n`n<!-- winget-release-notes:start -->`n## Fixed`n`n- Fixed`n<!-- winget-release-notes:end -->`n`n## Installation`nText" `
	-Expected "## Fixed`n`n- Fixed"
Assert-Notes `
	-Body "<!-- winget-release-notes:start -->`n    winget upgrade hwtnb.SylphyHornPlus`n<!-- winget-release-notes:end -->" `
	-Expected "    winget upgrade hwtnb.SylphyHornPlus"
Assert-Rejected -Body ""
Assert-Rejected -Body "## Notes`nText"
Assert-Rejected -Body "<!-- winget-release-notes:start -->`n`n<!-- winget-release-notes:end -->"
Assert-Rejected -Body "<!-- winget-release-notes:start -->`n- Missing end"
Assert-Rejected -Body "- Missing start`n<!-- winget-release-notes:end -->"
Assert-Rejected -Body "<!-- winget-release-notes:end -->`n- Reversed`n<!-- winget-release-notes:start -->"
Assert-Rejected -Body "<!-- winget-release-notes:start -->`n<!-- winget-release-notes:start -->`n- Duplicate start`n<!-- winget-release-notes:end -->"
Assert-Rejected -Body "<!-- winget-release-notes:start -->`n- Duplicate end`n<!-- winget-release-notes:end -->`n<!-- winget-release-notes:end -->"
Assert-Rejected -Body "<!-- winget-release-notes:start -->`n- One`n<!-- winget-release-notes:end -->`n<!-- winget-release-notes:start -->`n- Two`n<!-- winget-release-notes:end -->"

$temporaryDirectory = Join-Path `
	([System.IO.Path]::GetTempPath()) `
	("SylphyHornPlus-WinGetReleaseNotes-" + [Guid]::NewGuid().ToString("N"))
try {
	[void] [System.IO.Directory]::CreateDirectory($temporaryDirectory)
	$manifestPath = Join-Path $temporaryDirectory "hwtnb.SylphyHornPlus.locale.en-US.yaml"
	[System.IO.File]::WriteAllText(
		$manifestPath,
		"PackageIdentifier: hwtnb.SylphyHornPlus`nReleaseNotesUrl: https://example.invalid/release`n",
		[System.Text.UTF8Encoding]::new($false))
	$releaseNotes = (& $script `
		-ReleaseBody "<!-- winget-release-notes:start -->`n    winget upgrade hwtnb.SylphyHornPlus`nNormal paragraph.`n<!-- winget-release-notes:end -->") -join "`n"
	$null = & $setScript `
		-ManifestDirectory $temporaryDirectory `
		-ReleaseNotes $releaseNotes
	$updatedManifest = [System.IO.File]::ReadAllText($manifestPath)
	$expectedBlock = "ReleaseNotes: |2-`n      winget upgrade hwtnb.SylphyHornPlus`n  Normal paragraph.`nReleaseNotesUrl:"
	if (-not $updatedManifest.Contains($expectedBlock, [StringComparison]::Ordinal)) {
		throw "The YAML ReleaseNotes block did not preserve Markdown indentation."
	}
}
finally {
	if ([System.IO.Directory]::Exists($temporaryDirectory)) {
		[System.IO.Directory]::Delete($temporaryDirectory, $true)
	}
}

"WinGet release-notes fixtures passed."
