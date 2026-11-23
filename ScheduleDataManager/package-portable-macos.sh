#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

ROOT="$(pwd)"
ARCH="$(uname -m)"
if [[ "$ARCH" == "arm64" ]]; then
  RID="osx-arm64"
else
  RID="osx-x64"
fi

STAMP="$(date +%Y%m%d)"
OUT_DIR="$ROOT/publish/LocalBridge-macos"
STAGE_DIR="$ROOT/publish/BroadcastingSchedule-macos-portable"
ZIP_PATH="$ROOT/publish/BroadcastingSchedule-macOS-${RID#osx-}-$STAMP.zip"
APP_NAME="BroadcastingSchedule"

echo "[1/3] Publishing self-contained macOS ($RID) build..."
dotnet publish "$ROOT/LocalBridge/LocalBridge.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$OUT_DIR"

BIN="$OUT_DIR/$APP_NAME"
if [[ ! -f "$BIN" ]]; then
  echo "error: $BIN not found" >&2
  exit 1
fi
chmod +x "$BIN"

echo "[2/3] Staging portable folder..."
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR"
cp "$BIN" "$STAGE_DIR/$APP_NAME"
ICO="$OUT_DIR/BroadcastingSchedules.ico"
if [[ -f "$ICO" ]]; then
  cp "$ICO" "$STAGE_DIR/BroadcastingSchedules.ico"
fi
cat > "$STAGE_DIR/시작하기.txt" <<'EOF'
Broadcasting Schedule (macOS)

1. BroadcastingSchedule 을 더블클릭하거나 터미널에서 실행합니다.
2. 브라우저가 http://127.0.0.1:17821 로 열립니다.
3. NAS 주소·공유 폴더·계정으로 연결합니다.

참고
- .NET 별도 설치 불필요 (self-contained)
- 처음 실행 시 Gatekeeper가 막을 수 있습니다.
  우클릭 → 열기, 또는:
  xattr -dr com.apple.quarantine BroadcastingSchedule
- 시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크 허용이 필요할 수 있습니다.
EOF

echo "[3/3] Creating zip..."
rm -f "$ZIP_PATH"
(
  cd "$STAGE_DIR"
  zip -qry "$ZIP_PATH" .
)

echo ""
echo "Done."
echo "Portable folder: $STAGE_DIR"
echo "Zip package:     $ZIP_PATH"
echo "Extract and run ./BroadcastingSchedule"
ls -lh "$ZIP_PATH"
