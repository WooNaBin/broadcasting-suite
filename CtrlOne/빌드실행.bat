@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo [CtrlOne] 단일 실행 파일 빌드 중...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 (
  echo 빌드 실패.
  pause
  exit /b 1
)

echo.
echo 완료: %~dp0publish\CtrlOne.exe
echo 실행합니다...
start "" "%~dp0publish\CtrlOne.exe"
