#!/usr/bin/env bash
# macOS/Linux host에서 스위트 Windows 배포본을 빌드 (Build-BroadcastApps.ps1 대응)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${BROADCAST_BUILD_DIR:-$ROOT/Builded}"
STAMP="$(date +%Y%m%d)"
BUMP="${1:-Build}" # Build | Minor | Major | none

VERSION_FILE="$ROOT/broadcast-suite.version.json"
STAMP="$STAMP" VERSION_FILE="$VERSION_FILE" BUMP="$BUMP" python3 - <<'PY'
import json, datetime, os
from pathlib import Path
p = Path(os.environ["VERSION_FILE"])
info = json.loads(p.read_text())
bump = os.environ["BUMP"]
stamp = os.environ["STAMP"]
ver = info.get("version", "1.0.0")
parts = [int(x) for x in ver.split(".")[:3]]
while len(parts) < 3:
    parts.append(0)
build = int(info.get("build", 0))
if bump == "Major":
    parts = [parts[0] + 1, 0, 0]; build = 1
elif bump == "Minor":
    parts = [parts[0], parts[1] + 1, 0]; build = 1
elif bump == "Build":
    build += 1
elif bump == "none":
    pass
else:
    raise SystemExit(f"unknown bump: {bump}")
info["version"] = f"{parts[0]}.{parts[1]}.{parts[2]}"
info["build"] = build
label = f"{info['version']}.{build}_{stamp}"
info["lastBuild"] = datetime.datetime.now().astimezone().isoformat()
info["lastLabel"] = label
p.write_text(json.dumps(info, indent=4, ensure_ascii=False) + "\n")
print(label)
PY

LABEL="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['lastLabel'])")"
echo "스위트 라벨: $LABEL"
echo "출력: $OUT"
mkdir -p "$OUT"

publish_win() {
  local proj="$1" outdir="$2"
  shift 2
  mkdir -p "$outdir"
  dotnet publish "$proj" -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
    -p:EnableWindowsTargeting=true \
    "$@" -o "$outdir"
}

zip_dir() {
  local src="$1" zip="$2"
  rm -f "$zip"
  (cd "$(dirname "$src")" && zip -qr "$zip" "$(basename "$src")")
}

# --- CtrlOne ---
echo "== CtrlOne =="
CO_DEST="$OUT/CtrlOne"
CO_STAGE="$CO_DEST/_stage"
rm -rf "$CO_STAGE"
publish_win "$ROOT/CtrlOne/CtrlOne.csproj" "$CO_STAGE"
mkdir -p "$CO_DEST/CtrlOne-Windows-x64"
cp "$CO_STAGE/CtrlOne.exe" "$CO_DEST/CtrlOne.exe"
cp "$CO_STAGE/CtrlOne.exe" "$CO_DEST/CtrlOne-Windows-x64/CtrlOne.exe"
if [[ -d "$CO_STAGE/demo-preview" ]]; then
  rm -rf "$CO_DEST/demo-preview" "$CO_DEST/CtrlOne-Windows-x64/demo-preview"
  cp -R "$CO_STAGE/demo-preview" "$CO_DEST/demo-preview"
  cp -R "$CO_STAGE/demo-preview" "$CO_DEST/CtrlOne-Windows-x64/demo-preview"
fi
rm -rf "$CO_STAGE"
zip_dir "$CO_DEST/CtrlOne-Windows-x64" "$CO_DEST/CtrlOne-Windows-x64-$STAMP.zip"

echo "== CtrlOne macOS =="
export CTRLONE_BUILD_DIR="$CO_DEST"
bash "$ROOT/CtrlOne/package-portable-macos.sh"

# --- FileChecker ---
echo "== FileChecker =="
FC_DEST="$OUT/FileChecker"
FC_STAGE="$FC_DEST/_stage"
rm -rf "$FC_STAGE"
publish_win "$ROOT/FileChecker/FileCheckerFinder.csproj" "$FC_STAGE" \
  -p:IncludeNativeLibrariesForSelfExtract=true
