#!/usr/bin/env bash
# 방송실 프로그램 제거 (macOS): 프로세스 종료 → 설치 폴더·바탕화면 바로가기 삭제
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
if [[ -f "$HERE/Stop-BroadcastApps.sh" ]]; then
  chmod +x "$HERE/Stop-BroadcastApps.sh" 2>/dev/null || true
  # shellcheck source=/dev/null
  bash "$HERE/Stop-BroadcastApps.sh" || true
fi

DEFAULT_DIR="$HOME/Applications/BroadcastingApp"
INSTALL_DIR="${1:-}"

if [[ -z "$INSTALL_DIR" ]]; then
  echo ""
  echo "방송실 프로그램 제거"
  echo "===================="
  echo "  [1] 이 삭제 프로그램이 있는 위치: $HERE"
  echo "  [2] 기본 설치 위치: $DEFAULT_DIR"
  echo "  [3] 다른 폴더 선택"
  choice="$(osascript <<EOF 2>/dev/null || true
try
  set r to display dialog "삭제할 설치 폴더를 고르세요." buttons {"기본 위치", "폴더 선택", "이 위치"} default button "이 위치" with title "방송실 프로그램 제거"
  if button returned of r is "이 위치" then
    return "1"
  else if button returned of r is "기본 위치" then
    return "2"
  else
    return "3"
  end if
on error
  return "1"
end try
EOF
)"
  choice="${choice%$'\r'}"
  if [[ "$choice" == "2" ]]; then
    INSTALL_DIR="$DEFAULT_DIR"
  elif [[ "$choice" == "3" ]]; then
    picked="$(osascript <<EOF 2>/dev/null || true
try
  set defaultPath to POSIX file "$HERE"
  set chosen to choose folder with prompt "삭제할 설치 폴더를 선택하세요" default location defaultPath
  return POSIX path of chosen
on error
  return ""
end try
EOF
)"
    picked="${picked%$'\r'}"
    picked="${picked%/}"
    if [[ -n "$picked" ]]; then
      INSTALL_DIR="$picked"
    else
      INSTALL_DIR="$HERE"
    fi
  else
    INSTALL_DIR="$HERE"
  fi
fi
INSTALL_DIR="${INSTALL_DIR%/}"

echo ""
echo "삭제 대상: $INSTALL_DIR"
confirm="$(osascript <<EOF 2>/dev/null || true
try
  set r to display dialog "다음 폴더와 바탕화면 바로가기를 삭제할까요?\n$INSTALL_DIR" buttons {"취소", "삭제"} default button "취소" with title "방송실 프로그램 제거"
  if button returned of r is "삭제" then
    return "Y"
  else
    return "N"
  end if
on error
  return "N"
end try
EOF
)"
if [[ "$confirm" != "Y" ]]; then
  echo "취소했습니다."
  exit 0
fi

if [[ -d "$INSTALL_DIR" ]]; then
  if rm -rf "$INSTALL_DIR"; then
    echo "설치 폴더 삭제: $INSTALL_DIR"
  else
    echo "설치 폴더 삭제 실패: $INSTALL_DIR" >&2
    echo "이 스크립트가 설치 폴더 안에 있으면, 압축 푼 폴더의 Uninstall을 사용하세요." >&2
  fi
else
  echo "설치 폴더 없음: $INSTALL_DIR"
fi

DESKTOP="$(osascript -e 'POSIX path of (path to desktop folder)' 2>/dev/null | tr -d '\r' || true)"
DESKTOP="${DESKTOP%/}"
DESKTOP="${DESKTOP:-$HOME/Desktop}"
SHORTCUT_DIR="${DESKTOP}/방송실 프로그램"
if [[ -d "$SHORTCUT_DIR" ]]; then
  rm -rf "$SHORTCUT_DIR" && echo "바로가기 폴더 삭제: $SHORTCUT_DIR"
else
  echo "바탕화면 바로가기 폴더 없음."
fi

echo ""
echo "제거 작업이 끝났습니다."
read -r -p "Enter 키를 누르면 종료… " _
