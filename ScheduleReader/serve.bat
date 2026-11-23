@echo off
chcp 65001 >nul
cd /d "%~dp0"

set "EXE="
if exist "%~dp0ScheduleReader.exe" set "EXE=%~dp0ScheduleReader.exe"
if not defined EXE if exist "%~dp0bin\Release\net10.0\win-x64\ScheduleReader.exe" set "EXE=%~dp0bin\Release\net10.0\win-x64\ScheduleReader.exe"
if not defined EXE if exist "%~dp0bin\Debug\net10.0\ScheduleReader.exe" set "EXE=%~dp0bin\Debug\net10.0\ScheduleReader.exe"

if not defined EXE (
  echo ScheduleReader.exe 가 없습니다.
  echo 개발 PC:  dotnet run --project ScheduleReader.csproj
  echo 배포본:   Build-BroadcastApps 로 빌드한 폴더의 ScheduleReader.exe 실행
  pause
  exit /b 1
)

echo ScheduleReader 시작...
echo 브라우저: http://127.0.0.1:17823
start "" "%EXE%"
