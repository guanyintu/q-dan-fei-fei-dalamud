param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifestPath = Join-Path $PackageDirectory 'q弹狒狒.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$name = $manifest.InternalName
if ($name -ne 'q弹狒狒' -or $manifest.Name -ne 'q弹狒狒' -or $manifest.Author -ne 'someboy') {
    throw '发布清单的内部名称、显示名称或作者不正确。'
}
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $PackageDirectory 'latest.zip'))
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        $path = $entry.FullName.Replace('\','/')
        if ($path.EndsWith('/')) { continue }
        if ($entries.ContainsKey($path) -or $path.StartsWith('/') -or $path -match '(^|/)\.\.(/|$)') { throw "非法或重复路径：$path" }
        if ($path -match '\.(zip|br|pdb|exe)$|(^|/)QBounce\.(dll|json)$|(^|/)(Dalamud|FFXIVClientStructs|InteropGenerator\.Runtime)\.dll$') {
            throw "官方发布包包含旧产物、嵌套归档或游戏提供的依赖：$path"
        }
        $entries[$path] = $entry
    }
    foreach ($required in @("$name.dll","$name.json","$name.deps.json",'Assets/bgm.mp3','Assets/bgm-source.json','THIRD-PARTY-NOTICES.md')) {
        if (!$entries.ContainsKey($required) -or $entries[$required].Length -eq 0) { throw "缺少运行文件：$required" }
    }
    function ReadZipText($entry) {
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $packedManifest = ReadZipText $entries["$name.json"]
    if ($packedManifest -cne [IO.File]::ReadAllText($manifestPath)) { throw '独立清单与 ZIP 内的清单不一致。' }
    $dependencies = ReadZipText $entries["$name.deps.json"] | ConvertFrom-Json
    foreach ($target in $dependencies.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            foreach ($runtime in $library.Value.runtime.PSObject.Properties) {
                $file = [IO.Path]::GetFileName($runtime.Name)
                if (!$entries.ContainsKey($file)) { throw "缺少 deps.json 声明的依赖：$file" }
            }
        }
    }
    $musicSource = ReadZipText $entries['Assets/bgm-source.json'] | ConvertFrom-Json
    if ($musicSource.webpage_url -ne 'https://www.bilibili.com/video/BV1VGYR6bECg/') { throw '音乐出处不正确。' }
    $musicStream = $entries['Assets/bgm.mp3'].Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $musicHash = [Convert]::ToHexString($sha.ComputeHash($musicStream)).ToLowerInvariant() }
    finally { $sha.Dispose(); $musicStream.Dispose() }
    if ($musicHash -ne $musicSource.sha256 -or $entries['Assets/bgm.mp3'].Length -ne $musicSource.size_bytes) { throw '音乐内容与来源记录不一致。' }
    Write-Output "官方发布包通过：latest.zip / $name.json，版本 $($manifest.AssemblyVersion)，$($entries.Count) 个文件，依赖与音乐齐全。"
} finally { $zip.Dispose() }
