param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"
$tag = "v$Version"
$zipName = "PanelTray-$tag-$Runtime.zip"

Write-Host "Publishing PanelTray $tag..."
dotnet publish (Join-Path $root "src\PanelTray\PanelTray.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "PanelTray.exe") -DestinationPath $zipPath -Force

Write-Host "Created $zipPath"
