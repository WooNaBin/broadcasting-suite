#!/usr/bin/env bash
# 호스트 OS에서 스위트 배포본을 빌드 (Build-BroadcastApps.ps1 대응)
# 산출 경로: --out > BROADCAST_BUILD_DIR > broadcast-suite.build.json > <레포>/Builded
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIG_FILE="$ROOT/broadcast-suite.build.json"
STAMP="$(date +%Y%m%d)"
BUMP="Build" # Build | Minor | Major | none | Patch
OUT_OVERRIDE=""
SET_OUT=""
TARGET=""
SHOW_CONFIG=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT_OVERRIDE="${2:-}"; shift 2 ;;
    --set-out) SET_OUT="${2:-}"; shift 2 ;;
    --target) TARGET="${2:-}"; shift 2 ;;
    --show-config) SHOW_CONFIG=1; shift ;;
    --configure)
      echo "산출 폴더 경로를 입력하세요."
      read -r SET_OUT
      shift
      ;;
    Build|Minor|Major|none|Patch|None) BUMP="$1"; shift ;;
    *) echo "알 수 없는 인자: $1"; exit 1 ;;
  esac
done

resolve_out() {
  python3 - "$ROOT" "$CONFIG_FILE" "${OUT_OVERRIDE}" "${SET_OUT}" "${BROADCAST_BUILD_DIR:-}" <<'PY'
import json, os, sys
from pathlib import Path
root, cfg_path, out_override, set_out, env_out = sys.argv[1:6]
def expand(p):
    if not p:
        return None
    p = os.path.expanduser(p.strip().strip('"'))
    path = Path(p)
    if not path.is_absolute():
        path = Path(root) / path
    return str(path.resolve())
cfg = {}
if Path(cfg_path).is_file():
    cfg = json.loads(Path(cfg_path).read_text(encoding="utf-8"))
chosen = expand(out_override) or expand(set_out) or expand(env_out) or expand(cfg.get("outRoot")) or str((Path(root) / "Builded").resolve())
print(chosen)
PY
}

config_targets() {
  python3 - "$CONFIG_FILE" "${TARGET}" <<'PY'
import json, sys
from pathlib import Path
cfg_path, requested = sys.argv[1], sys.argv[2].strip().lower()
raw = requested
if not raw and Path(cfg_path).is_file():
    raw = str(json.loads(Path(cfg_path).read_text(encoding="utf-8")).get("targets") or "host").lower()
if not raw:
    raw = "host"
if raw in ("all", "both"):
    print("all")
elif raw in ("mac", "macos", "osx"):
    print("macos")
elif raw in ("win", "windows"):
    print("windows")
else:
    print("host")
PY
}

save_config() {
  local out_path="$1" targets_val="$2"
  python3 - "$CONFIG_FILE" "$out_path" "$targets_val" <<'PY'
import json, datetime, sys
from pathlib import Path
path, out, targets = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
payload = {"outRoot": out, "targets": targets, "updatedAt": datetime.datetime.now().astimezone().isoformat()}
path.write_text(json.dumps(payload, indent=4, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"빌드 설정 저장: {path}")
print(f"  outRoot = {out}")
print(f"  targets = {targets}")
PY
}

OUT="$(resolve_out)"
RESOLVED_TARGET="$(config_targets)"
HOST_OS="macos"
[[ "$(uname -s)" == "Darwin" ]] || HOST_OS="linux"
[[ "${OS:-}" == "Windows_NT" ]] && HOST_OS="windows"

WANT_WIN=0
WANT_MAC=0
case "$RESOLVED_TARGET" in
  all) WANT_WIN=1; WANT_MAC=1 ;;
  windows) WANT_WIN=1 ;;
  macos) WANT_MAC=1 ;;
  *)
    if [[ "$HOST_OS" == "windows" ]]; then WANT_WIN=1; else WANT_MAC=1; WANT_WIN=1; fi
    ;;
esac

if [[ -n "$SET_OUT" ]]; then
  OUT="$(python3 -c 'import os,sys; from pathlib import Path; p=Path(os.path.expanduser(sys.argv[1])); print(p if p.is_absolute() else (Path(sys.argv[2])/p).resolve())' "$SET_OUT" "$ROOT")"
  save_config "$OUT" "$RESOLVED_TARGET"
fi

if [[ "$SHOW_CONFIG" -eq 1 ]]; then
  echo "설정 파일: $CONFIG_FILE"
  echo "산출 경로: $OUT"
  echo "대상: $RESOLVED_TARGET  Windows=$WANT_WIN  Mac=$WANT_MAC"
  echo "우선순위: --out > BROADCAST_BUILD_DIR > broadcast-suite.build.json > <레포>/Builded"
  exit 0
