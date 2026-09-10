#!/usr/bin/env bash
# 방송실 UI 데모 정적 서버 (NAS/빌드 불필요). Windows: Open-UiDemos.bat
set -euo pipefail
PORT=17990
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
URL="http://127.0.0.1:${PORT}/demos/"
cd "$ROOT"
echo "UI demos root: $ROOT"
echo "Open: $URL"

if command -v python3 >/dev/null 2>&1; then
  PY=python3
elif command -v python >/dev/null 2>&1; then
  PY=python
else
  echo "Python이 필요합니다." >&2
  exit 1
fi

# macOS/Linux: open browser then serve
if command -v open >/dev/null 2>&1; then
  (sleep 0.6; open "$URL") &
elif command -v xdg-open >/dev/null 2>&1; then
  (sleep 0.6; xdg-open "$URL") &
fi

exec "$PY" -m http.server "$PORT" --bind 127.0.0.1
