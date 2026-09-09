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

## 2026-09-09 — Mac용 빌드 래퍼(.command) 추가

- **스위트:** —
- **대상:** 빌드스크립트 / 문서
- **유형:** 기능

### 내용
- Windows `Build-BroadcastApps.bat` / `Configure-BroadcastBuild.bat` 대응으로 macOS Finder용 `Build-BroadcastApps.command`, `Configure-BroadcastBuild.command` 추가
- `config`·`menu` 인자, 폴더 선택(osascript), Enter로 창 닫기 — `scripts/Build-BroadcastApps.sh` 호출
- `PROJECTS.md` / `README.md`에 Mac 빌드 진입점 안내 반영

### 확인
- [x] `./Build-BroadcastApps.command --show-config` 스모크
- [ ] Finder 더블클릭
- [ ] 전체 빌드

## 2026-09-09 — WL 브리지 진입 시 NAS 로그인 화면·연결 무반응 수정

- **스위트:** `—` (브리지·WorkLog UI 핫픽스, 재배포 전)
- **대상:** WorkLog / BroadcastNasBridge
- **유형:** 수정
- **TODO:** `#123`

### 내용
- WorkLog: `/worklog` 호스팅 시 `apiBase`를 단독 `17822`로 되돌리지 않음 → 연결 버튼 복구
- 브리지 NAS가 이미 연결됐으면 설정/로그인 화면 건너뛰고 프로필·일지로 진입
- 브리지 `/worklog/api/status` 바인딩 예외 흡수 · 미바인드 시 빈 `connect`로 store 연결
- `sync-ui`로 `wwwroot/worklog` 반영 · Bridge Release 빌드 확인

### 확인
- [x] `dotnet build` BroadcastNasBridge
- [x] `sync-ui.ps1`
- [ ] 브리지 재시작 후 `/worklog` 실기

## 2026-09-09 — 스위트 `1.1.0.12_20260909` 배포

- **스위트:** `1.1.0.12_20260909`
- **대상:** BroadcastNasBridge / ScheduleDataManager / WorkLog / FileChecker / 문서
- **유형:** 배포

### 내용
- SDM: 월별·기본보기·명단·공문 줌 · 인트로 레이아웃
- WL: 인쇄 직인란 NAS 설정 · 인쇄 배경·줄 앞 동그라미
- 문서: `docs/REMOTE-ACCESS.md` 외부·모바일 접속 가이드
- 브리지 FileChecker 스케줄 매칭(Praise/Streaming·고화질) 반영
- 통합 zip: `Builded\BroadcastingApp_1.1.0.12_20260909.zip`

### 확인
- [x] `Build-BroadcastApps.ps1` 빌드
- [ ] 설치·실행 스모크

## 2026-09-09 — SDM 인트로 레이아웃 · WL 인쇄 직인/디자인

- **스위트:** `1.1.0.12_20260909`
- **대상:** ScheduleDataManager / WorkLog / BroadcastNasBridge
- **유형:** 개선

### 내용
- SDM: 월일정~수정 버튼을 「방송실 일정」 타이틀 오른쪽으로 · 공유사항 폭 축소
- SDM: 타이틀 아이콘 불투명·2배 · 텍스트를 아이콘 위에 겹쳐 좌측 배치
- WL: 인쇄 설정 화면 기어 → 직인란 이름 편집 · NAS `print-signs.json` 저장
- WL: 인쇄 업무내용 배경 흰색 · 각 줄 앞 작은 동그라미

### 확인
- [x] 로컬 빌드
- [ ] NAS 동시 접속 (해당 시)
- [x] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — 외부·모바일 접속 가이드 문서

- **스위트:** `1.1.0.12_20260909`
- **대상:** 문서
- **유형:** 문서

### 내용
- `docs/REMOTE-ACCESS.md` — 상시 PC vs 시놀로지, VPN vs HTTPS, 어디서나·다기기 로드맵·용어 설명
- `PROJECTS.md` 링크 · TODO `#121` 아이디어

### 확인
- [x] 문서 작성
- [ ] 실제 VPN/상시 PC 도입 (운영)

## 2026-09-09 — SDM 월별·기본보기·명단·공문 줌

- **스위트:** `1.1.0.12_20260909`
- **대상:** ScheduleDataManager / BroadcastNasBridge
- **유형:** 개선

