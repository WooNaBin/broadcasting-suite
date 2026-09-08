# 방송실 프로그램 — 수정 사항

방송실 스위트(CtrlOne, FileChecker, ScheduleDataManager, ScheduleReader, WorkLog)의 **수정·배포 기록**을 남기는 문서입니다.  
빌드·버전은 `broadcast-suite.version.json` / `Builded\LATEST.txt` 와 맞춰 적습니다.

관련 문서: [PROJECTS.md](PROJECTS.md) · [TODO.md](TODO.md) · 일괄 빌드: `Build-BroadcastApps.bat`

할 일·버그는 **TODO.md**에 적고, 끝난 내용은 여기(CHANGES)에 남긴다.  
채팅: **「방송실 TODO 해줘」** → TODO 처리 / **「수정 사항 적어줘」** → 이 파일 갱신.

## 쓰는 방법

1. **새 항목은 맨 위(최신 먼저)**에 추가합니다.
2. 배포 빌드를 뽑았으면 `스위트`에 라벨을 적습니다. (예: `1.0.0.2_20260908`)
3. 채팅에서 **「수정 사항 적어줘」** / **「CHANGELOG 남겨줘」** 라고 하면 이 파일을 갱신합니다.

### 항목 템플릿 (복사해서 사용)

```markdown
## YYYY-MM-DD — 짧은 제목

- **스위트:** `x.y.z.n_yyyyMMdd` (미배포면 `—`)
- **대상:** CtrlOne / FileChecker / ScheduleDataManager / ScheduleReader / WorkLog / 빌드스크립트 / 문서
- **유형:** 수정 | 기능 | 배포 | 문서 | 기타

### 내용
- …

### 확인
- [ ] 로컬 실행
- [ ] NAS 동시 접속 (해당 시)
- [ ] `Build-BroadcastApps.bat` 빌드
```

---

## 기록

## 2026-09-08 — CtrlOne macOS 빌드 지원

- **스위트:** `—` (재패키지 시 `Mac/`에 포함)
- **대상:** CtrlOne, 빌드스크립트
- **유형:** 기능 | 배포

### 내용
- Release 고정 `win-x64`/`WinExe` 제거 → RID별 게시. macOS `open` 브라우저, 뮤텍스 이름 정리, AllocConsole는 Windows만
- `package-portable-macos.sh` → arm64/x64 (`Builded/CtrlOne/CtrlOne-macOS-*`, UI 임베드)
- 스위트 빌드 스크립트 Mac 번들에 CtrlOne 포함

### 확인
- [x] `dotnet build` / macOS publish
- [ ] HyperDeck 실기 (로컬 네트워크 권한)

## 2026-09-08 — FileChecker macOS 빌드 지원

- **스위트:** `—` (재패키지 시 `Mac/`에 포함)
- **대상:** FileChecker, 빌드스크립트
- **유형:** 기능 | 배포

### 내용
- TFM `net10.0` (WinForms 제거). macOS: 브라우저 `open`, Finder `open -R`, NAS `mount_smbfs`
- Windows: PowerShell 폴더 선택, WNet·1219 재사용 유지
- `package-portable-macos.sh` → arm64/x64 portable (`Builded/FileChecker/FileChecker-macOS-*`)
- `Build-BroadcastApps.sh` Mac 번들에 FileChecker 포함

### 확인
- [x] `dotnet build` / `package-portable-macos.sh`
- [ ] 방송실 Mac에서 NAS 스캔·동기화 실기

## 2026-09-08 — 스위트 1.1.0.2 macOS 산출물 포함

- **스위트:** `1.1.0.2_20260908`
- **대상:** WorkLog, ScheduleDataManager, 메타 패키지
- **유형:** 배포

### 내용
- Mac 패키지 있는 앱만 빌드: WorkLog (arm64/x64 `.app`), ScheduleDataManager (arm64/x64 portable)
- 통합 zip `Mac/`에 포함. CtrlOne·FileChecker·ScheduleReader는 Mac 패키지 없음
- 산출물: `Builded/BroadcastingApp_1.1.0.2_20260908.zip`

### 확인
- [x] WorkLog `package-mac.sh` (arm64·x64)
- [x] SDM `package-portable-macos.sh` + osx-x64
- [ ] Gatekeeper/실기 실행

## 2026-09-08 — 스위트 1.1.0.1 배포 빌드 (TODO 일괄)

- **스위트:** `1.1.0.1_20260908`
- **대상:** CtrlOne, ScheduleDataManager, WorkLog, 빌드스크립트, 문서
- **유형:** 배포 | 기능 | 수정

### 내용
- `#11` SDM 일괄 일정 입력 다이얼로그
- `#21`/`#22` WorkLog 프로필 PIN + 인쇄 템플릿 접두 제거·시트 다듬기
- `#43` CtrlOne 수신 버퍼 상한·슬롯 3+ UI
- `#63`/`#64` 통합 zip `Windows/`·`Mac/` 분리 + `Install-BroadcastApps`
- macOS용 `scripts/Build-BroadcastApps.sh` 추가 (pwsh 미가용 시)
- 통합 zip: `Builded/BroadcastingApp_1.1.0.1_20260908.zip`

### 확인
- [x] Mac에서 bash 스위트 빌드 (win-x64 크로스 퍼블리시)
- [ ] 방송실 Windows에서 설치·실행 스모크
- [ ] `#2` NAS 세 앱 동시 접속 실기 (보류)

## 2026-09-08 — CtrlOne #43 슬롯·버퍼 + 빌드 #63/#64 Windows/Mac·설치

- **스위트:** `1.1.0.1_20260908`
- **대상:** CtrlOne, 빌드스크립트, 문서
- **유형:** 기능 | 문서

### 내용
- `#43` HyperDeck 수신 버퍼 256KB 상한, `Dictionary` 슬롯(최대 8), `DeviceSnapshot.Slots` + Slot1/Slot2 별칭, UI·데모 3슬롯 레이아웃
- `#63` 통합 zip: `Windows/` · `Mac/` 분리, README 경로 갱신
- `#64` `Install-BroadcastApps.ps1`/`.bat` — 설치 폴더·Windows만 복사·바로가기 선택
- PROJECTS / DEPLOY-CHECKLIST 폴더 안내

### 확인
- [x] CtrlOne Release 빌드
- [x] 통합 zip `Windows/`·`Mac/` 레이아웃
- [ ] 3슬롯 장비 실기 모니터링

## 2026-09-08 — WorkLog #21 PIN · #22 인쇄 다듬기

- **스위트:** `1.1.0.1_20260908`
- **대상:** WorkLog
- **유형:** 기능 | 수정 | 문서

### 내용
- `#22`: 인쇄 본문에서 `[템플릿…]` 접두 제거(일지 텍스트만). `.print-sheet` 헤더 룰·half `#fafafa`·날짜 계층 보강(흑백 인쇄 고려)
- `#21`: 프로필 선택 시 숫자 PIN(1–8) 게이트. 미설정이면 설정(이중 입력) 후 PBKDF2-SHA256을 `profiles.json`에 저장. 검증 실패 시 토스트·프로필 화면 유지. 기억된 profileId도 동일 게이트. prefs/audit에 PIN 미저장
- `docs/schema.md` profiles.pin 스키마 반영

### 확인
- [x] 스위트 빌드 포함
- [ ] 방송실에서 PIN·인쇄 실기
- [ ] NAS 동시 접속 (해당 시)

## 2026-09-08 — SDM 일괄 일정 입력 (#11)

- **스위트:** `1.1.0.1_20260908`
- **대상:** ScheduleDataManager
- **유형:** 기능

### 내용
- 일정 보드에 **일괄 입력** 버튼·다이얼로그 추가 (다행: 시작/종료일, 시간, 색상·장소 select, 제목)
- 기간 확장 로직을 `expandScheduleDateRange`로 추출해 단일 저장(`submitSchedule`)과 공유
- 제목 있는 행만 저장, 시작===종료면 하루 일정, `saveLocal` + `render`

### 확인
- [x] 스위트 빌드 포함
- [ ] 방송실에서 일괄 입력 실기
- [ ] NAS 동시 접속 (해당 시)

## 2026-09-08 — 스위트 1.0.1.1 배포 빌드

- **스위트:** `1.0.1.1_20260908`
- **대상:** 전 앱 + 메타 레포
- **유형:** 배포

### 내용
- 첫 빌드(1.0.0.1) 이후 첫 수정분 버전 `1.0.1.1`
- 통합 zip: `Builded\BroadcastingApp_1.0.1.1_20260908.zip`

