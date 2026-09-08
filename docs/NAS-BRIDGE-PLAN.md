# NAS 형제 앱 — 통합 브리지 계획

상태: **1차 구현됨** (2026-09-08) — `BroadcastNasBridge/`  
대상: ScheduleDataManager · WorkLog · FileChecker  
제외: CtrlOne · ScheduleReader (NAS 무관)

실행: `BroadcastNasBridge/scripts/launch-macos.sh` 또는 `dotnet run --project BroadcastNasBridge`  
설정: `~/Library/Application Support/BroadcastNasBridge/nas.json` (Windows: `%AppData%\BroadcastNasBridge\nas.json`)

관련: [PROJECTS.md](../PROJECTS.md) · TODO `#90`

---

## 1. 목표

같은 NAS·같은 계정을 쓰는 세 앱의 **로컬 백엔드(SMB/마운트)를 하나의 프로세스**로 모은다.

- OS별 **얇은 런처** → 브리지 기동 → **시작 인덱스(사이트맵)**
- 인덱스에서 **NAS 로그인·경로 설정을 한 번**
- 이후 각 기능 UI는 브리지 API만 사용 (앱별 재로그인 없음)

---

## 2. 확정 결정

| 항목 | 결정 |
|------|------|
| 수명 | 시작 인덱스·기능 탭(세션)이 **모두** 닫히면 **5초** 후 브리지 종료 (CtrlOne UiSessionGuard와 유사, grace=5s) |
| 경로 마법사 | 기존 SDM/WL/FC 기본·기억 경로를 참고해 **스마트 프리필** (아래 §4) |
| 미연결 진입 | 사이트맵에서 앱 클릭 시 미연결이면 **마법사로 보냄** (연결 후 원래 앱으로 복귀 가능하면 더 좋음) |
| 구 포트 | **단일 포트만 listen** (제안 `17820`). 구 포트(17821/17822/5187)는 **별도 상시 바인딩하지 않음** — 안정·성능·단일 인스턴스에 유리. 런처·인덱스가 정식 진입점. 필요 시 마이그레이션 안내용 정적 안내만 |
| 자격 증명 | NAS **계정 1세트** |
| 마운트 | 브리지가 소유. **공유별 최대 2** (`Temp DATA`, `Permanent DATA`) — UI마다 신규 mount 금지. “로그인 1회 + 마운트 캐시” |
| UI 통합 | **1단계에서는 UI 분리 유지** (경로만 브리지 기준). SPA 합치기는 후순위 |
| 범위 밖 | CtrlOne, ScheduleReader |

### 구 포트 선택 이유 (스마트 결정)

- **한 프로세스·한 listen**이 localhost에서 가장 단순하고, 포트 충돌·방화벽·헬스체크가 쉬움.
- 구 포트 3개를 프록시로 유지하면 코드·테스트·실패 지점이 늘고, “어느 주소가 정본인가”가 다시 갈라짐.
- 방송실 진입은 **런처 더블클릭**이 주 경로라 북마크 호환 우선순위가 낮음.
- (선택, 후순위) 전환기 짧게만: 구 포트에서 `302 → http://127.0.0.1:17820/...` 하는 **호환 모드 플래그**. 기본 OFF.

---

## 3. 런타임 구조

```text
[Launcher Win / Launcher Mac]
        │  mutex: 브리지 단일 인스턴스
        │  health 실패 시 브리지 기동
        ▼
BroadcastNasBridge  (:17820)
        │
        ├─ GET  /                 시작 인덱스 (사이트맵)
        ├─ /setup/*               NAS 마법사
        ├─ /schedule/*  + /api/schedule/*     ← SDM UI·API
        ├─ /worklog/*   + /api/worklog/*      ← WorkLog UI·API
        └─ /files/*     + /api/files/*        ← FileChecker UI·API
        │
        ▼  SMB (계정 1, 공유 마운트 캐시)
       NAS
```

### 세션·수명

- SignalR 또는 짧은 heartbeat/`/api/goodbye` + 페이지 `pagehide` (CtrlOne 패턴).
- “활성 UI 세션 수 = 0”이 **5초** 지속되면 `StopApplication`.
- 런처 재실행 시: 이미 떠 있으면 **새 프로세스 없이** 브라우저만 인덱스 오픈.