fi

if [[ ! -f "$CONFIG_FILE" ]]; then
  save_config "$OUT" "$RESOLVED_TARGET"
fi

VERSION_FILE="$ROOT/broadcast-suite.version.json"
echo "산출 경로: $OUT"
echo "대상: $RESOLVED_TARGET  Windows=$WANT_WIN  Mac=$WANT_MAC"
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
b = bump.lower()
if b == "major":
    parts = [parts[0] + 1, 0, 0]; build = 1
elif b == "minor":
    parts = [parts[0], parts[1] + 1, 0]; build = 1
elif b == "patch":
    parts = [parts[0], parts[1], parts[2] + 1]; build = 1
elif b == "build":
    build += 1
elif b == "none":
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

publish_osx() {
  local proj="$1" rid="$2" outdir="$3"
  shift 3
  mkdir -p "$outdir"
  dotnet publish "$proj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
    "$@" -o "$outdir"
}

zip_dir() {
  local src="$1" zip="$2"
  rm -f "$zip"
  (cd "$(dirname "$src")" && zip -qr "$zip" "$(basename "$src")")
}

package_osx_folder() {
  local proj="$1" dest="$2" prefix="$3" bin="$4"
  local rid label stage portable
  for pair in "osx-arm64:arm64" "osx-x64:x64"; do
    rid="${pair%%:*}"; label="${pair##*:}"
    stage="$dest/_stage_$rid"
    rm -rf "$stage"
    publish_osx "$proj" "$rid" "$stage"
    portable="$dest/${prefix}-macOS-$label"
    rm -rf "$portable"
    mkdir -p "$portable"
    if [[ -f "$stage/$bin" ]]; then
      cp -R "$stage"/. "$portable/"
    else
      echo "error: $bin 없음 ($rid)" >&2
      return 1
    fi
    rm -rf "$stage"
    echo "  Mac $label: $portable"
  done
}

# --- CtrlOne ---
echo "== CtrlOne =="
CO_DEST="$OUT/CtrlOne"
mkdir -p "$CO_DEST"
if [[ "$WANT_WIN" -eq 1 ]]; then
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
fi
if [[ "$WANT_MAC" -eq 1 ]]; then
  echo "== CtrlOne macOS =="
  export CTRLONE_BUILD_DIR="$CO_DEST"
  if [[ -f "$ROOT/CtrlOne/package-portable-macos.sh" ]]; then
    bash "$ROOT/CtrlOne/package-portable-macos.sh"
  else
    package_osx_folder "$ROOT/CtrlOne/CtrlOne.csproj" "$CO_DEST" "CtrlOne" "CtrlOne" || echo "  CtrlOne Mac 건너뜀" >&2
  fi
fi

# --- FileChecker ---
echo "== FileChecker =="
FC_DEST="$OUT/FileChecker"
mkdir -p "$FC_DEST"
if [[ "$WANT_WIN" -eq 1 ]]; then
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
fi
if [[ "$WANT_MAC" -eq 1 ]]; then
  echo "== FileChecker macOS =="
  if [[ -f "$ROOT/FileChecker/package-portable-macos.sh" ]]; then
    export FILECHECKER_BUILD_DIR="$FC_DEST"
    bash "$ROOT/FileChecker/package-portable-macos.sh"
  else
    echo "  FileChecker는 Windows 전용 (WinForms) — Mac 생략"
  fi
fi

# --- ScheduleDataManager ---
echo "== ScheduleDataManager =="
export SCHEDULE_BUILD_DIR="$OUT/ScheduleDataManager"
SDM_DEST="$SCHEDULE_BUILD_DIR"
mkdir -p "$SDM_DEST"
if [[ "$WANT_WIN" -eq 1 ]]; then
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
fi
if [[ "$WANT_MAC" -eq 1 ]]; then
  echo "== ScheduleDataManager macOS =="
  package_osx_folder "$ROOT/ScheduleDataManager/LocalBridge/LocalBridge.csproj" \
    "$SDM_DEST" "BroadcastingSchedule" "BroadcastingSchedule"
fi

# --- WorkLog ---
echo "== WorkLog =="
export WORKLOG_BUILD_DIR="$OUT/WorkLog"
WL_DEST="$WORKLOG_BUILD_DIR"
mkdir -p "$WL_DEST"
if [[ "$WANT_WIN" -eq 1 ]]; then
  WL_BUILD="$WL_DEST/_build"
  rm -rf "$WL_BUILD"
  publish_win "$ROOT/WorkLog/LocalBridge/LocalBridge.csproj" "$WL_BUILD/win-x64"
  mkdir -p "$WL_DEST/WorkLog-Windows-x64"
  cp "$WL_BUILD/win-x64/WorkLog.exe" "$WL_DEST/WorkLog-Windows-x64/WorkLog.exe"
  rm -rf "$WL_BUILD"
  zip_dir "$WL_DEST/WorkLog-Windows-x64" "$WL_DEST/WorkLog-Windows-x64-$STAMP.zip"
