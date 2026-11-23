@echo off
setlocal
cd /d "%~dp0"
call "%~dp0package-portable.bat"
exit /b %errorlevel%
