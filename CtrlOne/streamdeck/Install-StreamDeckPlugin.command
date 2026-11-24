#!/bin/bash
# CtrlOne Stream Deck 플러그인 설치 (배포 폴더에서 실행)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/com.ctrlone.actions.sdPlugin"
DST="$HOME/Library/Application Support/com.elgato.StreamDeck/Plugins/com.ctrlone.actions.sdPlugin"

if [[ ! -f "$SRC/manifest.json" ]]; then
  echo "[오류] 플러그인 폴더가 없습니다: $SRC" >&2
  exit 1
fi

echo "Stream Deck 앱이 실행 중이면 종료한 뒤 Enter..."
read -r _

mkdir -p "$(dirname "$DST")"
rm -rf "$DST"
cp -R "$SRC" "$DST"
echo
echo "설치 완료: $DST"
echo "Stream Deck 앱을 다시 실행한 뒤 CtrlOne 카테고리에서 원터치 액션을 배치하세요."
