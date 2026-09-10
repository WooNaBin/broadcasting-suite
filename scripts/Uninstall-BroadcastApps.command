#!/bin/bash
cd "$(dirname "$0")" || exit 1
chmod +x "./Uninstall-BroadcastApps.sh" "./Stop-BroadcastApps.sh" 2>/dev/null || true
exec "./Uninstall-BroadcastApps.sh" "$@"
