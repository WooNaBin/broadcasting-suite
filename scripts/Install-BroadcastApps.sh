#!/usr/bin/env bash
# BroadcastingApp 통합 패키지 설치 (macOS)
# 통합 zip 루트(Windows / Mac / VERSION.txt 옆)에 둡니다.
# Mac/<arch>/* 를 설치 폴더로 복사하고, 선택 시 바탕화면/방송실 프로그램 에 Launch 바로가기를 만듭니다.
set -euo pipefail

BUNDLE_ROOT="$(cd "$(dirname "$0")" && pwd)"

detect_arch() {
  case "$(uname -m)" in
    arm64|aarch64) echo "arm64" ;;
    x86_64|amd64) echo "x64" ;;
    *) echo "arm64" ;;
  esac
}

ARCH="$(detect_arch)"
MAC_SRC="$BUNDLE_ROOT/Mac/$ARCH"

if [[ ! -d "$MAC_SRC" ]]; then
  echo "Mac/$ARCH 폴더를 찾을 수 없습니다: $MAC_SRC" >&2
  echo "이 스크립트는 통합 패키지 루트(BroadcastingApp_...)에서 실행해야 합니다." >&2
  echo "다른 CPU면 Mac/arm64 또는 Mac/x64 를 확인하세요." >&2
  exit 1
fi

pick_install_dir() {
  local default_path="$1"
  local picked=""
  picked="$(osascript <<EOF 2>/dev/null || true
try
  set defaultPath to POSIX file "$default_path"
  set chosen to choose folder with prompt "방송실 프로그램을 설치할 폴더를 선택하세요" default location defaultPath
  return POSIX path of chosen
on error
  return ""
end try
EOF
)"
  picked="${picked%$'\r'}"
  picked="${picked%/}"
  if [[ -n "$picked" ]]; then
    printf '%s\n' "$picked"
  else
    printf '%s\n' "$default_path"
  fi
}

DEFAULT_DIR="$HOME/Applications/BroadcastingApp"
INSTALL_DIR="${1:-}"
if [[ -z "${INSTALL_DIR}" ]]; then
  echo ""
  echo "방송실 프로그램 설치 (macOS · $ARCH)"
  echo "===================================="
  echo "기본 설치 위치: $DEFAULT_DIR"
  echo "폴더 선택 창이 열립니다. 취소하면 기본 위치에 설치합니다."
  INSTALL_DIR="$(pick_install_dir "$DEFAULT_DIR")"
fi
INSTALL_DIR="${INSTALL_DIR%/}"

echo ""
echo "설치 위치: $INSTALL_DIR"
echo "원본:      $MAC_SRC"
mkdir -p "$INSTALL_DIR"

echo "복사 중..."
# 기존 설치를 덮어쓰기 위해 내용 복사
rsync -a --delete "$MAC_SRC/" "$INSTALL_DIR/" 2>/dev/null || {
  # rsync 없으면 cp
  find "$INSTALL_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null || true
  cp -R "$MAC_SRC"/. "$INSTALL_DIR/"
}

if [[ -f "$BUNDLE_ROOT/VERSION.txt" ]]; then
  cp -f "$BUNDLE_ROOT/VERSION.txt" "$INSTALL_DIR/VERSION.txt"
fi

for helper in Stop-BroadcastApps.sh Stop-BroadcastApps.command \
  Uninstall-BroadcastApps.sh Uninstall-BroadcastApps.command; do
  if [[ -f "$BUNDLE_ROOT/$helper" ]]; then
    cp -f "$BUNDLE_ROOT/$helper" "$INSTALL_DIR/$helper"
    chmod +x "$INSTALL_DIR/$helper" 2>/dev/null || true
  fi
done

# Gatekeeper 격리 속성 제거 (가능하면)
xattr -dr com.apple.quarantine "$INSTALL_DIR" 2>/dev/null || true

# 실행 권한
find "$INSTALL_DIR" -type f \( -name 'Launch-*.command' -o -name '*.command' \) -exec chmod +x {} + 2>/dev/null || true
find "$INSTALL_DIR" -type f \( -name 'BroadcastNasBridge' -o -name 'CtrlOne' -o -name 'WorkLog' -o -name 'BroadcastingSchedule' -o -name 'ScheduleReader' -o -name 'FileCheckerFinder' \) -exec chmod +x {} + 2>/dev/null || true

