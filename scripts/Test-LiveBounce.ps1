param([switch]$RequireEnabled)
$ErrorActionPreference = 'Stop'
$snapshotPath = Join-Path $env:APPDATA 'XIVLauncherCN\pluginConfigs\q弹狒狒\diagnostics.json'
$file = Get-Item -LiteralPath $snapshotPath
if (((Get-Date) - $file.LastWriteTime).TotalSeconds -gt 10) { throw '运行诊断已过期，请先重新加载诊断版插件。' }
$snapshot = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json
if (!$snapshot.Local.Accepted) { throw "本地模型被拒绝：$($snapshot.Local.Rejection)。LoadState=$($snapshot.Local.LoadState)，Parent=$($snapshot.Local.ParentPresent)" }
if ($RequireEnabled -and (!$snapshot.Enabled -or !$snapshot.SceneAvailable -or $snapshot.Processed -eq 0)) {
    throw "没有实际变换回调：Enabled=$($snapshot.Enabled)，Scene=$($snapshot.SceneAvailable)，Tracked=$($snapshot.Tracked)，Processed=$($snapshot.Processed)，Hooks=$($snapshot.Hooks)"
}
Write-Output "实时检查通过：本地模型已接受；Tracked=$($snapshot.Tracked)，Processed=$($snapshot.Processed)，Hooks=$($snapshot.Hooks)"
