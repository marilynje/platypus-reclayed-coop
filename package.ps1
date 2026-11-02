# Builds the release zip players download: run .\build.ps1 first.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dll = Join-Path $root 'build\PlatypusReclayedCoop.dll'
if (-not (Test-Path $dll)) { throw 'Run .\build.ps1 first.' }
$version = (Select-String -Path (Join-Path $root 'src\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
$stage = Join-Path $root 'build\package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($f in @('install.bat', 'install.ps1', 'uninstall.bat', 'uninstall.ps1', 'find-game.ps1')) { Copy-Item (Join-Path $root $f) $stage }
Copy-Item $dll $stage
Copy-Item (Join-Path $root 'distribution\README-RU.txt') $stage
Copy-Item (Join-Path $root 'distribution\README-EN.txt') $stage
New-Item -ItemType Directory -Force (Join-Path $root 'releases') | Out-Null
$zip = Join-Path $root "releases\PlatypusReclayedCoop-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content (Join-Path $root 'releases\SHA256SUMS.txt')
Write-Host "Packaged $zip"