echo "복사 완료."
echo ""

write_launcher() {
  local out="$1"
  local work_dir="$2"
  local bin_rel="$3"
  local arg="${4:-}"
  local bin_name
  bin_name="$(basename "$bin_rel")"
  # 이미 실행 중이어도 바이너리를 다시 호출한다(브리지는 mutex로 브라우저만 연다).
  # pgrep으로 건너뛰면 Terminal만 뜨고 화면이 안 열린다.
  if [[ -n "$arg" ]]; then
    cat > "$out" <<EOF
#!/bin/bash
cd "$work_dir" || { echo "설치 폴더 없음: $work_dir"; read -r -p "Enter… "; exit 1; }
BIN="./$bin_rel"
chmod +x "\$BIN" 2>/dev/null || true
echo "시작 중… ($bin_name $arg)"
nohup "\$BIN" "$arg" >/dev/null 2>&1 &
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
  else
    cat > "$out" <<EOF
#!/bin/bash
cd "$work_dir" || { echo "설치 폴더 없음: $work_dir"; read -r -p "Enter… "; exit 1; }
BIN="./$bin_rel"
chmod +x "\$BIN" 2>/dev/null || true
echo "시작 중… ($bin_name)"
nohup "\$BIN" >/dev/null 2>&1 &
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
  fi
  chmod +x "$out"
}

ask_shortcuts() {
  local ans=""
  ans="$(osascript <<'EOF' 2>/dev/null || true
try
  set r to display dialog "바탕화면에 「방송실 프로그램」 바로가기 폴더를 만들까요?" buttons {"아니요", "예"} default button "예" with title "방송실 프로그램 설치"
  if button returned of r is "예" then
    return "Y"
  else
    return "N"
  end if
on error
  return "Y"
end try
EOF
)"
  [[ "${ans}" == "Y" ]]
}

if ask_shortcuts; then
  DESKTOP="$(osascript -e 'POSIX path of (path to desktop folder)' 2>/dev/null | tr -d '\r' || true)"
  DESKTOP="${DESKTOP%/}"
  DESKTOP="${DESKTOP:-$HOME/Desktop}"
  SHORTCUT_DIR="${DESKTOP}/방송실 프로그램"
  mkdir -p "$SHORTCUT_DIR"

  # 폴더 이름 후보 (arch 접미사)
  BRIDGE_DIR=""
  for d in "$INSTALL_DIR"/BroadcastNasBridge-macOS-*; do
    [[ -d "$d" ]] && BRIDGE_DIR="$d" && break
  done
  CTRL_DIR=""
  for d in "$INSTALL_DIR"/CtrlOne-macOS-*; do
    [[ -d "$d" ]] && CTRL_DIR="$d" && break
  done
  SR_DIR=""
  for d in "$INSTALL_DIR"/ScheduleReader-macOS-*; do
    [[ -d "$d" ]] && SR_DIR="$d" && break
  done

  make_one() {
    local name="$1" dir="$2" bin="$3" arg="${4:-}"
    local out="$SHORTCUT_DIR/${name}.command"
    if [[ -z "$dir" || ! -f "$dir/$bin" ]]; then
      echo "  건너뜀 (없음): $name"
      return 0
    fi
    write_launcher "$out" "$dir" "$bin" "$arg"
    echo "  바로가기: $out"
  }

  make_one "방송실 프로그램 시작" "$BRIDGE_DIR" "BroadcastNasBridge" ""
  make_one "레코더 컨트롤러" "$CTRL_DIR" "CtrlOne" ""
  make_one "렌더링 파일 확인" "$BRIDGE_DIR" "BroadcastNasBridge" "/files/"
  make_one "방송실 일정" "$BRIDGE_DIR" "BroadcastNasBridge" "/schedule/"
  make_one "방송실 작업일지" "$BRIDGE_DIR" "BroadcastNasBridge" "/worklog/"
  make_one "스케쥴 생성 유틸" "$SR_DIR" "ScheduleReader" ""
else
  echo "바로가기 생략."
fi

echo ""
echo "설치가 끝났습니다."
echo "  $INSTALL_DIR"
echo "「방송실 프로그램 시작」 또는 바탕화면 바로가기로 실행하세요."
echo "브라우저: http://127.0.0.1:17820"
echo ""
read -r -p "Enter 키를 누르면 종료… " _
