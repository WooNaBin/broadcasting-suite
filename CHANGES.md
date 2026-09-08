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
