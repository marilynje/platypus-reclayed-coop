# Testing

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
