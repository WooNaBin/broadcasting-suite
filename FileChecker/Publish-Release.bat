@echo off
setlocal
cd /d "%~dp0"

set "OUT_DIR=D:\Projects\Builded\FileChecker"

if not exist "D:\Projects\Builded" mkdir "D:\Projects\Builded"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo Publishing FileChecker to %OUT_DIR% ...
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%OUT_DIR%"

if errorlevel 1 (
	echo.
	echo Publish failed. See errors above.
	pause
	exit /b 1
)

echo.
echo Done: %OUT_DIR%
endlocal