---

## 4. 경로 마법사 (스마트 프리필)

한 화면(또는 짧은 스텝)에서:

| 필드 | 기본 후보 (기존 앱 관례) |
|------|-------------------------|
| NAS IP | 기존 로컬 설정/prefs에 저장된 IP 중 공통값 |
| 계정/비밀번호 | 기억하기 값 (있으면) — 평문 저장 정책은 현행과 동일 수준으로 명시 |
| Temp 공유명 | `Temp DATA` |
| 스케줄 루트 | `Temp DATA\_data` (또는 `_data`) |
| 일지 루트 | `Temp DATA\_data\_work_log` |
| Permanent 공유명 | `Permanent DATA` |
| 미디어 스캔 | `Permanent DATA\H264_mp4 DATA` |
| (선택) 스케줄 JSON | SDM이 쓰는 JSON 파일명 — FC 동기화용 |

동작:

1. 브리지 로컬 `nas.json`(가칭)에 저장.
2. Temp / Permanent 필요 시 각각 마운트·연결 후 **테스트 버튼**.
3. 성공 시 사이트맵 활성화.
4. 기존 앱 설정 파일이 디스크에 있으면 **읽어와 프리필** (마이그레이션 도움).

---

## 5. 시작 인덱스 (사이트맵)

- 연결 상태 배지: 연결됨 / 미연결 / 오류.
- 카드: 스케줄 · 작업일지 · 파일체크 · (링크만) CtrlOne·ScheduleReader는 “별도 실행” 안내 가능.
- 미연결 + 앱 클릭 → `/setup?next=/schedule/` 등.
- 설정 다시 열기 진입점.

---

## 6. 구현 단계 (권장 순서)

### Phase 0 — 문서·골격 (짧음)

- [ ] 본 계획 확정 반영 (이 문서 · TODO `#90`)
- [ ] 레포/폴더 이름 결정: 예) `BroadcastNasBridge` 또는 SDM LocalBridge 승격

### Phase 1 — 브리지 코어

- [ ] 단일 포트 `17820`, mutex 단일 인스턴스
- [ ] health `/api/health`
- [ ] 세션 가드 + **5초** grace 종료
- [ ] NAS 연결 모듈 통합 (Win WNet / Mac mount_smbfs, 공유 캐시, File exists·1219 대응 이관)
- [ ] `nas.json` 스키마

### Phase 2 — 시작 인덱스 + 마법사

- [ ] `/` 사이트맵 UI
- [ ] `/setup` 마법사 + 스마트 프리필 + 미연결 가드
- [ ] OS별 얇은 런처 (Win `.exe`/`.bat`, Mac `.app` 또는 portable + 시작 스크립트)

### Phase 3 — 기능 API·UI 이관

- [ ] SDM API/정적 UI → `/schedule` + `/api/schedule`
- [ ] WorkLog → `/worklog` + `/api/worklog`
- [ ] FileChecker → `/files` + `/api/files`
- [ ] 기존 단독 exe는 “레거시”로 유지 기간 정하거나 스위트에서 런처만 권장

### Phase 4 — 스위트·배포

- [ ] `Build-BroadcastApps`에 브리지+런처 포함, Mac/Windows 산출물
- [ ] DEPLOY-CHECKLIST · PROJECTS 포트 표 갱신 (`17820` 중심)
- [ ] 방송실 스모크: 런처 → 마법사 → 세 앱 → 탭 전부 닫기 → 5초 후 종료

---

## 7. 비목표 (당분간)

- 세 UI를 하나의 SPA로 완전 병합
- CtrlOne/ScheduleReader를 같은 브리지에 포함
- 구 포트 상시 멀티 리스너
- 클라우드/원격 NAS 게이트웨이

---

## 8. 성공 기준

- 방송실에서 **런처 1회**로 세 기능 사용
- NAS 계정 입력 **세션당 1회**
- Windows 1219 / Mac File exists가 **브리지 한곳에서만** 다뤄짐
- 모든 관련 탭 종료 후 **약 5초 내** 브리지 프로세스 종료
- 구 단독 포트 없이도 운영 가능
