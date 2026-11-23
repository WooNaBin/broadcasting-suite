@echo off
chcp 65001 >nul
cd /d "%~dp0"

if not exist "%~dp0publish\CtrlOne.exe" (
  echo CtrlOne.exe 가 없습니다. 먼저 빌드합니다...
  call "%~dp0빌드실행.bat"
  exit /b %errorlevel%
)

start "" "%~dp0publish\CtrlOne.exe"
