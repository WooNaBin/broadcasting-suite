#!/bin/bash
# macOS Finder 더블클릭용 (Configure-BroadcastBuild.bat 대응)
set -euo pipefail
cd "$(dirname "$0")"

# Build-BroadcastApps.command 의 config 경로와 동일
exec "./Build-BroadcastApps.command" config
