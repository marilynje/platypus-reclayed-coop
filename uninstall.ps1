param(
    [string]$Game = '',
    [switch]$RemoveBepInEx
)
# Removes the co-op plugin and its "Online Co-op" add-on entry. With
# -RemoveBepInEx also removes BepInEx itself. Without -Game the game is found
# through Steam, like install.ps1 does.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'find-game.ps1')
if (-not $Game) { $Game = Find-Game }
if (-not $Game -or -not (Test-Path (Join-Path $Game 'Platypus_Data'))) {
    throw "Platypus Reclayed not found. Run: .\uninstall.ps1 -Game 'path to the game folder'."
}
if (Get-Process -Name 'Platypus' -ErrorAction SilentlyContinue) { throw 'Close the game first. / Сначала закройте игру.' }
Write-Host "Game: $Game"

$dest = Join-Path $Game 'BepInEx\plugins\PlatypusReclayedCoop'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force; Write-Host 'Plugin removed.' }
$cfg = Join-Path $Game 'BepInEx\config\marilynje.platypus.reclayedcoop.cfg'
if (Test-Path $cfg) { Remove-Item $cfg -Force }
$addon = Join-Path $Game 'Platypus_Data\StreamingAssets\Online Co-op'
if (Test-Path $addon) { Remove-Item $addon -Recurse -Force; Write-Host 'Add-on entry "Online Co-op" removed.' }
if ($RemoveBepInEx) {
    foreach ($item in @('BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt')) {
        $p = Join-Path $Game $item
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Write-Host 'BepInEx removed. The game is back to its original state.'
}
