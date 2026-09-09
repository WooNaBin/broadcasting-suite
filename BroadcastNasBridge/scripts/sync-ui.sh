#!/usr/bin/env bash
# 형제 앱 UI를 wwwroot/{schedule,worklog,files} 로 복사·패치
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUITE="$(cd "$ROOT/.." && pwd)"

sync_schedule() {
  local src="$SUITE/ScheduleDataManager" dest="$ROOT/wwwroot/schedule"
  mkdir -p "$dest/assets/icons"
  [[ -d "$src" ]] || return 0
  cp -f "$src/index.html" "$src/styles.css" "$src/sw.js" "$dest/" 2>/dev/null || true
  if [[ -f "$src/app.js" ]]; then
    sed -e 's/const API_BASE = "";/const API_BASE = "\/schedule";/' \
        -e "s/const API_BASE = '';/const API_BASE = '\/schedule';/" \
        "$src/app.js" > "$dest/app.js"
  fi
  if [[ -d "$src/assets/icons" ]]; then
    cp -f "$src/assets/icons/"*.png "$dest/assets/icons/" 2>/dev/null || true
  fi
}

sync_worklog() {
  local src="$SUITE/WorkLog/www" dest="$ROOT/wwwroot/worklog"
  mkdir -p "$dest"
  [[ -d "$src" ]] || return 0
  cp -f "$src/index.html" "$src/styles.css" "$src/templates.js" "$dest/" 2>/dev/null || true
  if [[ -f "$dest/index.html" ]]; then
    sed -i.bak \
      -e 's|href="/styles.css"|href="/worklog/styles.css"|' \
      -e 's|href="styles.css"|href="/worklog/styles.css"|' \
      -e 's|src="/app.js"|src="/worklog/app.js"|' \
      -e 's|src="app.js"|src="/worklog/app.js"|' \
      "$dest/index.html" && rm -f "$dest/index.html.bak"
  fi
  if [[ -f "$src/app.js" ]]; then
    sed -e 's|let apiBase = "http://127.0.0.1:17822";|let apiBase = "/worklog";|' \
        -e "s|let apiBase = 'http://127.0.0.1:17822';|let apiBase = '/worklog';|" \
        -e 's|return isBridgeHosted() ? "/worklog" : "http://127.0.0.1:17822";|return "/worklog";|' \
        -e "s|return isBridgeHosted() ? '/worklog' : 'http://127.0.0.1:17822';|return '/worklog';|" \
        -e 's|return "http://127.0.0.1:17822";|return "/worklog";|' \
        -e "s|return 'http://127.0.0.1:17822';|return '/worklog';|" \
        -e 's|from "/templates.js"|from "/worklog/templates.js"|' \
        -e "s|from '/templates.js'|from '/worklog/templates.js'|" \
        -e 's|from "./templates.js"|from "/worklog/templates.js"|' \
        -e "s|from './templates.js'|from '/worklog/templates.js'|" \
        "$src/app.js" > "$dest/app.js"
  fi
}

sync_files() {
  local src="$SUITE/FileChecker/wwwroot" dest="$ROOT/wwwroot/files"
  mkdir -p "$dest"
  [[ -d "$src" ]] || return 0
  for f in "$src"/*; do
    local name
    name="$(basename "$f")"
    if [[ "$name" == "app.js" ]]; then
      sed -e "s|fetch(url, options)|fetch(url.startsWith('/api/') ? '/files' + url : url, options)|" \
          -e "s|window.location.href = '/api/|window.location.href = '/files/api/|" \
          "$f" > "$dest/app.js"
    else
      cp -f "$f" "$dest/"
    fi
  done
}

sync_schedule
sync_worklog
sync_files
echo "UI synced → $ROOT/wwwroot/{schedule,worklog,files}"
