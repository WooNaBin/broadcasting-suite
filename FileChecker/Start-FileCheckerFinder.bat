@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo FileCheckerFinder 실행 중...
echo 브라우저가 자동으로 열립니다. (http://localhost:5187)
echo 이 창을 닫으면 프로그램이 종료될 수 있습니다.
start "FileCheckerFinder" /wait "%~dp0FileCheckerFinder.exe"
endlocal
