param(
    [string]$DalamudPath,
    [string]$GameVersionFile,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if (!$DalamudPath) {
    if ($env:DALAMUD_HOME) { $DalamudPath = $env:DALAMUD_HOME }
    else {
        $candidates = foreach ($launcher in @('XIVLauncherCN','XIVLauncher')) {
            $hooksRoot = Join-Path $env:APPDATA "$launcher\addon\Hooks"
            if (!(Test-Path -LiteralPath $hooksRoot)) { continue }
            foreach ($directory in Get-ChildItem -LiteralPath $hooksRoot -Directory) {
                $versionFile = Join-Path $directory.FullName 'version.json'
                if (!(Test-Path -LiteralPath $versionFile) -or !(Test-Path -LiteralPath (Join-Path $directory.FullName 'Dalamud.dll'))) { continue }
                $metadata = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
                $runtimeVersion = [version]$metadata.AssemblyVersion
                if ($runtimeVersion.Major -eq 15) {
                    [PSCustomObject]@{ Path = $directory.FullName; Version = $runtimeVersion }
                }
            }
        }
        $DalamudPath = ($candidates | Sort-Object Version -Descending | Select-Object -First 1).Path
    }
}
if (!$DalamudPath) { throw '找不到 Dalamud.dll，请使用 -DalamudPath 指定 API 15 的目录。' }
$DalamudPath = (Resolve-Path -LiteralPath $DalamudPath).Path
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $localDotnet) { $dotnet = $localDotnet }
elseif ($dotnetCommand) { $dotnet = $dotnetCommand.Source }
else { throw '请安装 .NET 10 SDK。' }
if (!(& $dotnet --list-sdks | Select-String '^10\.')) { throw '需要 .NET 10 SDK；只有 runtime 无法编译。' }

$dist = Join-Path $projectRoot 'dist'
$package = Join-Path $dist 'q弹狒狒-卫月版'
$reports = Join-Path $dist 'reports'
New-Item -ItemType Directory -Path $package,$reports -Force | Out-Null
$libArgument = '-p:DalamudLibPath=' + $DalamudPath + '\'
$output = Join-Path $projectRoot 'QBounce\bin\Release'
& $dotnet build (Join-Path $projectRoot 'QBounce\q弹狒狒.csproj') -c Release $libArgument -p:RestoreLockedMode=true -v minimal 2>&1 |
    Tee-Object -FilePath (Join-Path $reports 'build.log')
if ($LASTEXITCODE -ne 0) { throw '插件构建失败。' }
& (Join-Path $projectRoot 'scripts\Test-PluginCompatibility.ps1') -ManifestPath (Join-Path $output 'q弹狒狒.json') -DalamudPath $DalamudPath -GameVersionFile $GameVersionFile |
    Tee-Object -FilePath (Join-Path $reports 'compatibility.log')
if (!$SkipTests) {
    & $dotnet test (Join-Path $projectRoot 'QBounce.Tests\QBounce.Tests.csproj') -c Release $libArgument -p:RestoreLockedMode=true `
        --logger 'trx;LogFileName=QBounce.trx' --results-directory $reports -v minimal 2>&1 |
        Tee-Object -FilePath (Join-Path $reports 'tests.log')
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
}
$files = @('q弹狒狒.json','q弹狒狒.deps.json','NAudio.Core.dll','NAudio.WinMM.dll','NAudio.Wasapi.dll','q弹狒狒.dll')
foreach ($asset in @('Assets\bgm.mp3','Assets\bgm-source.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $output $asset))) { continue }
    $assetDirectory = Split-Path -Parent (Join-Path $package $asset)
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $output $asset) -Destination (Join-Path $package $asset) -Force
    $files += $asset
}
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $output $file) -Destination (Join-Path $package $file) -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $package -Force
$packageDocs = Join-Path $package 'docs'
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\VALIDATION.md'),(Join-Path $projectRoot 'docs\TECHNICAL-NOTES.md') -Destination $packageDocs -Force
$manifest = Get-Content -LiteralPath (Join-Path $package 'q弹狒狒.json') -Raw | ConvertFrom-Json
$packageVersion = ([version]$manifest.AssemblyVersion).ToString(3)
$archive = Join-Path $dist "q弹狒狒-卫月版-$packageVersion.zip"
$archiveStage = Join-Path $projectRoot ('.tools\release-' + [Guid]::NewGuid().ToString('N') + '\q弹狒狒')
New-Item -ItemType Directory -Path $archiveStage -Force | Out-Null
foreach ($relative in @($files + @('README.md','THIRD-PARTY-NOTICES.md','docs\VALIDATION.md','docs\TECHNICAL-NOTES.md'))) {
    $target = Join-Path $archiveStage $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $package $relative) -Destination $target -Force
}
Compress-Archive -Path (Join-Path $archiveStage '*') -DestinationPath $archive -Force
$hashes = foreach ($file in $files) {
    $hash = Get-FileHash -LiteralPath (Join-Path $package $file) -Algorithm SHA256
    [ordered]@{ File = $file; SHA256 = $hash.Hash }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reports 'sha256.json') -Encoding utf8
Write-Host "插件：$package"
Write-Host "安装包：$archive"
