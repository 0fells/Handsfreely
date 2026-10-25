param(
    [string]$Dotnet = "dotnet",
    [string]$JavaHome = $env:JAVA_HOME,
    [string]$SigningDir = (Join-Path $env:USERPROFILE ".signing"),
    [string[]]$GradleArgs = @()
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = Join-Path $root "dist"
$work = Join-Path $root "windows\Handsfreely.App\bin\release-msix"
$project = Join-Path $root "windows\Handsfreely.App"
$version = ([xml](Get-Content (Join-Path $project "Handsfreely.App.csproj"))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

Remove-Item $dist, $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dist | Out-Null

& $Dotnet publish $project -c Release -p:Platform=x64 -r win-x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never "-p:AppxPackageDir=$work/" -nologo -v q -p:AppxSymbolPackageEnabled=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$msix = Join-Path $dist "Handsfreely-x64.msix"
Copy-Item (Get-ChildItem $work -Recurse -Filter *.msix | Select-Object -First 1).FullName $msix

$signtool = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools") -Recurse -Filter signtool.exe | Where-Object { $_.DirectoryName -like "*\x64" } | Sort-Object FullName | Select-Object -Last 1
$password = Get-Content (Join-Path $SigningDir "handsfreely-pfx.txt") -Raw
& $signtool.FullName sign /fd SHA256 /f (Join-Path $SigningDir "handsfreely.pfx") /p $password $msix
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item (Join-Path $SigningDir "Handsfreely.cer") (Join-Path $dist "Handsfreely.cer")

$env:JAVA_HOME = $JavaHome
Push-Location (Join-Path $root "android")
try {
    & .\gradlew.bat -q @GradleArgs assembleRelease
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally { Pop-Location }
Copy-Item (Join-Path $root "android\app\build\outputs\apk\release\app-release.apk") (Join-Path $dist "Handsfreely.apk")

$sums = Get-ChildItem $dist -File | Sort-Object Name | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
[IO.File]::WriteAllText((Join-Path $dist "SHA256SUMS"), (($sums -join "`n") + "`n"))
Write-Output "Handsfreely $version"
Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
