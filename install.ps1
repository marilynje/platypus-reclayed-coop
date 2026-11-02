param(
    [string]$Game = '',
    [string]$Plugin = ''
)
# Installs BepInEx 5 (if missing) and the co-op plugin into Platypus Reclayed.
# Game files are not modified: BepInEx loads through winhttp.dll next to the EXE.
# Without -Game the game is found through Steam (every Steam library folder).
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'find-game.ps1')

if (-not $Game) { $Game = Find-Game }
if (-not $Game -or -not (Test-Path (Join-Path $Game 'Platypus_Data\Managed\Platypus.dll'))) {
    throw "Platypus Reclayed not found. Run: .\install.ps1 -Game 'path to the game folder' (Steam: Properties > Installed Files > Browse)."
}
if (Get-Process -Name 'Platypus' -ErrorAction SilentlyContinue) { throw 'Close the game first. / Сначала закройте игру.' }
Write-Host "Game: $Game"

if (-not $Plugin) {
    foreach ($c in @((Join-Path $PSScriptRoot 'build\PlatypusReclayedCoop.dll'), (Join-Path $PSScriptRoot 'PlatypusReclayedCoop.dll'))) {
        if (Test-Path $c) { $Plugin = $c; break }
    }
}

# 1. BepInEx 5.4.23.3 x64
$hasLoader = Test-Path (Join-Path $Game 'winhttp.dll')
$hasCore = Test-Path (Join-Path $Game 'BepInEx\core\BepInEx.dll')
if (-not ($hasLoader -and $hasCore)) {
    Write-Host 'Downloading BepInEx 5.4.23.3 (x64) from GitHub...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'platypus-reclayed-coop' }
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/BepInEx/BepInEx/releases/tags/v5.4.23.3' -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -match 'x64' -and $_.name -match '\.zip$' -and $_.name -notmatch 'linux|macos|unix' } | Select-Object -First 1
    if (-not $asset) { throw 'BepInEx x64 zip not found. Install BepInEx 5 (x64) by hand from github.com/BepInEx/BepInEx/releases and run this again.' }
    $zip = Join-Path $env:TEMP $asset.name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers $headers
    Expand-Archive -Path $zip -DestinationPath $Game -Force
    Remove-Item $zip -ErrorAction SilentlyContinue
    Write-Host "BepInEx installed ($($asset.name))."
} else {
    Write-Host 'BepInEx already installed.'
}

# 2. The older host-authoritative attempt patches the same methods: disable it.
$old = Join-Path $Game 'BepInEx\plugins\PlatypusOnlineCoop'
if (Test-Path $old) {
    $target = "$old.disabled"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Rename-Item -Path $old -NewName (Split-Path $target -Leaf)
    Write-Host 'Disabled the older PlatypusOnlineCoop plugin (renamed to PlatypusOnlineCoop.disabled).'
}

# 3. Plugin
if (-not $Plugin -or -not (Test-Path $Plugin)) {
    Write-Host 'BepInEx is ready. Now run .\build.ps1 and then .\install.ps1 again to copy the plugin.'
    return
}
$dest = Join-Path $Game 'BepInEx\plugins\PlatypusReclayedCoop'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -Path $Plugin -Destination $dest -Force
Write-Host "Installed $(Split-Path $Plugin -Leaf) -> $dest"
Write-Host ''
Write-Host 'Done. Start the game from Steam, then: Mods > Add-ons > tick "Online Co-op".'
Write-Host 'Готово. Запустите игру из Steam: Моды > Дополнения > включите "Online Co-op".'
