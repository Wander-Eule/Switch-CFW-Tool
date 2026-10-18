# Builds the release package:
#
#   release\Switch-CFW-Tool-v<Version>.zip
#     Switch CFW Tool\
#       Switch-CFW-Tool.exe
#       LICENSE
#
# The version is read from <Version> in Switch-CFW-Tool.csproj, so it only has to be changed
# in one place. Run it via build-release.cmd (double-click), or from PowerShell with
#   powershell -ExecutionPolicy Bypass -File .\build-release.ps1

$ErrorActionPreference = "Stop"

$root       = $PSScriptRoot
$project    = Join-Path $root "Switch-CFW-Tool.csproj"
$publishDir = Join-Path $root "bin\Release\net8.0-windows\win-x64\publish"
$releaseDir = Join-Path $root "release"
$folderName = "Switch CFW Tool"
$stageDir   = Join-Path $releaseDir $folderName

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in Switch-CFW-Tool.csproj." }

$zipPath = Join-Path $releaseDir "Switch-CFW-Tool-v$version.zip"

Write-Host "Building Switch-CFW-Tool $version ..." -ForegroundColor Cyan

# A clean publish folder, so nothing left over from an earlier build ends up in the zip.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir | Out-Null

# Everything the publish step produced goes in, not just the exe, so the package stays
# complete if the publish settings ever change to output more than a single file.
Copy-Item (Join-Path $publishDir "*") $stageDir -Recurse

# The program is GPL-3.0, which asks for the licence text to travel with the binary.
Copy-Item (Join-Path $root "LICENSE") $stageDir

# Pointing at the folder itself (not its contents) makes it the top level inside the zip.
Compress-Archive -Path $stageDir -DestinationPath $zipPath

Write-Host ""
Write-Host "Done: $zipPath" -ForegroundColor Green
Get-ChildItem $stageDir | ForEach-Object { Write-Host "  $folderName\$($_.Name)" }
