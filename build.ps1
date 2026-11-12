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
$reports = Join-Path $dist 'reports'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
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
$sdkPackage = Join-Path $output 'q弹狒狒'
& (Join-Path $projectRoot 'scripts\Test-ReleasePackage.ps1') -PackageDirectory $sdkPackage |
    Tee-Object -FilePath (Join-Path $reports 'package.log')
$manifest = Get-Content -LiteralPath (Join-Path $sdkPackage 'q弹狒狒.json') -Raw | ConvertFrom-Json
$packageVersion = ([version]$manifest.AssemblyVersion).ToString(3)
$package = Join-Path $dist "q弹狒狒-卫月版-$packageVersion"
New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach ($file in @('latest.zip','q弹狒狒.json')) {
    Copy-Item -LiteralPath (Join-Path $sdkPackage $file) -Destination (Join-Path $package $file) -Force
    if ((Get-FileHash -LiteralPath (Join-Path $sdkPackage $file)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $package $file)).Hash) {
        throw "官方产物复制后校验失败：$file"
    }
}
$download = "$($manifest.RepoUrl)/releases/download/v$packageVersion/latest.zip"
foreach ($field in @('DownloadLinkInstall','DownloadLinkUpdate','DownloadLinkTesting')) {
    $manifest | Add-Member -NotePropertyName $field -NotePropertyValue $download -Force
}
$manifest | Add-Member -NotePropertyName LastUpdate -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force
ConvertTo-Json -InputObject @($manifest) -Depth 10 | Set-Content -LiteralPath (Join-Path $projectRoot 'repo.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $projectRoot 'repo.json') -Destination $package -Force
$hashes = foreach ($file in @('latest.zip','q弹狒狒.json','repo.json')) {
    $hash = Get-FileHash -LiteralPath (Join-Path $package $file) -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + $file
}
$hashes | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding utf8
Write-Host "官方发布目录：$package"
Write-Host "SDK 原生生成：latest.zip + q弹狒狒.json"
