# 레거시 코드·프로그램 정리 (#116)

상태: **2단계 반영** (2026-09-10) — 빌드 zip이 Bridge·CtrlOne·SR을 `Windows\`에, SDM/WL/FC를 `Windows\Legacy\`에 둔다.  
`-IncludeLegacy:$false` 로 레거시 제외 가능. 소스 삭제는 아직 하지 않음.

관련: [NAS-BRIDGE-PLAN.md](NAS-BRIDGE-PLAN.md) · [PROJECTS.md](../PROJECTS.md) · TODO `#116` `#124` `#61`

---

## 권장 vs 레거시

| 구분 | 포트 | 진입 |
|------|------|------|
| **권장** BroadcastNasBridge | **17820** | `/` · `/schedule` · `/worklog` · `/files` |
| 레거시 SDM | 17821 | `BroadcastingSchedule.exe` / Mac 단독 |
| 레거시 WorkLog | 17822 | `WorkLog.exe` · `WorkLog-macOS-*.app` |
| 레거시 FileChecker | 5187 | `FileCheckerFinder.exe` |

브리지가 떠 있으면 레거시 런처는 **브리지 URL로만 열고 자체 SMB 마운트를 하지 않도록** 유도한다 (Mac 다중 마운트 방지, `#3` `#30`).

---

## 삭제·유지 기준

**지금은 유지 (폴백)**

- `ScheduleDataManager/LocalBridge`, `WorkLog/LocalBridge`·`MacBridge`, `FileChecker` 단독 서버
- 스위트 zip: `Windows\Legacy\` (또는 `-IncludeLegacy:$false` 로 생략). 설치 바로가기는 브리지

**삭제 후보 (단계 진행 시, 백업·실기 후)**

| 후보 | 이유 |
|------|------|
| `WorkLog/nas-api/` | 브리지·원격 접근 문서와 역할 중복 |
| `WorkLog/_ref/` | 참고용 사본 |
| `WorkLog/scripts/DataFormats.cs` 등 느슨한 복사본 | 빌드 입력 아님 |
| 앱별 `publish/` · `FileChecker/deploy/` · 구 `Publish-Release.bat` 산출 | `Builded` 스위트로 대체됨 |
| `Builded` 오래된 `BroadcastingApp_*` 버전 폴더 | 최신만 유지 |

**삭제 금지 (당분간)**

- 브리지 `SmbConnection` / `NasService`
- `sync-ui` · 브리지 `wwwroot/{schedule,worklog,files}`
- CtrlOne · ScheduleReader (NAS 레거시와 무관)

---

## SMB 코드 중복 (통합은 후순위)

1. `BroadcastNasBridge/Services/SmbConnection.cs` ← **정본**
2. SDM `LocalBridge` 내장 `SmbConnection`
3. WorkLog `LocalBridge` / `MacBridge` 스토어
4. FileChecker `FileCheckerStore` Mac/Win NAS

---

## 단계 계획

| 단계 | 내용 | 상태 |
|------|------|------|
| **1** | 이 문서 + PROJECTS/체크리스트에 레거시=폴백 명시 | **완료** |
| **2** | 빌드 기본을 브리지 중심, 레거시는 `-IncludeLegacy` / `Windows\Legacy\` · Mac `Legacy\` | **완료** (`#124` `#61` `#62`) |
| **3** | 설치 시 레거시 폴더 복사 축소·격리 | 부분 (아이콘은 Legacy 경로 우선, 전체 복사는 유지) |
| **4** | `Builded`·앱 `publish` 산출 정리 | 미착수 |
| **5** | SMB 단일화 후 레거시 listen 포트 폐기 | 미착수 (실기 soak 후) |

Win·Mac 모두: 바로가기는 브리지, 레거시 exe/app는 “브리지 다운 시에만”.
