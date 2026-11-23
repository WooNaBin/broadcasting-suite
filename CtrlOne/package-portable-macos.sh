#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

ROOT="$(pwd)"
STAMP="$(date +%Y%m%d)"
DEST_ROOT="${CTRLONE_BUILD_DIR:-$ROOT/../Builded/CtrlOne}"
mkdir -p "$DEST_ROOT"

build_one() {
  local rid="$1"
  local label="$2"
  local out="$DEST_ROOT/_build/$rid"
  local stage="$DEST_ROOT/CtrlOne-macOS-$label"
  local zip="$DEST_ROOT/CtrlOne-macOS-$label-$STAMP.zip"

  echo "[$label] publishing $rid..."
  rm -rf "$out" "$stage"
  mkdir -p "$out" "$stage"

  # Release 기본 RID 제거된 csproj — 여기서 osx RID 지정
  dotnet publish "$ROOT/CtrlOne.csproj" \
    -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true \
    -p:DebugType=None -p:DebugSymbols=false \
    -o "$out"

  local bin="$out/CtrlOne"
  if [[ ! -f "$bin" ]]; then
    echo "error: $bin not found" >&2
    exit 1
  fi
  chmod +x "$bin"
  cp "$bin" "$stage/CtrlOne"
  # UI는 임베드됨. demo-preview는 선택 포함
  if [[ -d "$out/demo-preview" ]]; then
    cp -R "$out/demo-preview" "$stage/demo-preview"
  fi

  cat > "$stage/시작하기.txt" <<'EOF'
CtrlOne (macOS) — HyperDeck 대시보드

1. CtrlOne 을 더블클릭하거나 터미널에서 실행합니다.
2. 브라우저가 http://127.0.0.1:5177 로 열립니다.
3. 브라우저를 닫으면 프로그램이 종료됩니다.

참고
- .NET 별도 설치 불필요 (self-contained, UI 내장)
- Gatekeeper: 우클릭 → 열기, 또는
  xattr -dr com.apple.quarantine CtrlOne
- HyperDeck 검색/제어를 위해 macOS
  시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크 허용이 필요할 수 있습니다.
- 장비 설정: 같은 폴더의 devices.json / presets.json
EOF

  rm -f "$zip"
  (cd "$DEST_ROOT" && zip -qry "$(basename "$zip")" "$(basename "$stage")")
  echo "Created $zip"
}

build_one osx-arm64 arm64
build_one osx-x64 x64
rm -rf "$DEST_ROOT/_build"
echo "Done under $DEST_ROOT (port 5177)"
ls -lh "$DEST_ROOT"/CtrlOne-macOS-* 2>/dev/null | head -20
