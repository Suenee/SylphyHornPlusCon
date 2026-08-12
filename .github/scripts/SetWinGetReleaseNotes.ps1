#requires -Version 7.2

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
	[string] $ManifestDirectory,

	[Parameter(Mandatory = $true)]
	[ValidateNotNullOrEmpty()]
	[string] $ReleaseNotes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$normalizedNotes = $ReleaseNotes.Replace("`r`n", "`n").Trim()
if ([string]::IsNullOrWhiteSpace($normalizedNotes)) {
	throw "Release notes must not be empty."
}
if ($normalizedNotes.IndexOfAny(@([char] 0x00, [char] 0x0B, [char] 0x0C)) -ge 0) {
	throw "Release notes contain characters that are not valid in a YAML block scalar."
}

$localeManifests = @(Get-ChildItem `
	-LiteralPath $ManifestDirectory `
	-Filter '*.locale.en-US.yaml' `
	-File `
	-Recurse)
if ($localeManifests.Count -ne 1) {
	throw "Expected exactly one en-US locale manifest, found $($localeManifests.Count)."
}

$manifestPath = $localeManifests[0].FullName
$manifest = [System.IO.File]::ReadAllText($manifestPath)
if ([regex]::IsMatch($manifest, '(?m)^ReleaseNotes\s*:')) {
	throw "The locale manifest already contains ReleaseNotes: $manifestPath"
}
$releaseNotesUrlMatches = [regex]::Matches($manifest, '(?m)^ReleaseNotesUrl\s*:')
if ($releaseNotesUrlMatches.Count -ne 1) {
	throw "Expected exactly one ReleaseNotesUrl property: $manifestPath"
}

$indentedNotes = ($normalizedNotes -split "`n" | ForEach-Object { "  $_" }) -join "`n"
$releaseNotesBlock = "ReleaseNotes: |-`n$indentedNotes`n"
$updated = [regex]::Replace(
	$manifest,
	'(?m)^ReleaseNotesUrl\s*:',
	$releaseNotesBlock + 'ReleaseNotesUrl:',
	1)

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
	$manifestPath,
	$updated.Replace("`r`n", "`n"),
	$utf8NoBom)

Write-Output $localeManifests[0].Directory.FullName
