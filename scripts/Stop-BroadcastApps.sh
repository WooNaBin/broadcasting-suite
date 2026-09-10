#!/usr/bin/env bash
# 방송실 관련 프로세스 종료 (macOS)
set -euo pipefail

NAMES=(
  BroadcastNasBridge
  CtrlOne
  WorkLog
  BroadcastingSchedule
  ScheduleReader
  FileCheckerFinder
)

echo "방송실 프로그램 프로세스 종료"
echo "=========================="
killed=0
for name in "${NAMES[@]}"; do
  # -x exact match; 여러 개면 모두
  pids="$(pgrep -x "$name" 2>/dev/null || true)"
  if [[ -z "$pids" ]]; then
    continue
  fi
  while read -r pid; do
    [[ -z "$pid" ]] && continue
    echo "  종료: $name (PID $pid)"
    kill "$pid" 2>/dev/null || kill -9 "$pid" 2>/dev/null || true
    killed=$((killed + 1))
  done <<< "$pids"
done

if [[ "$killed" -eq 0 ]]; then
  echo "  실행 중인 방송실 프로세스가 없습니다."
else
  echo "  ${killed}개 프로세스 종료."
fi
sleep 0.4