### 내용
- 월별 보기: 시간 미설정 시 「시간미정」 생략 · 일정 클릭은 날짜 상세 UI만 (수정은 상세에서)
- 기본 보기: 시작·종료 날짜 구간 필터 · 날짜별 접기/펼치기(진입 시 오늘 이전 접힘)
- 강사 설정·버튼: 「명단」으로 통합 · 외부 명단 제거(기존 외부 이름은 명단으로 병합)
- 일정 추가/수정 창 제목 옆 「공문 보기」 · 공문/월 일정 이미지 확대·축소·맞춤

### 확인
- [ ] 로컬 실행
- [ ] NAS 동시 접속 (해당 시)
- [ ] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — SDM 강사 목록 순서·광주/외부 `#10` `#11`

- **스위트:** `—` (재배포 시 브리지 `sync-ui`로 `/schedule` 반영)
- **대상:** ScheduleDataManager / BroadcastNasBridge
- **유형:** 수정

### 내용
- 강사 설정 ↑↓로 순서 변경 · 추가 시 가나다 강제 정렬 제거
- 일정 추가「광주」「외부」선택 팝오버를 모달 다이얼로그 안으로 이동(가려지던 문제 수정)

### 확인
- [x] `sync-ui.ps1`
- [ ] 설정에서 강사 ↑↓ · 일정 추가에서 광주/외부 선택

---

## 2026-09-09 — TODO #31·#30·#10·#40·#41

- **스위트:** `—`
- **대상:** WorkLog / ScheduleDataManager / FileChecker / BroadcastNasBridge
- **유형:** 수정 | 기능

### 내용
- WL: 작성 중 날짜 전환 시 미저장 초안 폐기·저장본 복원 · 삭제/저장 버튼 왼쪽
- SDM: 색상 프리셋 칩에 색 표시
- FC/브리지: `_숫자M` 고화질 검색 · preparation Praise/Streaming → 특송/YouTube

### 확인
- [ ] 로컬 실행
- [ ] NAS 동시 접속 (해당 시)
- [ ] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — 스위트 배포 `1.1.0.9_20260909`

- **스위트:** `1.1.0.9_20260909`
- **대상:** BroadcastNasBridge / CtrlOne / FileChecker / ScheduleDataManager / ScheduleReader / WorkLog / 빌드스크립트 / 문서
- **유형:** 배포

### 내용
- 일괄 빌드·번들 zip: `Builded\BroadcastingApp_1.1.0.9_20260909.zip`
- 포함: SR 엑셀 추출 전환, SDM 강사목록·일요일 칸, WorkLog 인쇄됨·테마 등 당일 변경

### 확인
- [x] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — ScheduleReader 엑셀 추출로 전환

- **스위트:** `1.1.0.9_20260909`
- **대상:** ScheduleReader / 빌드스크립트
- **유형:** 기능 | 기타

### 내용
- 이미지 OCR·격자 UI·관련 소스 제거
- 브라우저: 일정 엑셀 불러오기 → 추출 → JSON 내보내기
- openpyxl 헤더 표(`날짜`/`제목`/…) 추출 → SDM `schedule-data.json`
- 배포 패키지에서 OCR `models/` 복사 중단

### 확인
- [x] 로컬 스모크(표 샘플 추출)
- [ ] 실제 월간 일정 엑셀로 추출·SDM import
- [x] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — SDM 강사 목록(광주/외부) · 설정

- **스위트:** `1.1.0.9_20260909`
- **대상:** ScheduleDataManager
- **유형:** 기능

### 내용
- 강사명 입력란을 좁히고 PA 옆에 **광주·외부** 버튼 추가(목록에서 선택 시 이름 입력)
- 「일정 선택(내보내기)」 옆 톱니 설정 → **강사 목록** 탭에서 광주/외부 각각 저장(localStorage)

### 확인
- [ ] 로컬 실행
- [ ] NAS 동시 접속 (해당 시)
- [ ] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — SDM 달력 일요일 칸 가로 확대

- **스위트:** `1.1.0.9_20260909`
- **대상:** ScheduleDataManager
- **유형:** 수정

### 내용
- 달력 보기에서 일요일 열(헤더·셀)만 가로 약 1.2배

