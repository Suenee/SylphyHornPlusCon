#requires -Version 7.2

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[AllowEmptyString()]
	[string] $ReleaseBody
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$startMarker = '<!-- winget-release-notes:start -->'
$endMarker = '<!-- winget-release-notes:end -->'
$startCount = ([regex]::Matches($ReleaseBody, [regex]::Escape($startMarker))).Count
$endCount = ([regex]::Matches($ReleaseBody, [regex]::Escape($endMarker))).Count
$matches = [regex]::Matches(
	$ReleaseBody,
	"(?s)$([regex]::Escape($startMarker))(?<Notes>.*?)$([regex]::Escape($endMarker))")

if ($startCount -ne 1 -or $endCount -ne 1 -or $matches.Count -ne 1) {
	throw "Release notes must contain exactly one ordered '$startMarker' / '$endMarker' marker pair."
}

$notes = $matches[0].Groups['Notes'].Value.Replace("`r`n", "`n").Trim([char[]] "`r`n")
if ([string]::IsNullOrWhiteSpace($notes)) {
	throw "The WinGet release-notes section must not be empty."
}

Write-Output $notes