mkdir -p "$FC_DEST/FileChecker-Windows-x64"
cp "$FC_STAGE/FileCheckerFinder.exe" "$FC_DEST/FileCheckerFinder.exe"
cp "$FC_STAGE/FileCheckerFinder.exe" "$FC_DEST/FileChecker-Windows-x64/FileCheckerFinder.exe"
BAT_SRC="$FC_STAGE/Start-FileCheckerFinder.bat"
[[ -f "$BAT_SRC" ]] || BAT_SRC="$ROOT/FileChecker/Start-FileCheckerFinder.bat"
if [[ -f "$BAT_SRC" ]]; then
  cp "$BAT_SRC" "$FC_DEST/Start-FileCheckerFinder.bat"
  cp "$BAT_SRC" "$FC_DEST/FileChecker-Windows-x64/Start-FileCheckerFinder.bat"
fi
rm -rf "$FC_STAGE"
zip_dir "$FC_DEST/FileChecker-Windows-x64" "$FC_DEST/FileChecker-Windows-x64-$STAMP.zip"

# FileChecker macOS (arm64 + x64)
echo "== FileChecker macOS =="
export FILECHECKER_BUILD_DIR="$FC_DEST"
bash "$ROOT/FileChecker/package-portable-macos.sh"

# --- ScheduleDataManager ---
echo "== ScheduleDataManager =="
export SCHEDULE_BUILD_DIR="$OUT/ScheduleDataManager"
SDM_DEST="$SCHEDULE_BUILD_DIR"
SDM_BUILD="$SDM_DEST/_build"
rm -rf "$SDM_BUILD"
publish_win "$ROOT/ScheduleDataManager/LocalBridge/LocalBridge.csproj" "$SDM_BUILD/win-x64"
mkdir -p "$SDM_DEST/BroadcastingSchedule-Windows-x64"
cp "$SDM_BUILD/win-x64/BroadcastingSchedule.exe" \
  "$SDM_DEST/BroadcastingSchedule-Windows-x64/BroadcastingSchedule.exe"
ICO="$SDM_BUILD/win-x64/BroadcastingSchedules.ico"
[[ -f "$ICO" ]] && cp "$ICO" "$SDM_DEST/BroadcastingSchedule-Windows-x64/"
rm -rf "$SDM_BUILD"
zip_dir "$SDM_DEST/BroadcastingSchedule-Windows-x64" \
  "$SDM_DEST/BroadcastingSchedule-Windows-x64-$STAMP.zip"

# --- WorkLog ---
echo "== WorkLog =="
export WORKLOG_BUILD_DIR="$OUT/WorkLog"
WL_DEST="$WORKLOG_BUILD_DIR"
WL_BUILD="$WL_DEST/_build"
rm -rf "$WL_BUILD"
publish_win "$ROOT/WorkLog/LocalBridge/LocalBridge.csproj" "$WL_BUILD/win-x64"
mkdir -p "$WL_DEST/WorkLog-Windows-x64"
cp "$WL_BUILD/win-x64/WorkLog.exe" "$WL_DEST/WorkLog-Windows-x64/WorkLog.exe"
rm -rf "$WL_BUILD"
zip_dir "$WL_DEST/WorkLog-Windows-x64" "$WL_DEST/WorkLog-Windows-x64-$STAMP.zip"

# --- ScheduleReader ---
echo "== ScheduleReader =="
SR_DEST="$OUT/ScheduleReader"
SR_PORTABLE="$SR_DEST/ScheduleReader-portable"
rm -rf "$SR_PORTABLE"
mkdir -p "$SR_PORTABLE"
cp -R "$ROOT/ScheduleReader/schedule_reader" "$SR_PORTABLE/"
cp -R "$ROOT/ScheduleReader/config" "$SR_PORTABLE/"
cp "$ROOT/ScheduleReader/requirements.txt" "$SR_PORTABLE/"
[[ -f "$ROOT/ScheduleReader/README.md" ]] && cp "$ROOT/ScheduleReader/README.md" "$SR_PORTABLE/"
[[ -d "$ROOT/ScheduleReader/models" ]] && cp -R "$ROOT/ScheduleReader/models" "$SR_PORTABLE/"
mkdir -p "$SR_PORTABLE/input" "$SR_PORTABLE/output"
export SR_PORTABLE
python3 - <<'PY'
from pathlib import Path
import os
portable = Path(os.environ["SR_PORTABLE"])
(portable / "serve.bat").write_text("""@echo off
cd /d "%~dp0"
if exist ".venv\\Scripts\\python.exe" (
  ".venv\\Scripts\\python.exe" -m schedule_reader
) else (
  echo Run Setup-And-Run.bat first.
  pause
)
""", encoding="utf-8")
(portable / "Setup-And-Run.bat").write_text("""@echo off
chcp 65001 >nul
cd /d "%~dp0"
set PYEXE=py -3
where py >nul 2>&1 || set PYEXE=python
echo Using: %PYEXE%
if not exist ".venv\\Scripts\\python.exe" (
  %PYEXE% -m venv .venv
  if errorlevel 1 ( echo Failed to create venv. & pause & exit /b 1 )
)
".venv\\Scripts\\python.exe" -m pip install --upgrade pip
".venv\\Scripts\\python.exe" -m pip install -r requirements.txt
if errorlevel 1 ( echo pip install failed. & pause & exit /b 1 )
call "%~dp0serve.bat"
""", encoding="utf-8")
(portable / "README-DEPLOY.txt").write_text(
"""ScheduleReader 배포 패키지
==========================

1. 이 폴더를 대상 PC에 복사
2. Python 3.11+ 설치 (python.org 권장)
3. Setup-And-Run.bat 실행
4. 이후: serve.bat
5. 브라우저: http://127.0.0.1:17823
""", encoding="utf-8")
print("ScheduleReader staged", portable)
PY
zip_dir "$SR_PORTABLE" "$SR_DEST/ScheduleReader-portable-$STAMP.zip"

