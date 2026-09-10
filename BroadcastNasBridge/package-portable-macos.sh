#!/usr/bin/env bash
# BroadcastNasBridge portable macOS packages (arm64 + x64)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="${BROADCAST_NAS_BRIDGE_BUILD_DIR:-${BROADCAST_BUILD_DIR:-$ROOT/../Builded}/BroadcastNasBridge}"
mkdir -p "$OUT"
bash "$ROOT/scripts/sync-ui.sh"

package_rid() {
  local rid="$1" label="$2"
  local stage="$OUT/_stage_$rid"
  local stamp
  stamp="$(date +%Y%m%d)"
  rm -rf "$stage"
  mkdir -p "$stage"
  dotnet publish "$ROOT/BroadcastNasBridge.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
    -o "$stage"
  # UI 동기화용 형제 소스가 없을 수 있으므로 publish 시 이미 wwwroot 포함
  local dest="$OUT/BroadcastNasBridge-macOS-$label"
  rm -rf "$dest"
  mkdir -p "$dest"
  cp -R "$stage"/* "$dest/"
  # 런처: 항상 기동(이미 떠 있으면 mutex가 브라우저만 연다) 후 Terminal 창 닫기
  cat > "$dest/Launch-BroadcastNasBridge.command" <<'EOF'
#!/bin/bash
cd "$(dirname "$0")" || exit 1
BIN="./BroadcastNasBridge"
chmod +x "$BIN" 2>/dev/null || true
echo "시작 중… (BroadcastNasBridge)"
nohup "$BIN" >/dev/null 2>&1 &
disown 2>/dev/null || true
osascript >/dev/null 2>&1 <<'OSA' &
delay 0.4
tell application "Terminal"
  try
    if (count of windows) > 0 then close front window saving no
  end try
end tell
OSA
exit 0
EOF
  chmod +x "$dest/Launch-BroadcastNasBridge.command" "$dest/BroadcastNasBridge" 2>/dev/null || true
  rm -rf "$stage"
  local zip="$OUT/BroadcastNasBridge-macOS-$label-$stamp.zip"
  rm -f "$zip"
  (cd "$OUT" && zip -qr "$(basename "$zip")" "$(basename "$dest")")
  echo "Created $dest"
  echo "Created $zip"
}

package_rid osx-arm64 arm64
package_rid osx-x64 x64
echo "BroadcastNasBridge macOS packages ready under $OUT"
