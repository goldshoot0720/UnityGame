@echo off
rem MoeGames web version: double-click to start a local web server and open the game.
rem Uses the PowerShell built into Windows; nothing to install. Close this window to stop.
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server.ps1" %*
if errorlevel 1 pause