### 확인
- [ ] 로컬 실행
- [ ] NAS 동시 접속 (해당 시)
- [ ] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — WorkLog 인쇄됨 저장 · 오늘 버튼 · 테마 톤 맞춤

- **스위트:** `1.1.0.9_20260909`
- **대상:** WorkLog / BroadcastNasBridge(worklog UI 동기화)
- **유형:** 기능 | 수정

### 내용
- 인쇄 성공(`afterprint`) 후 해당일·동반일 `isPrinted=true` 저장
- 「오늘」 버튼을 날짜 라벨과 같은 줄 오른쪽으로 이동
- 다크 초록 톤을 SDM과 같은 밝은 배경·오렌지 액센트로 맞춤

### 확인
- [ ] 로컬 실행
- [ ] NAS 동시 접속 (해당 시)
- [ ] `Build-BroadcastApps.bat` 빌드

## 2026-09-09 — SDM 색상 직접선택 · 다수추가 기간·스와치

- **스위트:** `1.1.0.9_20260909`
- **대상:** ScheduleDataManager
- **유형:** 기능 | 수정

### 내용
- 일정 추가 색상에 **직접** 색상 선택(`type=color`, hex 저장)
- 다수 추가: **시작일·종료일**(기간이면 날짜별 생성) · 색상 **스와치**
- 기본 **2행** ·「1행 추가」만 유지(5행 추가 제거)

### 확인
- [ ] 단일/다수에서 색상 스와치·직접 선택
- [ ] 다수 추가 기간 행 → 여러 날짜 생성

---

## 2026-09-09 — SDM 다수 추가(직접 작성) UX 보강

- **스위트:** `—`
- **대상:** ScheduleDataManager / BroadcastNasBridge
- **유형:** 수정

### 내용
- 다수 추가 = **표에 여러 일정을 직접 입력** (JSON 다중 선택 아님) 안내 명시
- 열 순서: 날짜 → **제목** → 시간 → 장소 → 강사 → 색상 → 준비
- 제목 Enter → 다음 행 · 하단 일괄 저장 고정 · SW `v18`

### 확인
- [ ] 일정 추가 → 다수 추가 → 여러 행 작성 → 일괄 저장

---

## 2026-09-09 — SDM intro 레이아웃 복구 (월 일정 버튼)

- **스위트:** `—` (재배포 또는 `sync-ui` 후 브리지 재시작)
- **대상:** ScheduleDataManager / BroadcastNasBridge
- **유형:** 수정

### 내용
- 광주교회 n월 일정 버튼 추가로 intro 우측 칸이 좁아져 공유사항 줄·버튼이 찌그러진 문제 수정
- 제목/공유사항 줄을 intro 전체 폭으로 배치 · 월 일정 버튼 폭 약간 확대

### 확인
- [x] `sync-ui.ps1`
- [ ] `/schedule`에서 광주교회 n월 일정 · 오늘 일정 · 공유사항 한 줄로 보이는지

---

## 2026-09-09 — WorkLog 날짜별 작성 잠금 `#115`

- **스위트:** `—` (재배포 시 브리지 `sync-ui`로 `/worklog` 반영)
- **대상:** WorkLog / BroadcastNasBridge
- **유형:** 기능

### 내용
- 일지 기본 읽기 전용. **일지 작성/수정**으로만 편집 모드 진입
- 잠금은 NAS 공유 폴더 `locks/YYYY-MM-DD.json` (로컬 브리지가 각각 읽기/쓰기). heartbeat 15초 · TTL 45초
- 다른 단말: 「{이름}님이 작성 중입니다」· 버튼 시 작성 불가 안내
- 저장·작성 취소·날짜 이동·페이지 종료(`pagehide`/`beforeunload`/sendBeacon) 시 잠금 해제. 비정상 종료는 TTL로 회수

### 확인
- [x] LocalBridge / MacBridge / nas-api / BroadcastNasBridge 빌드
- [x] `sync-ui.ps1`
- [ ] 두 PC에서 같은 날짜 작성 잠금·저장 후 해제
- [ ] 작성 중 탭 종료 후 약 1분 내 다른 PC 진입

---

## 2026-09-09 — SDM 공문 업로드 파일명 YYYYMMDD 보정

- **스위트:** `—`
- **대상:** ScheduleDataManager / BroadcastNasBridge
- **유형:** 수정

