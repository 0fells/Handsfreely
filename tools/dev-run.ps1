param([string]$Platform = "x64", [string]$Dotnet = "dotnet")

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\windows\Handsfreely.App"
Get-Process Handsfreely -ErrorAction SilentlyContinue | Stop-Process -Force
& $Dotnet build $project -p:Platform=$Platform -v q -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$rid = if ($Platform -eq "ARM64") { "win-arm64" } else { "win-x64" }
$manifest = Get-ChildItem (Join-Path $project "bin\$Platform\Debug") -Recurse -Filter AppxManifest.xml | Where-Object { $_.DirectoryName -like "*$rid" } | Select-Object -First 1
Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown
$package = Get-AppxPackage -Name Handsfreely
Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
Write-Output "log: $env:LOCALAPPDATA\Packages\$($package.PackageFamilyName)\LocalState\handsfreely.log"
