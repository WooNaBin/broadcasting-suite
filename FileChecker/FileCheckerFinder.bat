@echo off
setlocal
cd /d "%~dp0"
dotnet build --nologo
if errorlevel 1 (
	echo.
	echo 빌드에 실패했습니다. 위 오류를 확인하세요.
	pause
	exit /b 1
)
start "FileCheckerFinder Server" cmd /k "dotnet run --no-build --urls http://localhost:5187"
timeout /t 2 /nobreak >nul
start "" http://localhost:5187
endlocal