# --- BroadcastNasBridge (통합 NAS) ---
echo "== BroadcastNasBridge =="
BNB_DEST="$OUT/BroadcastNasBridge"
BNB_STAGE="$BNB_DEST/_stage"
rm -rf "$BNB_STAGE"
bash "$ROOT/BroadcastNasBridge/scripts/sync-ui.sh"
publish_win "$ROOT/BroadcastNasBridge/BroadcastNasBridge.csproj" "$BNB_STAGE"
mkdir -p "$BNB_DEST/BroadcastNasBridge-Windows-x64"
cp -R "$BNB_STAGE"/* "$BNB_DEST/BroadcastNasBridge-Windows-x64/"
cp "$ROOT/BroadcastNasBridge/scripts/Launch-BroadcastNasBridge.bat" "$BNB_DEST/BroadcastNasBridge-Windows-x64/" 2>/dev/null || true
# Windows 런처: 게시된 exe 직접 실행용
cat > "$BNB_DEST/BroadcastNasBridge-Windows-x64/Start-BroadcastNasBridge.bat" <<'EOF'
@echo off
cd /d "%~dp0"
start "" BroadcastNasBridge.exe
EOF
rm -rf "$BNB_STAGE"
zip_dir "$BNB_DEST/BroadcastNasBridge-Windows-x64" "$BNB_DEST/BroadcastNasBridge-Windows-x64-$STAMP.zip"

echo "== BroadcastNasBridge macOS =="
export BROADCAST_NAS_BRIDGE_BUILD_DIR="$BNB_DEST"
bash "$ROOT/BroadcastNasBridge/package-portable-macos.sh"

# --- Suite bundle ---
echo "== Suite bundle =="
BUNDLE_ROOT="$OUT/_bundle_stage/BroadcastingApp_$LABEL"
rm -rf "$OUT/_bundle_stage"
mkdir -p "$BUNDLE_ROOT/Windows" "$BUNDLE_ROOT/Mac"
cp -R "$CO_DEST/CtrlOne-Windows-x64" "$BUNDLE_ROOT/Windows/"
cp -R "$FC_DEST/FileChecker-Windows-x64" "$BUNDLE_ROOT/Windows/"
cp -R "$SDM_DEST/BroadcastingSchedule-Windows-x64" "$BUNDLE_ROOT/Windows/"
cp -R "$WL_DEST/WorkLog-Windows-x64" "$BUNDLE_ROOT/Windows/"
cp -R "$SR_PORTABLE" "$BUNDLE_ROOT/Windows/"
cp -R "$BNB_DEST/BroadcastNasBridge-Windows-x64" "$BUNDLE_ROOT/Windows/"

# Mac payloads (있는 것만)
[[ -d "$CO_DEST/CtrlOne-macOS-arm64" ]] && cp -R "$CO_DEST/CtrlOne-macOS-arm64" "$BUNDLE_ROOT/Mac/"
[[ -d "$CO_DEST/CtrlOne-macOS-x64" ]] && cp -R "$CO_DEST/CtrlOne-macOS-x64" "$BUNDLE_ROOT/Mac/"
[[ -d "$OUT/WorkLog/WorkLog-macOS-arm64.app" ]] && cp -R "$OUT/WorkLog/WorkLog-macOS-arm64.app" "$BUNDLE_ROOT/Mac/"
[[ -d "$OUT/WorkLog/WorkLog-macOS-x64.app" ]] && cp -R "$OUT/WorkLog/WorkLog-macOS-x64.app" "$BUNDLE_ROOT/Mac/"
[[ -d "$OUT/ScheduleDataManager/BroadcastingSchedule-macOS-arm64" ]] && cp -R "$OUT/ScheduleDataManager/BroadcastingSchedule-macOS-arm64" "$BUNDLE_ROOT/Mac/"
[[ -d "$OUT/ScheduleDataManager/BroadcastingSchedule-macOS-x64" ]] && cp -R "$OUT/ScheduleDataManager/BroadcastingSchedule-macOS-x64" "$BUNDLE_ROOT/Mac/"
[[ -d "$FC_DEST/FileChecker-macOS-arm64" ]] && cp -R "$FC_DEST/FileChecker-macOS-arm64" "$BUNDLE_ROOT/Mac/"
[[ -d "$FC_DEST/FileChecker-macOS-x64" ]] && cp -R "$FC_DEST/FileChecker-macOS-x64" "$BUNDLE_ROOT/Mac/"
[[ -d "$BNB_DEST/BroadcastNasBridge-macOS-arm64" ]] && cp -R "$BNB_DEST/BroadcastNasBridge-macOS-arm64" "$BUNDLE_ROOT/Mac/"
[[ -d "$BNB_DEST/BroadcastNasBridge-macOS-x64" ]] && cp -R "$BNB_DEST/BroadcastNasBridge-macOS-x64" "$BUNDLE_ROOT/Mac/"

cp "$ROOT/scripts/Install-BroadcastApps.ps1" "$BUNDLE_ROOT/"
cp "$ROOT/scripts/Install-BroadcastApps.bat" "$BUNDLE_ROOT/"
cat > "$BUNDLE_ROOT/Mac/README.txt" <<EOF
macOS 배포본
============

포함
- BroadcastNasBridge-macOS-arm64 / …-x64 (17820) ← 권장 진입점
- CtrlOne-macOS-arm64 / …-x64 (5177)
- WorkLog-macOS-arm64.app / WorkLog-macOS-x64.app (17822, 레거시)
- BroadcastingSchedule-macOS-arm64 / …-x64 (17821, 레거시)
- FileChecker-macOS-arm64 / …-x64 (5187, 레거시)

미포함
- ScheduleReader (Windows만)

Gatekeeper: 우클릭 → 열기, 또는 xattr -dr com.apple.quarantine <앱>
EOF
cat > "$BUNDLE_ROOT/VERSION.txt" <<EOF
BroadcastingApp suite
version: $(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['version'])")
build: $(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['build'])")
label: $LABEL
stamp: $STAMP
Apps:
- CtrlOne (Windows + macOS)
- FileChecker (Windows + macOS)
- ScheduleDataManager (Windows + macOS)
- WorkLog (Windows + macOS)
- ScheduleReader
EOF
cat > "$BUNDLE_ROOT/README.txt" <<EOF
방송실 프로그램 통합 배포 패키지
================================

폴더 구성
---------
- Windows\\   … Windows x64 포터블 앱
- Mac\\       … macOS (CtrlOne, WorkLog, ScheduleDataManager, FileChecker)
- Install-BroadcastApps.bat / .ps1  … Windows 설치 도우미
- VERSION.txt

설치 (권장, Windows)
-----------
1. zip 압축 해제
2. Install-BroadcastApps.bat 실행
3. 설치 폴더 선택
4. Windows\\ 내용만 복사됨. 바탕화면 바로가기는 선택

Mac은 Mac\\ 폴더의 앱을 직접 실행하세요.

포트: Bridge 17820 / Schedule 17821 / WorkLog 17822 / ScheduleReader 17823 / CtrlOne 5177 / FileChecker 5187
EOF

ZIP_OUT="$OUT/BroadcastingApp_$LABEL.zip"
rm -f "$ZIP_OUT"
(cd "$OUT/_bundle_stage" && zip -qr "$ZIP_OUT" "BroadcastingApp_$LABEL")
rm -rf "$OUT/_bundle_stage"
echo "$LABEL" > "$OUT/LATEST.txt"
echo "완료: $ZIP_OUT"
ls -lh "$ZIP_OUT"