### 내용
- 공문 업로드 시 원본 파일명 유지. 8자리 날짜(`YYYYMMDD`)로 시작하지 않으면 일정 날짜를 앞에 붙여 저장
- 예: `행사.jpg` + 일정 `2026-09-09` → `20260909_행사.jpg`
- 동일 이름 충돌 시 `_01` 등으로 유일화 · 업로드 토스트에 저장 파일명 표시

### 확인
- [ ] 날짜 없는 파일 업로드 → `YYYYMMDD_` 접두 확인
- [ ] 이미 `20260909_…`인 파일은 접두 중복 없이 유지

---

## 2026-09-09 — SDM 공문 연결 목록 필터

- **스위트:** `—`
- **대상:** ScheduleDataManager
- **유형:** 수정

### 내용
- 공문 연결 드롭다운: `YYYYMMDD`(8자리)로 시작하는 파일만 표시
- 일정 날짜 기준 앞뒤 2개월 밖·날짜 없는 파일은 목록에서 제외 (월 일정 `yyyymm_광주교회일정_*` 등 제외)

### 확인
- [ ] 일정 날짜 근처 8자리 공문만 드롭다운에 보이는지
- [ ] `202609_광주교회일정_01` 등 6자리·월 일정 이미지는 연결 목록에 안 나오는지

---

## 2026-09-09 — SDM 월 일정 이미지 · 다수 추가 `#114`

- **스위트:** `—` (재배포 시 브리지 `sync-ui`로 `/schedule` 반영)
- **대상:** ScheduleDataManager / BroadcastNasBridge
- **유형:** 기능

### 내용
- 오늘 일정 왼쪽에 **광주교회 {m}월 일정** 버튼(캘린더 월 기준). 공문 폴더 `_official_documents`에서 `yyyymm_광주교회일정_nn` jpg/png 열어 보기. 없으면 토스트 안내
- 일정 추가 **다수 추가** 탭 표기·레이아웃 보강(넓은 표·날짜 고정·날짜 채우기·기본/추가 5행)

### 확인
- [x] `sync-ui.ps1`
- [ ] NAS에 `202609_광주교회일정_01.jpg` 등 두고 해당 월에서 열기
- [ ] 다수 추가 → 여러 행 일괄 저장

---

## 2026-09-09 — WorkLog 로그 IP·오늘·제목규칙 우선순위 `#113`

- **스위트:** `—` (재배포 시 브리지 `sync-ui`로 `/worklog` 반영)
- **대상:** WorkLog / BroadcastNasBridge
- **유형:** 기능

### 내용
- 감사(로그) 기록에 요청 IP 포함(로컬이면 PC LAN IP) · 로그 모달에 `IP …` 표시
- 선택 날짜 오른쪽에 **오늘** 버튼(해당 월로 이동·선택)
- 일지 입력란 아래, 선택일이 오늘이 아니면 「오늘 날짜가 아닌 일지입니다」 안내
- 기본 일정 기록 시 제목 규칙은 위부터 첫 일치만 적용 · 설정 UI에 ↑↓ 우선순위

### 확인
- [x] `dotnet build -c Release` WorkLog LocalBridge / nas-api / BroadcastNasBridge
- [x] `sync-ui.ps1`
- [ ] 기록 규칙: `대집회`가 `집회`보다 위일 때 「3월대집회」→ 대집회만
- [ ] 로그 저장 후 IP 표시

---

## 2026-09-09 — TODO `#110` `#111` `#112`

- **스위트:** `—` (재배포 권장)
- **대상:** BroadcastNasBridge / FileChecker / ScheduleDataManager / 빌드스크립트
- **유형:** 수정 | 기능

### 내용
- `#110` FC·브리지 JSON 선택을 SDM과 동일 규칙으로 맞춤 + SDM 연결 파일을 `ScheduleJsonFile`로 저장
- `#111` `Build-BroadcastApps.ps1`/`.sh` 완료 시 산출 폴더 자동 열기
- `#112` 공문 보기 원본 비율·페이지(넘버링) · 연결 리스트 그룹 1개 · 일정 ±2개월 필터

