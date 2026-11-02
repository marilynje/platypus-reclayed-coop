@echo off
rem Double-click: installs BepInEx and the Online Co-op mod into Platypus Reclayed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause