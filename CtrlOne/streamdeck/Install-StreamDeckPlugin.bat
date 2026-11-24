@echo off
setlocal
REM CtrlOne Stream Deck 플러그인 설치 (배포 폴더에서 실행)
set "SRC=%~dp0com.ctrlone.actions.sdPlugin"
set "DST=%APPDATA%\Elgato\StreamDeck\Plugins\com.ctrlone.actions.sdPlugin"

if not exist "%SRC%\manifest.json" (
  echo [오류] 플러그인 폴더가 없습니다: %SRC%
  pause
  exit /b 1
)

echo Stream Deck 앱이 실행 중이면 종료한 뒤 Enter...
pause >nul

if exist "%DST%" rd /s /q "%DST%"
mkdir "%APPDATA%\Elgato\StreamDeck\Plugins" 2>nul
xcopy /E /I /Y "%SRC%" "%DST%" >nul
if errorlevel 1 (
  echo [오류] 복사 실패
  pause
  exit /b 1
)

echo.
echo 설치 완료: %DST%
echo Stream Deck 앱을 다시 실행한 뒤 CtrlOne 카테고리에서 원터치 액션을 배치하세요.
pause
