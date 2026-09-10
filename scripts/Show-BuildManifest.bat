@echo off
REM 한글(UTF-8) 콘솔
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-BroadcastApps.ps1" -List
echo.
pause
