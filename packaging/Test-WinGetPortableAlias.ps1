#requires -Version 7.2

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string] $LauncherPath,

	[Parameter(Mandatory = $true)]
	[string] $ProbePath,

	[Parameter()]
	[switch] $RequireSymbolicLink
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Condition {
	param(
		[Parameter(Mandatory = $true)]
		[bool] $Condition,

		[Parameter(Mandatory = $true)]
		[string] $Message
	)

	if (-not $Condition) {
		throw $Message
	}
}

$resolvedLauncher = (Resolve-Path -LiteralPath $LauncherPath).Path
$resolvedProbe = (Resolve-Path -LiteralPath $ProbePath).Path
$temporaryRoot = Join-Path `
	([System.IO.Path]::GetTempPath()) `
	("SylphyHornPlus-AliasTest-{0}" -f ([guid]::NewGuid().ToString("N")))
$packageRoot = Join-Path $temporaryRoot "package with spaces"
$linksRoot = Join-Path $temporaryRoot "WinGet Links"
$aliasPath = Join-Path $linksRoot "SylphyHornPlus.exe"
$probePath = Join-Path $packageRoot "SylphyHorn.exe"
$markerPath = Join-Path $temporaryRoot "alias-launched.txt"
$launcherProcess = $null

try {
	New-Item -ItemType Directory -Path $packageRoot, $linksRoot | Out-Null
	Copy-Item -LiteralPath $resolvedLauncher -Destination `
		(Join-Path $packageRoot "SylphyHorn.WinGetLauncher.exe")
	Copy-Item -LiteralPath $resolvedProbe -Destination $probePath
	try {
		New-Item -ItemType SymbolicLink -Path $aliasPath -Target `
			(Join-Path $packageRoot "SylphyHorn.WinGetLauncher.exe") | Out-Null
	}
	catch [System.UnauthorizedAccessException] {
		if ($RequireSymbolicLink) {
			throw
		}

		Write-Warning `
			"WinGet alias integration test skipped because symbolic-link creation is not permitted."
		return [pscustomobject]@{
			Status = "Skipped"
			Reason = "SymbolicLinkPrivilegeUnavailable"
		}
	}

	$expectedArguments = @(
		"",
		"value with spaces",
		'embedded"quote',
		'trailing\',
		'slashes\\\"quote',
		"日本語")
	$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
	$startInfo.FileName = $aliasPath
	$startInfo.UseShellExecute = $false
	$startInfo.Environment["SYLPHYHORN_ALIAS_TEST_RESULT"] = $markerPath
	foreach ($argument in $expectedArguments) {
		$startInfo.ArgumentList.Add($argument)
	}
	$launcherProcess = [System.Diagnostics.Process]::Start($startInfo)
	$deadline = [DateTime]::UtcNow.AddSeconds(10)
	while (-not $launcherProcess.HasExited) {
		if ([DateTime]::UtcNow -ge $deadline) {
			$launcherProcess.Kill()
			$launcherProcess.WaitForExit()
			throw "The WinGet alias launcher did not exit before the timeout."
		}

		Start-Sleep -Milliseconds 25
	}
	Assert-Condition ($launcherProcess.ExitCode -eq 0) `
		"WinGet alias launcher returned exit code $($launcherProcess.ExitCode)."

	while (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
		if ([DateTime]::UtcNow -ge $deadline) {
			throw "The WinGet alias did not launch the sibling SylphyHorn.exe probe."
		}

		Start-Sleep -Milliseconds 25
	}

	$stream = [System.IO.File]::OpenRead($markerPath)
	$probeProcessId = 0
	try {
		$reader = [System.IO.BinaryReader]::new($stream)
		try {
			Assert-Condition ($reader.ReadUInt32() -eq 0x31414853) `
				"The alias probe result has an invalid header."
			$probeProcessId = $reader.ReadUInt32()
			Assert-Condition ($probeProcessId -ne 0) `
				"The alias probe result contains an invalid process ID."
			$argumentCount = $reader.ReadUInt32()
			Assert-Condition ($argumentCount -eq $expectedArguments.Count) `
				"The alias probe received $argumentCount arguments instead of $($expectedArguments.Count)."
			$actualArguments = @()
			for ($index = 0; $index -lt $argumentCount; ++$index) {
				$characterCount = $reader.ReadUInt32()
				$bytes = $reader.ReadBytes($characterCount * 2)
				Assert-Condition ($bytes.Length -eq $characterCount * 2) `
					"The alias probe result ended inside argument $index."
				$actualArguments += [Text.Encoding]::Unicode.GetString($bytes)
			}
			Assert-Condition ($stream.Position -eq $stream.Length) `
				"The alias probe result contains trailing data."
		}
		finally {
			$reader.Dispose()
		}
	}
	finally {
		$stream.Dispose()
	}
	Assert-Condition `
		(@(Compare-Object $expectedArguments $actualArguments -SyncWindow 0).Count -eq 0) `
		"The WinGet alias did not preserve argument boundaries."

	try {
		$probeProcess = [System.Diagnostics.Process]::GetProcessById($probeProcessId)
	}
	catch [System.ArgumentException] {
		$probeProcess = $null
	}
	if ($null -ne $probeProcess) {
		try {
			while (-not $probeProcess.HasExited) {
				if ([DateTime]::UtcNow -ge $deadline) {
					throw "The alias probe process did not exit before the timeout."
				}

				Start-Sleep -Milliseconds 25
			}
		}
		finally {
			$probeProcess.Dispose()
		}
	}

	return [pscustomobject]@{
		Status = "Passed"
		Reason = $null
	}
}
finally {
	if ($null -ne $launcherProcess) {
		if (-not $launcherProcess.HasExited) {
			$launcherProcess.Kill()
			$launcherProcess.WaitForExit()
		}
		$launcherProcess.Dispose()
	}
	if (Test-Path -LiteralPath $temporaryRoot) {
		Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
	}
}
