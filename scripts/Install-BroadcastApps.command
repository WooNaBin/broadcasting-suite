#!/bin/bash
# Finder 더블클릭용 — 같은 폴더의 Install-BroadcastApps.sh 실행
cd "$(dirname "$0")" || exit 1
chmod +x "./Install-BroadcastApps.sh" 2>/dev/null || true
exec "./Install-BroadcastApps.sh" "$@"