### 확인
- [x] `Build-BroadcastApps.ps1 -Bump Build` (version 1.0.1)
- [ ] 각 앱 GitHub push

## 2026-09-08 — 방송실 TODO 일괄 처리 (높음·명확 보통)

- **스위트:** `—` (재빌드 권장)
- **대상:** CtrlOne, FileChecker, WorkLog, ScheduleDataManager, ScheduleReader, 빌드스크립트, 문서
- **유형:** 수정 | 기능 | 문서

### 내용
- CtrlOne: DeviceStore/PresetStore 잠금·원자 저장, 슬롯 전환 UI 제거·파싱 분리(슬롯1), IP 변경, 로그 접두사, goodbye 8초
- FileChecker: Recording 일정 JSON 동기화, 단일 실행·아이콘, 브라우저 자동 오픈
- WorkLog: 오늘 이후 날짜 목록 숨김
- SDM: 바로 연결하고 시작(autoEnter)
- ScheduleReader Setup: py launcher + 한글 배포 안내
- `docs/DEPLOY-CHECKLIST.md` (NAS 계정·포트)

### 확인
- [x] CtrlOne / FileChecker Release 빌드
- [ ] 방송실 장비로 슬롯1·IP 변경 실기 확인
- [ ] FileChecker Recording 동기화 실기 확인

- **스위트:** `1.0.0.1_20260908`
- **대상:** 빌드스크립트, 문서, 전 앱 배포 산출물
- **유형:** 배포 | 기능 | 문서

### 내용
- `scripts\Build-BroadcastApps.ps1` / `Build-BroadcastApps.bat` — 5개 앱 일괄 빌드
- `broadcast-suite.version.json` — 스위트 버전·build 번호 관리
- 통합 패키지: `Builded\BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip`
  - 압축 해제 시 각 앱 portable 폴더 + `VERSION.txt` / `README.txt`
- `Builded\LATEST.txt`, `Builded\versions\`, `Builded\manifest.json`, `BUILD-INFO.md`
- ScheduleDataManager `package-portable.ps1` 기본 출력을 `Builded\ScheduleDataManager`로 통일

### 확인
- [x] `Build-BroadcastApps.ps1` 전체 빌드 성공
- [x] 통합 zip 생성 (`BroadcastingApp_1.0.0.1_20260908.zip`)

---

## 2026-09-08 — NAS 동시 접속 / CtrlOne 안정화

- **스위트:** `—` (이후 `1.0.0.1` 빌드에 포함)
- **대상:** ScheduleDataManager, WorkLog, FileChecker, CtrlOne
- **유형:** 수정

### 내용
- **ScheduleDataManager / WorkLog:** 연결 실패 시 `ForceClearServerSessions`·호스트 전체 `net use /delete` 제거. 형제 앱·탐색기 SMB 세션 보호. 1219 안내에 동일 계정 사용 권고.
- **FileChecker:** NAS 테스트에서 1219/85/2404 시 기존 세션으로 경로 접근 재시도 (세션 강제 해제 없음).
- **CtrlOne:** 단일 인스턴스 mutex, TCP 명령 쓰기 직렬화, 전송 실패 전파, 연결 타임아웃(4초).

### 확인
- [x] 관련 프로젝트 Release 빌드
- [ ] 방송실 PC에서 3앱 동시 NAS 접속 실사용 확인

---

## 2026-09-08 — 상위 문서·프로젝트 개요

- **스위트:** `—`
- **대상:** 문서
- **유형:** 문서

### 내용
- `PROJECTS.md` — 폴더 역할, NAS 경로·SMB 규칙, 포트, 일괄 빌드 안내 정리
- 본 수정 사항 문서(`CHANGES.md`) 추가

### 확인
- [x] 문서 작성

---

## 2026-09-08 — 메타 레포 broadcasting-suite

- **스위트:** `—`
- **대상:** 문서, git
- **유형:** 배포 | 문서

### 내용
- GitHub 메타 레포 구성: 문서·TODO·일괄 빌드만 추적, 앱 소스·`Builded`는 gitignore
- `README.md`, `.gitignore` 추가

### 확인
- [x] 로컬 git init·초기 커밋
- [x] GitHub `WooNaBin/broadcasting-suite` 생성·push (`main`)
