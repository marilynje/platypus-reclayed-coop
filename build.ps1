param(
    [string]$Game = 'D:\Steam\steamapps\common\Platypus Reclayed Demo',
    [string]$BepInExCore = ''
)
# Builds build\PlatypusReclayedCoop.dll against your own copy of the game.
# Uses the first C# compiler it finds: Visual Studio (Roslyn), the .NET SDK,
# or the csc.exe that ships with Windows (.NET Framework 4.x). The source is
# plain C# 5, so any of them works.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$managed = Join-Path $Game 'Platypus_Data\Managed'
if (-not (Test-Path (Join-Path $managed 'Platypus.dll'))) {
    throw "Platypus.dll not found in '$managed'. Pass -Game 'path to the Platypus Reclayed folder'."
}

if (-not $BepInExCore) {
    $candidates = @((Join-Path $Game 'BepInEx\core'), (Join-Path (Split-Path $Game -Parent) 'BepInEx\core'))
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'BepInEx.dll')) { $BepInExCore = $c; break }
    }
}
if (-not $BepInExCore -or -not (Test-Path (Join-Path $BepInExCore 'BepInEx.dll'))) {
    throw 'BepInEx not found. Run .\install.ps1 once (it installs BepInEx 5 into the game), or pass -BepInExCore <folder with BepInEx.dll>.'
}

function Find-Compiler {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        foreach ($vs in (& $vswhere -all -products '*' -property installationPath)) {
            $csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
            if (Test-Path $csc) { return @{ Exe = $csc; Pre = @(); Roslyn = $true } }
        }
    }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        foreach ($line in (& dotnet --list-sdks 2>$null)) {
            if ($line -match '^(\S+)\s+\[(.+)\]$') {
                $dll = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
                if (Test-Path $dll) { return @{ Exe = $dotnet.Source; Pre = @('exec', $dll); Roslyn = $true } }
            }
        }
    }
    $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (Test-Path $fw) { return @{ Exe = $fw; Pre = @(); Roslyn = $false } }
    throw 'No C# compiler found. Install Visual Studio (any workload) or the .NET SDK.'
}

$compiler = Find-Compiler
Write-Host "Compiler: $($compiler.Exe) $($compiler.Pre -join ' ')"
Write-Host "Game:     $managed"
Write-Host "BepInEx:  $BepInExCore"

$out = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out | Out-Null
$dll = Join-Path $out 'PlatypusReclayedCoop.dll'

$lines = @('/nologo', '/target:library', '/optimize+', '/unsafe+', '/nostdlib+', '/noconfig', '/nowarn:1701,1702', "/out:`"$dll`"")
if ($compiler.Roslyn) { $lines += '/deterministic' }
foreach ($ref in (Get-ChildItem -Path $managed -Filter '*.dll')) { $lines += "/reference:`"$($ref.FullName)`"" }
$lines += "/reference:`"$(Join-Path $BepInExCore 'BepInEx.dll')`""
$lines += "/reference:`"$(Join-Path $BepInExCore '0Harmony.dll')`""
foreach ($src in (Get-ChildItem -Path (Join-Path $root 'src') -Filter '*.cs')) { $lines += "`"$($src.FullName)`"" }

$rsp = Join-Path $out 'csc.rsp'
[System.IO.File]::WriteAllLines($rsp, $lines)
& $compiler.Exe @($compiler.Pre) "@$rsp"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed - send the errors above.' }
Write-Host "Built $dll"
