#!/bin/bash
# double-click on macOS
cd "$(dirname "$0")"
chmod +x ./Open-UiDemos.sh 2>/dev/null || true
exec ./Open-UiDemos.sh
