@echo off
REM 한글(UTF-8) 콘솔 — dotnet 복원 메시지 깨짐 완화
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
setlocal
cd /d "%~dp0"

if /i "%~1"=="config" goto CONFIG
if /i "%~1"=="menu" goto MENU

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-BroadcastApps.ps1" %*
set ERR=%ERRORLEVEL%
echo.
pause
exit /b %ERR%

:CONFIG
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-BroadcastApps.ps1" -Configure
set ERR=%ERRORLEVEL%
echo.
pause
exit /b %ERR%

:MENU
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-BroadcastApps.ps1" -Menu
set ERR=%ERRORLEVEL%
echo.
pause
exit /b %ERR%