fi
if [[ "$WANT_MAC" -eq 1 ]]; then
  echo "== WorkLog macOS =="
  if [[ -f "$ROOT/WorkLog/package-mac.sh" ]]; then
    bash "$ROOT/WorkLog/package-mac.sh"
  else
    echo "  WorkLog package-mac.sh 없음 — Mac 생략"
  fi
fi

# --- ScheduleReader ---
echo "== ScheduleReader =="
SR_DEST="$OUT/ScheduleReader"
SR_PORTABLE="$SR_DEST/ScheduleReader-portable"
if [[ "$WANT_WIN" -eq 1 ]]; then
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
else
  echo "  ScheduleReader는 Windows 패키지만 — 건너뜀"
fi

# --- BroadcastNasBridge (통합 NAS) ---
echo "== BroadcastNasBridge =="
BNB_DEST="$OUT/BroadcastNasBridge"
mkdir -p "$BNB_DEST"
bash "$ROOT/BroadcastNasBridge/scripts/sync-ui.sh"
if [[ "$WANT_WIN" -eq 1 ]]; then
BNB_STAGE="$BNB_DEST/_stage"
rm -rf "$BNB_STAGE"
publish_win "$ROOT/BroadcastNasBridge/BroadcastNasBridge.csproj" "$BNB_STAGE"
mkdir -p "$BNB_DEST/BroadcastNasBridge-Windows-x64"
cp -R "$BNB_STAGE"/* "$BNB_DEST/BroadcastNasBridge-Windows-x64/"
cp "$ROOT/BroadcastNasBridge/scripts/Launch-BroadcastNasBridge.bat" "$BNB_DEST/BroadcastNasBridge-Windows-x64/" 2>/dev/null || true
cat > "$BNB_DEST/BroadcastNasBridge-Windows-x64/Start-BroadcastNasBridge.bat" <<'EOF'
@echo off
cd /d "%~dp0"
start "" BroadcastNasBridge.exe
EOF
rm -rf "$BNB_STAGE"
zip_dir "$BNB_DEST/BroadcastNasBridge-Windows-x64" "$BNB_DEST/BroadcastNasBridge-Windows-x64-$STAMP.zip"
fi
if [[ "$WANT_MAC" -eq 1 ]]; then
echo "== BroadcastNasBridge macOS =="
export BROADCAST_NAS_BRIDGE_BUILD_DIR="$BNB_DEST"
bash "$ROOT/BroadcastNasBridge/package-portable-macos.sh"
fi

# --- Suite bundle ---
echo "== Suite bundle =="
INCLUDE_LEGACY="${INCLUDE_LEGACY:-1}"
BUNDLE_ROOT="$OUT/_bundle_stage/BroadcastingApp_$LABEL"
rm -rf "$OUT/_bundle_stage"
mkdir -p "$BUNDLE_ROOT/Windows" "$BUNDLE_ROOT/Mac/arm64" "$BUNDLE_ROOT/Mac/x64"
[[ -d "$BNB_DEST/BroadcastNasBridge-Windows-x64" ]] && cp -R "$BNB_DEST/BroadcastNasBridge-Windows-x64" "$BUNDLE_ROOT/Windows/"
[[ -d "$CO_DEST/CtrlOne-Windows-x64" ]] && cp -R "$CO_DEST/CtrlOne-Windows-x64" "$BUNDLE_ROOT/Windows/"
[[ -d "$SR_PORTABLE" ]] && cp -R "$SR_PORTABLE" "$BUNDLE_ROOT/Windows/"
if [[ "$INCLUDE_LEGACY" == "1" ]]; then
  mkdir -p "$BUNDLE_ROOT/Windows/Legacy"
  [[ -d "$FC_DEST/FileChecker-Windows-x64" ]] && cp -R "$FC_DEST/FileChecker-Windows-x64" "$BUNDLE_ROOT/Windows/Legacy/"
  [[ -d "$SDM_DEST/BroadcastingSchedule-Windows-x64" ]] && cp -R "$SDM_DEST/BroadcastingSchedule-Windows-x64" "$BUNDLE_ROOT/Windows/Legacy/"
  [[ -d "$WL_DEST/WorkLog-Windows-x64" ]] && cp -R "$WL_DEST/WorkLog-Windows-x64" "$BUNDLE_ROOT/Windows/Legacy/"
fi

copy_mac() {
  local arch="$1" dest="$BUNDLE_ROOT/Mac/$1"
  shift
  for src in "$@"; do
    [[ -e "$src" ]] || continue
    cp -R "$src" "$dest/"
  done
}
copy_mac arm64 \
  "$BNB_DEST/BroadcastNasBridge-macOS-arm64" \
  "$CO_DEST/CtrlOne-macOS-arm64" \
  "$OUT/WorkLog/WorkLog-macOS-arm64" \
  "$OUT/ScheduleDataManager/BroadcastingSchedule-macOS-arm64" \
  "$FC_DEST/FileChecker-macOS-arm64"
copy_mac x64 \
  "$BNB_DEST/BroadcastNasBridge-macOS-x64" \
  "$CO_DEST/CtrlOne-macOS-x64" \
  "$OUT/WorkLog/WorkLog-macOS-x64" \
  "$OUT/ScheduleDataManager/BroadcastingSchedule-macOS-x64" \
  "$FC_DEST/FileChecker-macOS-x64"
if [[ "$INCLUDE_LEGACY" == "1" ]]; then
  [[ -d "$OUT/WorkLog/WorkLog-macOS-arm64.app" ]] && mkdir -p "$BUNDLE_ROOT/Mac/arm64/Legacy" && cp -R "$OUT/WorkLog/WorkLog-macOS-arm64.app" "$BUNDLE_ROOT/Mac/arm64/Legacy/"
  [[ -d "$OUT/WorkLog/WorkLog-macOS-x64.app" ]] && mkdir -p "$BUNDLE_ROOT/Mac/x64/Legacy" && cp -R "$OUT/WorkLog/WorkLog-macOS-x64.app" "$BUNDLE_ROOT/Mac/x64/Legacy/"
fi

cp "$ROOT/scripts/Install-BroadcastApps.ps1" "$BUNDLE_ROOT/"
cp "$ROOT/scripts/Install-BroadcastApps.bat" "$BUNDLE_ROOT/"
cat > "$BUNDLE_ROOT/HOW-TO-START.txt" <<EOF
1) BroadcastNasBridge 실행 (권장)
2) http://127.0.0.1:17820
3) 일정 / 작업일지 / 파일체크 카드 진입
EOF
cat > "$BUNDLE_ROOT/Mac/README.txt" <<EOF
macOS 배포본
============
레이아웃: Mac/arm64/ · Mac/x64/

권장: BroadcastNasBridge-macOS-*/Launch-BroadcastNasBridge.command
WorkLog는 폴더형 Launch-WorkLog.command (.app은 Legacy/)
Gatekeeper: 우클릭 → 열기 또는 xattr -dr com.apple.quarantine <폴더>
EOF
cat > "$BUNDLE_ROOT/VERSION.txt" <<EOF
BroadcastingApp suite
version: $(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['version'])")
build: $(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['build'])")
label: $LABEL
stamp: $STAMP
includeLegacy: $INCLUDE_LEGACY
Apps:
- CtrlOne (Windows + macOS)
- FileChecker (Windows Legacy)
- ScheduleDataManager (Windows Legacy + macOS)
- WorkLog (Windows Legacy + macOS)
- ScheduleReader (Windows)
- BroadcastNasBridge (Windows + macOS)
EOF
cat > "$BUNDLE_ROOT/README.txt" <<EOF
방송실 프로그램 통합 배포 패키지
================================

폴더 구성
---------
- Windows\\          … Bridge, CtrlOne, ScheduleReader
- Windows\\Legacy\\   … SDM / WorkLog / FileChecker 단독
- Mac\\arm64\\ · Mac\\x64\\
- Install-BroadcastApps.bat / .ps1
- HOW-TO-START.txt · VERSION.txt

시작: Bridge → http://127.0.0.1:17820
포트: Bridge 17820 / 레거시 17821·17822·5187 / SR 17823 / CtrlOne 5177
EOF

ZIP_OUT="$OUT/BroadcastingApp_$LABEL.zip"
rm -f "$ZIP_OUT"
(cd "$OUT/_bundle_stage" && zip -qr "$ZIP_OUT" "BroadcastingApp_$LABEL")
rm -rf "$OUT/_bundle_stage"
echo "$LABEL" > "$OUT/LATEST.txt"
echo "완료: $ZIP_OUT"
echo "사용법: Bridge 실행 후 http://127.0.0.1:17820"
ls -lh "$ZIP_OUT"

if command -v open >/dev/null 2>&1; then
  open "$OUT" || true
elif command -v xdg-open >/dev/null 2>&1; then
  xdg-open "$OUT" >/dev/null 2>&1 || true
fi
