Platypus Reclayed — Online Co-op (unofficial mod)
==================================================

Two-player online co-op through Steam: invite a friend and fly together.
Both players need the same game version (Steam, Windows) and this mod.

INSTALL
1. Close the game.
2. Unpack this archive anywhere.
3. Double-click install.bat.
   It finds the game through Steam, downloads BepInEx 5 (the mod loader) from
   GitHub and copies the mod. Game files are not changed.
   If the game is not found, in PowerShell from this folder:
     .\install.ps1 -Game "path to the Platypus Reclayed folder"
   (Steam > Properties > Installed Files > Browse).
4. Start the game FROM STEAM (otherwise the Steam invite window cannot open).
5. Once: Mods > Add-ons > tick "Online Co-op".

HOW TO PLAY
Host:
  Main menu > "Online Co-op" > difficulty > world > ship select.
  Bottom right: "Invite a Friend" opens the Steam invite window; pick a friend.
Friend:
  Accepts the invite in Steam; the game opens the ship screen by itself.
Both:
  Each picks a ship (host on the left, friend on the right) and presses
  "Ready". Changed your mind: "Cancel". The level starts when both are ready.
  "Leave" ends the co-op game for both.
  Pause (Esc) is shared. If the connection drops the game waits up to 60 s.

TROUBLESHOOTING
- "Invite a Friend" opens nothing: turn on the Steam overlay
  (Steam > Settings > In Game) and start the game from Steam.
- No "Online Co-op" in the main menu: tick it in Mods > Add-ons.
- "Different game builds": your game versions differ; update both.
- "Mod version mismatch": install the same mod version.
- Log: BepInEx\LogOutput.log in the game folder.

UNINSTALL
uninstall.bat removes the mod. To put the game back exactly as it was:
  .\uninstall.ps1 -RemoveBepInEx

Unofficial fan mod, not affiliated with Claymatic Games.
