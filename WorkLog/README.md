# WorkLog

교회 방송실 **공유 업무일지**.  
[ScheduleDataManager](../ScheduleDataManager/)와 같은 NAS에 저장하며, 스케줄 JSON은 읽기만 하고 일지는 `_work_log`에 따로 둡니다.

## Phase 1 — Windows

1. `package-portable.bat` / `package-portable.ps1` 실행  
   → 기본 출력: `D:\Projects\Builded\WorkLog\`
2. `WorkLog-Windows-x64-YYYYMMDD.zip` 압축 해제
3. `WorkLog.exe` 실행 (브라우저가 `http://127.0.0.1:17822` 로 열림)

로그인 시 공유 경로 예:

`Temp DATA\_data\_work_log`

연결 후 **고윤석 / 김종희 / 임원빈** 중 프로필을 선택합니다.  
누가 무엇을 바꿨는지는 `audit/YYYY-MM.jsonl`에 남습니다.

## Phase 1 — Mac

1. `package-mac.bat` / `package-mac.ps1` (또는 Mac에서 `./package-mac.sh`)  
   → 기본 출력: `D:\Projects\Builded\WorkLog\`
2. `WorkLog-macOS-arm64-YYYYMMDD.zip` (Apple Silicon) 또는 `…-x64-…` (Intel) 압축 해제
3. `WorkLog-macOS-arm64.app` (또는 x64) 실행  
   - 처음: 우클릭 → 열기, 또는 `xattr -dr com.apple.quarantine WorkLog-macOS-*.app`
   - 실행이 안 되면: `chmod +x WorkLog-macOS-arm64.app/Contents/MacOS/WorkLog`

로그인·공유 경로·프로필은 Windows와 동일합니다.  
SMB 마운트에 실패하면 Finder에서 `smb://` 로 먼저 연결한 뒤 다시 시도하세요.

## 포트 / 동시 실행

다른 방송실 프로그램과 포트를 나눕니다. 자세한 표: [docs/ports.md](docs/ports.md)

| 앱 | 포트 |
|----|------|
| schedule-data-manager | 17821 |
| **WorkLog** | **17822** |
| ScheduleReader | 17823 |
| CtrlOne | 5177 |

## Phase 2 — 원격 API (모바일 등)

`nas-api` 폴더의 HTTP API를 NAS에 배포한 뒤, 로그인 화면에서 **원격 API**를 선택합니다.  
자세한 내용은 [nas-api/README.md](nas-api/README.md).

## 데이터

스키마·경로: [docs/schema.md](docs/schema.md)
