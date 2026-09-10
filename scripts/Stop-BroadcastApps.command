#!/bin/bash
cd "$(dirname "$0")" || exit 1
chmod +x "./Stop-BroadcastApps.sh" 2>/dev/null || true
./Stop-BroadcastApps.sh
echo ""
read -r -p "Enter 키를 누르면 종료… " _