### 확인
- [x] `dotnet build -c Release` BroadcastNasBridge
- [x] `dotnet build -c Release` FileCheckerFinder
- [x] `sync-ui.ps1`
- [ ] NAS 실기: SDM 연결 파일 = FC Recording 목록
- [ ] 공문 다장 넘김·연결 필터

---

## 2026-09-09 — SDM 일정 복수추가 탭 `#109`

- **스위트:** `—` (재배포 시 브리지 `sync-ui`로 `/schedule` 반영)
- **대상:** ScheduleDataManager
- **유형:** 기능

### 내용
- 일정 추가 다이얼로그 모드에 **복수추가** 탭 추가 (단일 날짜 / 기간 입력 / 복수추가)
- 표 형식 일괄 입력: 날짜·시간·색상·장소·제목·강사·준비 체크 · 행 추가/삭제 · 일괄 저장
- 수정 시 복수추가 탭 숨김 · 복수추가 모드에서 다이얼로그 폭 확대

### 확인
- [ ] SDM에서 복수추가 → 여러 행 저장 후 보드 반영
- [ ] 브리지 `/schedule` (재시작 또는 `sync-ui`) 동일 UI

---

## 2026-09-09 — WorkLog CSS · FileChecker SDM Recording

- **스위트:** `—` (재배포 권장)
- **대상:** BroadcastNasBridge
- **유형:** 수정

### 내용
- WorkLog: `/worklog/styles.css`·`/worklog/app.js`·`/worklog/templates.js` 절대 경로 + `/worklog`→`/worklog/` 리다이렉트 (상대경로 CSS 미적용 수정)
- FileChecker: 자체 `schedules.json` 대신 **SDM 공유 JSON**의 `preparation`∋Recording 만 작업 목록으로 자동 로드
- 브리지 UI: 수동 스케줄 등록/가져오기 안내 변경, SDM 동기화 버튼

### 확인
- [x] `dotnet build -c Release`
- [ ] 브리지 `/worklog/` 스타일
- [ ] NAS 연결 후 `/files/` Recording 목록

---

## 2026-09-09 — 빌드 피드백 `#100`–`#108`

- **스위트:** `—` (재배포 권장)
- **대상:** BroadcastNasBridge / Install / SDM / WorkLog / 빌드스크립트
- **유형:** 수정 | 기능

### 내용
- `#100` 아이콘: `assets/icons` → 각 앱 assets · Bridge ApplicationIcon
- `#101` `#102` 바로가기 한글명 + 바탕화면「방송실 프로그램」폴더
- `#105` 일정·일지·파일체크 바로가기가 Bridge exe + `/schedule|/worklog|/files` (레거시 exe 아님)
- `#103` 시작 페이지: NAS 연결됨 / 설정 / 스케줄 / 일지 / 파일체크만
- `#104` NAS IP 직접입력 + 네트워크 검색(`/api/lan-devices`)
- `#106` 브리지 연결 시 스케줄 로그인 스킵
- `#107` WorkLog CSS·JS 상대경로 (`styles.css` / `./templates.js`)
- `#108` FileChecker Recording 동기화 — SDM 배열·`scheduleDate`/`scheduleTitle` 매핑

### 확인
- [x] `dotnet build -c Release` BroadcastNasBridge
- [ ] `Build-BroadcastApps.bat` 재배포 후 설치·바로가기 확인
- [ ] NAS 실기: 시작 페이지·검색·스케줄/일지/파일체크

---

## 2026-09-09 — 빌드 산출 경로 설정 `#1`

- **스위트:** `—`
- **대상:** 빌드스크립트 / 문서
- **유형:** 기능

### 내용
- `broadcast-suite.build.json`에 산출 폴더·대상 OS를 기록 (예시: `broadcast-suite.build.example.json`)
- `Configure-BroadcastBuild.bat` / `Build-BroadcastApps.bat config` 로 폴더 선택
- Windows·Mac 스크립트가 같은 JSON을 읽고, `-Target Host|Windows|Mac|All` 로 OS별 게시
- Windows 일괄 빌드에 `BroadcastNasBridge` 포함, `sync-ui.ps1` 추가
- WorkLog·ScheduleDataManager·FileChecker의 `D:\Projects\Builded` 하드코딩 제거
- `Build-BroadcastApps.bat`는 ASCII만 사용 (cmd UTF-8 한글 줄 깨짐으로 ExecutionPolicy가 쪼개지던 문제)
- `Install-BroadcastApps.ps1` UTF-8 BOM + 콘솔 UTF-8 — Windows 설치 메시지 한글 깨짐 수정
- 설치 폴더는 빌드 경로와 같이 폴더 선택 창으로 지정 (직접 입력 없음, 취소 시 기본 위치)

