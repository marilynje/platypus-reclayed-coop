# Finds Platypus Reclayed through Steam: every Steam library folder, the full
# game first, the demo if that is all there is. Used by install.ps1 / uninstall.ps1.
function Find-Game {
    $roots = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in @('SteamPath', 'InstallPath')) {
                if ($item.$name) { $roots += ($item.$name -replace '/', '\') }
            }
        } catch { }
    }
    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += ($m.Groups[1].Value -replace '\\\\', '\')
            }
        }
    }
    # The full game first, the demo if that is all there is.
    foreach ($folder in @('Platypus Reclayed', 'Platypus Reclayed Demo')) {
        foreach ($lib in ($libraries | Select-Object -Unique)) {
            $candidate = Join-Path $lib "steamapps\common\$folder"
            if (Test-Path (Join-Path $candidate 'Platypus_Data\Managed\Platypus.dll')) { return $candidate }
        }
    }
    return $null
}
