param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$DalamudPath,
    [string]$GameVersionFile
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$runtime = Get-Content -LiteralPath (Join-Path $DalamudPath 'version.json') -Raw | ConvertFrom-Json
$target = $runtime.SupportedGameVer
if ($GameVersionFile) { $target = (Get-Content -LiteralPath $GameVersionFile -Raw).Trim() }
if ($target -ne $runtime.SupportedGameVer) { throw "Dalamud 目标 $($runtime.SupportedGameVer) 与游戏 $target 不匹配。" }
if ($manifest.DalamudApiLevel -ne 15) { throw '本工程需要 Dalamud API 15。' }
if ($manifest.ApplicableVersion -ne $target) {
    throw "QBounce manifest 适用版本 $($manifest.ApplicableVersion) 与当前目标 $target 不匹配；加载器会拒绝旧版本上限。"
}
Write-Output "兼容性通过：API $($manifest.DalamudApiLevel) / Dalamud $($runtime.AssemblyVersion) / 游戏 $target"
