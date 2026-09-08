@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo.
echo 방송실 프로그램 일괄 빌드 (Builded)
echo CtrlOne / FileChecker / ScheduleDataManager / ScheduleReader / WorkLog
echo 완료 후: Builded\BroadcastingApp_버전_날짜.zip 통합 패키지 생성
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-BroadcastApps.ps1" %*
set ERR=%ERRORLEVEL%

echo.
if %ERR% neq 0 (
  echo 빌드 중 오류가 있었습니다. 위 로그를 확인하세요.
) else (
  echo 완료. 산출물: %~dp0Builded\
  echo 요약: %~dp0Builded\BUILD-INFO.md
  echo 통합 zip: %~dp0Builded\BroadcastingApp_*.zip
  if exist "%~dp0Builded\LATEST.txt" type "%~dp0Builded\LATEST.txt"
)
echo.
pause
exit /b %ERR%
