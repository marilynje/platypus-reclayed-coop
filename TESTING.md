# Testing

## 0.2.2 — 2026-11-03, single-player menu fix

When the player returns from online co-op and presses the ordinary Start
button, the mod now clears online intent, closes the previous session/lobby
and cancels pending Steam lobby operations before invoking the game's
original Start action. The Online Co-op button invokes that original action
separately, preserving its online intent.

| Check | Result |
| --- | --- |
| Build against full game 1.4.0 (`1.4.0-26-g2926f6f81`) | Passed; existing compiler warnings only |
| In-game plugin initialization | 0.2.2 loaded; all 76 patches applied |
| Co-op → Back → ordinary Start → solo level | Not completed: user stopped computer control with Esc |
| Two-account Steam session with 0.2.2 | Not tested |

## Workshop-only feasibility — 2026-11-03

Inspected repository commit `184fca0ff04ab2cb045f4e80d1d4358537ece9a2`
and the locally installed game. No game installation files were changed.

| Assembly | SHA-256 |
| --- | --- |
| `Platypus.dll` | `85038a105a0ef30a9bbe7006d3d29ce71613eeb60acd07d16373b70147b69b71` |
| `MoonSharp.dll` | `9c6ace11b386c5a06be427de0b5986e28a03c4cb2e504e50dcbe6e183e8377e2` |

### Static inspection

- `ModManager.LoadAvailableMods` discovers local and Workshop content using
  `modmeta.json`; `SetModAsync` links enabled add-ons, loads bundles and
  localisation, and initializes `LuaBridge`.
- `AssetLoader.TryLoadLuaBehaviour` reads `Behaviours/<name>.lua`;
  `LuaBehaviour.OnInitialise` executes `globals.lua` and the entity script.
- `LuaBridge.Initialize` registers game proxies; `AssignGlobalFunctions`
  exposes entity, player, audio and utility functions. These APIs do not
  expose the network transport, menu hooks or player input/tick interception
  needed by the current co-op implementation.
- No managed assembly-loading API references (`Assembly.Load`, `LoadFrom`,
  or `LoadFile`) were found in `Platypus.dll`, `Platypus.Core.dll` or
  `Assembly-CSharp.dll`. The inspected add-on loading paths do not initialize
  external C# plugins. Asset bundle loading is not a managed plugin loader.
- MoonSharp's `PlatformAutoDetector.GetDefaultPlatform` selects
  `LimitedPlatformAccessor` when Unity types are present. Its module filter
  removes `CoreModules.OS_System` and `CoreModules.IO`.

### Isolated interpreter check

A temporary .NET 8 console probe loaded the installed `MoonSharp.dll` and
`UnityEngine.CoreModule.dll`, then constructed `new Script()` as the game
does. The selected platform was `LimitedPlatformAccessor`.

| Lua expression | Result |
| --- | --- |
| `type(io)` | `nil` |
| `type(os.execute)` | `nil` |
| `type(os.exit)` | `nil` |
| `type(package.loadlib)` | `nil` |
| `type(luanet)` / `type(clr)` | `nil` / `nil` |
| `type(socket)` / `type(SteamNetworking)` | `nil` / `nil` |
| `type(math.random)` | `function` |

This is an isolated interpreter check, not an in-game co-op test. Unity's
resource-backed script loader logged an initialization error outside the
Unity player; the inline expressions above executed successfully. The game
bridge's additional globals were checked statically, not executed by the
probe. Decompiled game code and copied dependencies remain only in ignored
local build output and are not included in source or release archives.

**Result:** a subscription alone cannot initialize this co-op through the
inspected game's supported mod APIs. A game update providing a native code
mod entry point (or equivalent additional APIs) is required. An independently
installed loader remains a separate prerequisite; it is not a Workshop-only
solution. No Workshop-only gameplay or two-account Steam test is claimed.

## 0.2.0 — 2026-11-03, full game 1.4.0 (`1.4.0-26-g2926f6f81`)

| Check | Result |
| --- | --- |
| `tools/verify.py` against `Platypus.dll` 1.4.0 | all hooks found (the 1.4 input pool replaces `horizontalInput` / `verticalInput`; `verify.py` still lists those two) |
| Two copies on one PC (`--coop-host` / `--coop-join 127.0.0.1`) | connect, handshake, friend lands on the ship screen |
| Ship screen | invite button in the right panel; only the local panel has buttons; Ready / Cancel; Leave |
| Automated match on `level1_data` (`--coop-autotest`), 90 s of combat | identical state hashes on both games at every check (ticks 0–5400) |
| Lightning entity desync (1.4) | fixed: was `Desync at tick 1 (entities)`, now none |
| Localisation | all mod texts in the game's 16 languages, no missing keys |
| Steam overlay | loaded when the game is started from Steam |

### Not yet tested

- Steam invite accepted by a **second Steam account** (two copies on one PC
  share one account and cannot invite each other).
- The lightning weapon picked up in a match (the autotest does not collect it).
- The demo build with the 0.2.0 menu flow.

## Two copies on one PC

Copy the whole game folder (e.g. to `D:\PRFull2`), install the mod there too,
put `steam_appid.txt` with `2912430` next to both `Platypus.exe`, then:

```text
"<game>\Platypus.exe" --coop-host 27833 --coop-shiptest
"D:\PRFull2\Platypus.exe" --coop-join 127.0.0.1:27833
```

`--coop-shiptest` takes the host straight to the ship screen on the first
world; add `--coop-autotest` to both to let them play by themselves. Compare
the `SYNC tick=...` lines of both `BepInEx\LogOutput.log` files; a desync
writes `BepInEx\desync-tick*.txt` on both sides.
