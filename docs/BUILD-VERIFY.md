# 빌드 확인 체크리스트

최신 스위트 빌드 후 **직접 확인해야 할 항목**을 모은 문서입니다.  
`Build-BroadcastApps.ps1` / `.sh` 실행 시 **「현재 빌드(자동)」** 구역이 갱신되고, 동일 내용이 `Builded\BUILD-VERIFY.md`에도 복사됩니다.

- 배포 설치 절차: [DEPLOY-CHECKLIST.md](DEPLOY-CHECKLIST.md)
- 변경 기록: [CHANGES.md](../CHANGES.md) · 할 일: [TODO.md](../TODO.md)

체크(`- [x]`)는 **실기한 사람이 수동으로** 표시합니다. 자동 구역의 메타·산출물 표만 빌드가 덮어씁니다.

---

<!-- BUILD-VERIFY:AUTO-START -->
## 현재 빌드 (자동)

| 항목 | 값 |
|------|-----|
| 라벨 | `1.2.0.17_20260910` |
| 버전 | `1.2.0` (build 17) |
| 빌드 시각 | 2026-09-10 11:49:10 |
| 출력 | `D:\Projects\Builded` |
| 통합 zip | `D:\Projects\Builded\BroadcastingApp_1.2.0.17_20260910.zip` |
| Windows | True |
| Mac | False |
| IncludeLegacy | True |

### 이번 실행 앱 결과

| 앱 | 상태 |
|----|------|
| BroadcastNasBridge | ok |
| CtrlOne | ok |
| FileChecker | ok |
| ScheduleDataManager | ok |
| ScheduleReader | ok |
| WorkLog | ok |

### 산출물 빠른 확인 (자동 힌트)

- [x] 통합 zip 경로가 `LATEST.txt` / 위 표와 일치

- [x] `Windows\BroadcastNasBridge-Windows-x64` 존재 예상
- [x] `Windows\Legacy\` (SDM/WL/FC) — IncludeLegacy=True
- [ ] `Mac\arm64\` · `Mac\x64\` — 이번 빌드는 Windows만 (Host/Windows)
<!-- BUILD-VERIFY:AUTO-END -->

---

<!-- BUILD-VERIFY:MANUAL-START -->
## 이번 릴리스 실기 (수동)

`1.2.0` 기준. 새 Minor/기능 빌드 후 항목을 추가·정리하세요. 빌드 스크립트는 **이 구역을 지우지 않습니다.**

### Windows

- [ ] Bridge `Start-BroadcastNasBridge.bat` → http://127.0.0.1:17820 NAS 연결
- [ ] `/files` — 기간 기본(일주일~오늘) · 날짜 접기 · 장소·특송/YT 아이콘 · 삭제 버튼 없음 (`#125`–`#128`)
- [ ] `/worklog` — NAS 연결 후 로그인(설정)에 멈추지 않고 프로필/메인 진입 (일지 루트 복구)
- [ ] `/schedule` — 헤더 슬림·연결 푸터·경로 ellipsis(클릭 시 전체 경로) (`#10` `#11` `#13` `#17`)
- [ ] `/schedule` — 달력 빈칸→일정보기 · 토/일 톤 · 월 타이포 · 타이틀 glow · 공유사항 읽기톤 (`#14`–`#19`)
- [ ] `/schedule` — 일괄 입력 색상 칩 UI (`#20`)
- [ ] Install 후 바로가기(일정·일지·파일체크)가 브리지로 열림 · Legacy exe는 `Windows\Legacy\` (`#124`)
- [ ] zip 안 `HOW-TO-START.txt` · README 레이아웃 안내 (`#63`)

### macOS (실기 필요)

- [ ] `Mac\<arch>\BroadcastNasBridge-macOS-*\Launch-BroadcastNasBridge.command` → 17820
- [ ] 월일정(광주교회 x월) 이미지 표시 (`#12`)
- [ ] FileChecker NAS 테스트·목록 (브리지 `/files` 권장 · 단독 시 `#40`)
- [ ] Bridge만 켠 뒤 SDM/WL/FC 실행 시 추가 마운트 없이 URL만 (`#3`) — Finder/마운트 ≤2 목표
- [ ] WorkLog 폴더형 `Launch-WorkLog.command` (`.app`은 `Legacy\`) (`#61`)

### 보류(코드 변경 없음 — 실기만)

- [ ] `#2` 세 앱+브리지 동시 접속/해제 스모크
- [ ] SR `#20` — 샘플 엑셀 제공 후

## 상시 스모크 (매 배포)

- [ ] Bridge 탭 전부 닫으면 약 5초 후 프로세스 종료
- [ ] CtrlOne http://127.0.0.1:5177
- [ ] ScheduleReader `Setup-And-Run.bat` / `serve.bat` → 17823
- [ ] 레거시 단독은 브리지 **꺼진** 상태에서만 자체 포트 기동
<!-- BUILD-VERIFY:MANUAL-END -->
