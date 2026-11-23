#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

ROOT="$(pwd)"
STAMP="$(date +%Y%m%d)"
DEST_ROOT="${FILECHECKER_BUILD_DIR:-$ROOT/../Builded/FileChecker}"
mkdir -p "$DEST_ROOT"

build_one() {
  local rid="$1"
  local label="$2"
  local out="$DEST_ROOT/_build/$rid"
  local stage="$DEST_ROOT/FileChecker-macOS-$label"
  local zip="$DEST_ROOT/FileChecker-macOS-$label-$STAMP.zip"

  echo "[$label] publishing $rid..."
  rm -rf "$out" "$stage"
  mkdir -p "$out" "$stage"

  dotnet publish "$ROOT/FileCheckerFinder.csproj" \
    -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
    -o "$out"

  local bin="$out/FileCheckerFinder"
  if [[ ! -f "$bin" ]]; then
    echo "error: $bin not found" >&2
    exit 1
  fi
  chmod +x "$bin"
  cp "$bin" "$stage/FileCheckerFinder"
  # wwwroot is needed at runtime (not embedded)
  if [[ -d "$out/wwwroot" ]]; then
    cp -R "$out/wwwroot" "$stage/wwwroot"
  elif [[ -d "$ROOT/wwwroot" ]]; then
    cp -R "$ROOT/wwwroot" "$stage/wwwroot"
  fi

  cat > "$stage/시작하기.txt" <<'EOF'
FileCheckerFinder (macOS)

1. FileCheckerFinder 를 더블클릭하거나 터미널에서 실행합니다.
2. 브라우저가 http://127.0.0.1:5187 로 열립니다.
3. 설정에서 로컬 경로·NAS IP·검색 경로·계정을 입력합니다.
   - 로컬 경로는 직접 입력 (Finder 폴더 선택은 Windows만)
   - NAS 검색 경로 예: Permanent DATA/H264_mp4 DATA

참고
- .NET 별도 설치 불필요 (self-contained)
- Gatekeeper: 우클릭 → 열기, 또는
  xattr -dr com.apple.quarantine FileCheckerFinder
- 시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크 허용이 필요할 수 있습니다.
EOF

  rm -f "$zip"
  (cd "$DEST_ROOT" && zip -qry "$(basename "$zip")" "$(basename "$stage")")
  echo "Created $zip"
}

build_one osx-arm64 arm64
build_one osx-x64 x64
rm -rf "$DEST_ROOT/_build"
echo "Done under $DEST_ROOT (port 5187)"
ls -lh "$DEST_ROOT"/FileChecker-macOS-* 2>/dev/null | head -20
