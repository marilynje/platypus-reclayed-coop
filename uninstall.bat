@echo off
rem Double-click: removes the Online Co-op mod (BepInEx stays). Full removal: uninstall.bat -RemoveBepInEx
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
echo.
pause