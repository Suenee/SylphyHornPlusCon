# ビルド後に実行
# - .NET Framework: SylphyHorn.exe (とその関連ファイル) を除くすべてのファイルを lib フォルダーに移動
# - .NET: SDK が生成する標準の実行レイアウトを維持

Param ( $TargetDir, $TargetFramework )

if ( $TargetFramework -ne "net48" ) {
    $requiredFiles = "SylphyHorn.exe", "SylphyHorn.dll", "SylphyHorn.deps.json", "SylphyHorn.runtimeconfig.json"
    foreach ( $requiredFile in $requiredFiles ) {
        if ( -not (Test-Path (Join-Path $TargetDir $requiredFile)) ) {
            throw "Required .NET application host file was not found: $requiredFile"
        }
    }

    return
}

$targets = $TargetDir
$lib = Join-Path $TargetDir "lib"
$excludes = ".assets", "SylphyHorn.exe*", "SylphyHorn.pdb", "SchedulerManager.exe*", "SchedulerManager.pdb", "lib"

if ( Test-Path $lib ) {
    Remove-Item $lib -Recurse
}

New-Item $lib -ItemType Directory

Get-ChildItem $targets -Exclude $excludes | Move-Item -Destination $lib
