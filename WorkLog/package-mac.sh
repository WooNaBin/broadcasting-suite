#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
STAMP="$(date +%Y%m%d)"
DEST="${WORKLOG_BUILD_DIR:-$ROOT/../Builded/WorkLog}"
PROJ="$ROOT/MacBridge/MacBridge.csproj"
PLIST="$ROOT/MacBridge/Info.plist"
BUILD="$DEST/_build"

write_silent_launcher() {
  local dir="$1"
  local file="$2"
  cat > "$dir/$file" <<'EOF'
#!/bin/bash
cd "$(dirname "$0")" || exit 1
BIN="./WorkLog"
chmod +x "$BIN" 2>/dev/null || true
if ! pgrep -xq "WorkLog" >/dev/null 2>&1; then
  nohup "$BIN" >/dev/null 2>&1 &
  disown 2>/dev/null || true
fi
osascript >/dev/null 2>&1 <<'OSA' &
delay 0.2
tell application "Terminal"
  try
    close front window saving no
  end try
end tell
OSA
exit 0
EOF
  chmod +x "$dir/$file"
}

build_one() {
  local rid="$1"
  local label="$2"
  local out="$BUILD/MacBridge-$rid"
  echo "[$label] publishing $rid (WorkLog)..."
  dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "$out"

  # 형제 앱과 같은 폴더형 포터블 (#61) — Launch.command는 Terminal 창을 남기지 않음
  local portable="$DEST/WorkLog-macOS-$label"
  rm -rf "$portable"
  mkdir -p "$portable"
  cp "$out/WorkLog" "$portable/WorkLog"
  chmod +x "$portable/WorkLog"
  # 부가 파일 있으면 복사
  find "$out" -mindepth 1 -maxdepth 1 ! -name WorkLog -exec cp -R {} "$portable/" \;
  write_silent_launcher "$portable" "Launch-WorkLog.command"
  write_silent_launcher "$portable" "Start-WorkLog.command"
  echo "  folder: $portable"

  # 전환기: .app 도 유지 (Finder 더블클릭 / Legacy)
  local app="$DEST/WorkLog-macOS-$label.app"
  rm -rf "$app"
  mkdir -p "$app/Contents/MacOS"
  cp "$out/WorkLog" "$app/Contents/MacOS/WorkLog"
  chmod +x "$app/Contents/MacOS/WorkLog"
  cp "$PLIST" "$app/Contents/Info.plist"
  echo "  app: $app"

  local zip="$DEST/WorkLog-macOS-$label-$STAMP.zip"
  rm -f "$zip"
  (cd "$DEST" && zip -qr "$(basename "$zip")" "$(basename "$portable")")
  echo "Created $zip"
}

mkdir -p "$DEST"
rm -rf "$BUILD"
mkdir -p "$BUILD"
build_one osx-arm64 arm64
build_one osx-x64 x64
rm -rf "$BUILD"
echo "Done under $DEST"
echo "권장: WorkLog-macOS-{arm64|x64}/Launch-WorkLog.command (Terminal 창 자동 닫힘)"
