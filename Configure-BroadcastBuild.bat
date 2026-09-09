@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-BroadcastApps.ps1" -Configure
set ERR=%ERRORLEVEL%
echo.
pause
exit /b %ERR%
