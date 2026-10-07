<#
  Builds a self-contained AlightenInk release and packs it with Velopack.
  Output: .\Releases\AlightenInk-win-Setup.exe (+ the .nupkg / RELEASES files that updates need).

  Usage:  .\build-release.ps1 -Version 1.0.1
  Needs:  .NET 8 SDK and the vpk tool (dotnet tool install -g vpk).
#>
param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$project = Join-Path $root "src\AlightenInk\AlightenInk.csproj"
$publish = Join-Path $root "publish"
$assets  = Join-Path $root "src\AlightenInk\Assets"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publishing AlightenInk $Version (win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained -o $publish -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing with Velopack..." -ForegroundColor Cyan
vpk pack `
    --packId AlightenInk `
    --packVersion $Version `
    --packDir $publish `
    --mainExe AlightenInk.exe `
    --packTitle "AlightenInk" `
    --packAuthors "Alighten" `
    --icon (Join-Path $assets "AlightenInk.ico") `
    --splashImage (Join-Path $assets "AlightenInk.png") `
    --outputDir (Join-Path $root "Releases")
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "Done: $(Join-Path $root 'Releases\AlightenInk-win-Setup.exe')" -ForegroundColor Green
