#!/bin/bash
# macOS Finder 더블클릭용 (Build-BroadcastApps.bat 대응)
set -euo pipefail
cd "$(dirname "$0")"

SCRIPT="./scripts/Build-BroadcastApps.sh"
if [[ ! -x "$SCRIPT" ]]; then
  chmod +x "$SCRIPT" 2>/dev/null || true
fi

pick_folder() {
  local current="$1"
  local picked
  picked="$(osascript <<EOF 2>/dev/null || true
try
  set defaultPath to POSIX file "$current"
  set chosen to choose folder with prompt "방송실 빌드 산출물 폴더를 선택하세요" default location defaultPath
  return POSIX path of chosen
on error
  return ""
end try
EOF
)"
  # trailing slash 제거
  picked="${picked%/}"
  printf '%s' "$picked"
}

run_configure() {
  local current
  current="$("$SCRIPT" --show-config 2>/dev/null | sed -n 's/^산출 경로: //p' | head -1)"
  [[ -z "$current" ]] && current="$(pwd)/Builded"
  echo "현재 산출 경로: $current"
  local picked
  picked="$(pick_folder "$current")"
  if [[ -z "$picked" ]]; then
    echo "폴더 선택 취소 — 경로를 직접 입력하세요 (Enter = 유지)."
    read -r -p "> " picked
    picked="${picked%\"}"
    picked="${picked#\"}"
  fi
  if [[ -z "$picked" ]]; then
    echo "변경하지 않았습니다."
    return 0
  fi
  "$SCRIPT" --set-out "$picked"
}

run_menu() {
  while true; do
    echo ""
    echo "방송실 프로그램 빌드"
    "$SCRIPT" --show-config 2>/dev/null || true
    echo ""
    echo "  1) 빌드 시작"
    echo "  2) 대상 OS: 이 Mac만 (host)"
    echo "  3) 대상 OS: Windows + Mac"
    echo "  4) 산출 경로 변경"
    echo "  5) 설정 보기"
    echo "  Q) 종료"
    read -r -p "선택: " choice
    case "$choice" in
      1)
        "$SCRIPT"
        return $?
        ;;
      2)
        local out
        out="$("$SCRIPT" --show-config 2>/dev/null | sed -n 's/^산출 경로: //p' | head -1)"
        "$SCRIPT" --set-out "${out:-$(pwd)/Builded}" --target host
        ;;
      3)
        local out
        out="$("$SCRIPT" --show-config 2>/dev/null | sed -n 's/^산출 경로: //p' | head -1)"
        "$SCRIPT" --set-out "${out:-$(pwd)/Builded}" --target all
        ;;
      4) run_configure ;;
      5) "$SCRIPT" --show-config ;;
      q|Q) return 0 ;;
      *) echo "다시 선택하세요." ;;
    esac
  done
}

ERR=0
case "${1:-}" in
  config|configure)
    run_configure || ERR=$?
    ;;
  menu)
    run_menu || ERR=$?
    ;;
  *)
    "$SCRIPT" "$@" || ERR=$?
    ;;
esac

echo ""
read -r -p "Press Enter to close..." _
exit "$ERR"