### 확인
- [x] `Build-BroadcastApps.ps1 -ShowConfig` 경로 해석
- [ ] 전체 `Build-BroadcastApps.bat` 재배포 (요청 시)

---

## 2026-09-08 — NAS 통합 브리지 `#90` 구현

- **스위트:** `—` (브리지 포함 재배포는 별도)
- **대상:** BroadcastNasBridge / 빌드스크립트 / 문서
- **유형:** 기능

### 내용
- 새 프로젝트 `BroadcastNasBridge` (`http://127.0.0.1:17820`)
- 단일 인스턴스 mutex · UI 세션 5초 grace · `nas.json` · Temp/Permanent SMB 캐시
- 시작 인덱스·경로 마법사 · 미연결 시 setup 유도
- SDM/WL/FC UI를 `/schedule` `/worklog` `/files`로 서빙, API 동일 프로세스
- `scripts/sync-ui.sh` · `package-portable-macos.sh` · `Build-BroadcastApps.sh` 연동

### 확인
- [x] `dotnet build -c Release` (net10.0)
- [x] `/api/health` · 허브 · setup · `/schedule/` `/worklog/` `/files/` HTTP 200
- [ ] 방송실 NAS 실기 연결·세 앱 동시 사용
- [ ] `Build-BroadcastApps` 전체 재배포

---

## 2026-09-08 — NAS 통합 브리지 계획 문서화

- **스위트:** `—`
- **대상:** 문서
- **유형:** 문서

### 내용
- SDM·WorkLog·FileChecker 통합 NAS 브리지 계획 확정·문서화 (`docs/NAS-BRIDGE-PLAN.md`)
- 결정: 세션 전부 종료 후 5초 grace · 경로 마법사 스마트 프리필 · 미연결 시 마법사 · 단일 포트 `17820` · TODO `#90`

### 확인
- [x] 문서만 (구현 미착수)

---

## 2026-09-08 — 스위트 1.1.0.3 배포 빌드

- **스위트:** `1.1.0.3_20260908`
- **대상:** 전 앱 (Windows + Mac 포함분)
- **유형:** 배포

### 내용
- `#10`/`#30`/SDM Mac SMB/`FileChecker`·`CtrlOne` macOS 지원 반영 재빌드
- 통합 zip: `Builded/BroadcastingApp_1.1.0.3_20260908.zip`
- Mac: CtrlOne, FileChecker, ScheduleDataManager, WorkLog (arm64/x64)
- Windows: 5개 앱 전체

### 확인
- [x] `scripts/Build-BroadcastApps.sh Build`
- [ ] 방송실 설치·실행 스모크

## 2026-09-08 — SDM Mac SMB File exists 수정

- **스위트:** `—`
- **대상:** ScheduleDataManager
- **유형:** 수정

### 내용
- LocalBridge `ConnectMac`: 공유별 고정 마운트 경로, 마운트 전 디렉터리 정리, `umount -f` 재시도, Finder `/Volumes/<share>` 재사용 (FileChecker `#30`과 동일 패턴)

### 확인
- [x] macOS arm64 패키지 재빌드
- [ ] NAS `Temp DATA` 접속 실기

## 2026-09-08 — TODO #10 일괄 행추가 · #30 Mac NAS File exists

- **스위트:** `—`
- **대상:** ScheduleDataManager, FileChecker
- **유형:** 수정

### 내용
- `#10` 일괄 입력 다이얼로그: 하단 액션이 flex에 눌려 클릭 불가하던 문제 수정. 행 추가 시 스크롤·포커스
- `#30` FileChecker macOS SMB: 공유별 고정 마운트 포인트, 마운트 전 디렉터리 비우기, `File exists` 시 `umount -f` 재시도, Finder `/Volumes/<share>` 재사용

### 확인
- [x] FileChecker Release 빌드
- [ ] SDM 일괄 입력 행 추가 UI
- [ ] FileChecker Mac NAS 테스트

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
