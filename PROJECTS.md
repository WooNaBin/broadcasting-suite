# Projects — 방송실 도구 모음

상위 폴더(이 **메타 레포** 루트) 아래에 여러 프로그램 소스가 형제 폴더로 있고, 배포 산출물은 `Builded`에 모은다.  
앱 소스는 각각 별도 GitHub 레포이며, 이 저장소는 문서·TODO·일괄 빌드만 관리한다. → [README.md](README.md)

| 문서 | 용도 |
|------|------|
| [README.md](README.md) | 메타 레포 소개·클론 배치 |
| [PROJECTS.md](PROJECTS.md) | 프로젝트 개요·NAS·포트·빌드 |
| [TODO.md](TODO.md) | **버그·개선 할 일** → 「방송실 TODO 해줘」로 일괄 작업 |
| [CHANGES.md](CHANGES.md) | 수정·배포 기록 |
| [docs/NAS-BRIDGE-PLAN.md](docs/NAS-BRIDGE-PLAN.md) | SDM·WL·FC 통합 NAS 브리지 설계 (`BroadcastNasBridge`) |
| [docs/REMOTE-ACCESS.md](docs/REMOTE-ACCESS.md) | 외부·모바일 접속 방법 (VPN / HTTPS, 상시 PC vs 시놀로지) |
| `broadcast-suite.version.json` | 스위트 버전·build 번호 |

### 커밋 메시지

한글 제목은 **체언형**으로 짧게 (`로그인 기억 추가` O · `추가한다` X).  
규칙: `.cursor/rules/commit-message-ko.mdc`

| 폴더 | 역할 |
|------|------|
| **BroadcastNasBridge** | **권장 진입점** — NAS 1회 연결 + 스케줄·일지·파일체크 (`17820`) |
| **ScheduleDataManager** | 방송실 스케줄 관리 (NAS JSON R/W) — 단독 `17821` 레거시 |
| **WorkLog** | 일일 작업 일지 — 단독 `17822` 레거시 |
| **FileChecker** | 렌더링 파일 체크 — 단독 `5187` 레거시 |
| **CtrlOne** | HyperDeck 등 방송 장비 TCP 제어 (NAS 무관) |
| **ScheduleReader** | 일정 엑셀 → JSON (로컬; NAS 세션 관리 없음) |
| **ImgToText** | 이미지→텍스트 보조 도구 |
| **Builded** | 배포용 빌드 산출물 |

---

## 형제 앱 (같은 NAS, 동시 실행)

**권장:** `BroadcastNasBridge` 한 프로세스가 SMB를 소유하고 `/schedule` · `/worklog` · `/files`를 연다.  
단독 exe(17821/17822/5187)는 레거시로 유지한다.

| 진입 | 기본 포트 | 기본 공유 경로 | NAS 역할 |
|------|-----------|----------------|----------|
| **BroadcastNasBridge** | `17820` | Temp + Permanent (마운트 캐시) | 계정 1 · 공유 ≤2 |
| ScheduleDataManager (레거시) | `17821` | `Temp DATA\_data` | 스케줄 JSON·공문·백업 R/W |
| WorkLog (레거시) | `17822` | `Temp DATA\_data\_work_log` | 일지 R/W, 상위 `_data` 스케줄 **읽기만** |
| FileChecker (레거시) | `5187` | `Permanent DATA\H264_mp4 DATA` | 미디어 파일 **열거/검색** |

```text
\\NAS\Temp DATA\_data\                 ← ScheduleDataManager R/W
\\NAS\Temp DATA\_data\*.json           ← WorkLog 스케줄 읽기
\\NAS\Temp DATA\_data\_work_log\       ← WorkLog 전용
\\NAS\Permanent DATA\H264_mp4 DATA\    ← FileChecker 스캔
```

### Windows SMB 규칙 (중요)

- Windows는 **서버(IP)당 자격 증명 한 세트**만 허용한다. 공유 폴더가 달라도 호스트가 같으면 충돌(오류 **1219**)이 난다.
- **운영 권장:** 세 앱·탐색기 모두 **같은 NAS 계정**을 쓴다.
- 앱 Disconnect / 종료 시 Windows에서는 **SMB 세션을 끊지 않는다** (로컬 연결 상태만 해제). macOS는 앱이 만든 마운트만 umount.
- **금지(수정됨):** 연결 실패 시 `net use \\host /delete` / `WNetCancelConnection2(force)`로 호스트 전체 세션을 지우는 동작.  
  → ScheduleDataManager·WorkLog에서 제거함. 형제 앱·탐색기 세션을 보호한다.
- FileChecker는 평소 UNC 읽기만 하고, NAS 테스트 시에만 `WNetAddConnection2`를 시도한다. 1219면 **기존 세션으로 경로 접근만 확인**하고 세션을 끊지 않는다.

### 자격 증명 충돌 시 사용자 조치

1. 모든 앱에서 동일 NAS 계정으로 맞춘다.  
2. 해당 NAS 탐색기 창을 닫는다.  
3. 필요 시: `net use \\NAS서버 /delete /y` 후 한 앱부터 다시 연결.

---

## CtrlOne (장비 제어)

- 포트: `http://127.0.0.1:5177`
- HyperDeck Ethernet Protocol TCP **9993**
- NAS/SMB와 무관. 브라우저 닫으면 프로세스 종료(UI 세션 가드).
- **단일 인스턴스** mutex (`Local\CtrlOne.Dashboard`) — 두 번째 실행 시 기존 UI만 연다.
- TCP 명령 쓰기 직렬화 + 전송 실패를 삼키지 않음 + 연결 타임아웃(4초).

### 검토에서 남은 개선 후보

| 우선 | 내용 |
|------|------|
| 중 | `DeviceStore` 파일 쓰기 잠금/원자적 저장 (`PresetStore`와 동일 패턴) |
| 중 | 장비 로그에 장비명 접두사 |
| 중 | UI goodbye 유예 시간(짧은 새로고침으로 전체 종료되는 경우) |
| 낮 | 수신 버퍼 상한, 슬롯 3개 이상 UI |

---

## 포트 한눈에

| 앱 | 포트 |
|----|------|
| **BroadcastNasBridge** | **17820** |
| ScheduleDataManager (레거시) | 17821 |
| WorkLog (레거시) | 17822 |
| ScheduleReader | 17823 |
| CtrlOne | 5177 |
| FileChecker (레거시) | 5187 (배포) / launchSettings는 개발용 |

겹치면 안 된다. WorkLog `docs/ports.md` 참고.

---

## 기술 스택 요약

| 앱 | 스택 |
|----|------|
| BroadcastNasBridge | ASP.NET Core · 통합 SMB · 허브/마법사 · `/schedule` `/worklog` `/files` |
| ScheduleDataManager | .NET LocalBridge + 임베드 웹(vanilla JS) |
| WorkLog | .NET LocalBridge / MacBridge + `www/` (+ 선택 `nas-api`) |
| FileChecker | ASP.NET Core + `wwwroot` (Windows + macOS portable) |
| CtrlOne | ASP.NET Core + SignalR + HyperDeck TCP |
| ScheduleReader | Python FastAPI + Excel(openpyxl) |

---

## 배포 · 최신화 (일괄 빌드)

레포가 어디에 있든, **그 머신·OS에서** 해당 OS 산출물을 뽑는다. 산출 폴더는 설정 파일에 기록한다.

| 방법 | 설명 |
|------|------|
| **`Build-BroadcastApps.bat`** | 더블클릭 → 저장된 경로로 전체 빌드 + 통합 zip |
| **`Configure-BroadcastBuild.bat`** / `Build-BroadcastApps.bat config` | 산출 폴더 선택 → `broadcast-suite.build.json`에 기록 |
| `Build-BroadcastApps.bat menu` | 경로·OS 선택 메뉴 후 빌드 |
| `scripts\Build-BroadcastApps.ps1` | Windows PowerShell (옵션 가능) |
| `scripts/Build-BroadcastApps.sh` | macOS/Linux (같은 JSON 설정) |
| `scripts\Show-BuildManifest.bat` | 마지막 빌드·버전 요약 |

**산출 경로 우선순위:** `-OutRoot` / `--out` → 환경 변수 `BROADCAST_BUILD_DIR` → `broadcast-suite.build.json` → `<레포>/Builded`  
예시 템플릿: `broadcast-suite.build.example.json` (실제 설정 파일은 git 무시)

버전 원본: `broadcast-suite.version.json` (빌드마다 `build` 번호 +1)

```powershell
# 전체 (개별 portable + 개별 zip + 통합 zip)
.\scripts\Build-BroadcastApps.ps1
# → <outRoot>\BroadcastingApp_1.0.0.1_20260908.zip

# 산출 경로 기록
.\scripts\Build-BroadcastApps.ps1 -Configure
.\scripts\Build-BroadcastApps.ps1 -SetOutRoot E:\Releases

# 이 PC OS만 / Windows+Mac (가능한 앱)
.\scripts\Build-BroadcastApps.ps1 -Target Host
.\scripts\Build-BroadcastApps.ps1 -Target All

# 마이너/메이저 버전 올림
.\scripts\Build-BroadcastApps.ps1 -Bump Minor
.\scripts\Build-BroadcastApps.ps1 -Version 1.2.0

# 일부만 / 통합 zip 생략
.\scripts\Build-BroadcastApps.ps1 -Apps CtrlOne,WorkLog
.\scripts\Build-BroadcastApps.ps1 -SkipBundle

# 마지막 빌드 정보
.\scripts\Build-BroadcastApps.ps1 -List
.\scripts\Build-BroadcastApps.ps1 -ShowConfig
```

```bash
# Mac
./scripts/Build-BroadcastApps.sh
./scripts/Build-BroadcastApps.sh --set-out ~/Releases --target all
./scripts/Build-BroadcastApps.sh --show-config
```

| 산출물 | 경로 |
|--------|------|
| 통합 배포 zip | `<outRoot>\BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip` |
| 최신 포인터 | `<outRoot>\LATEST.txt` |
| 버전 이력 | `<outRoot>\versions\*.json` |
| 앱별 portable | 아래 표 |

통합 zip 안에는 OS별 폴더와 설치 도우미가 들어 있다.

```
BroadcastingApp_<label>/
  Windows/          ← BroadcastNasBridge-Windows-x64, CtrlOne-…, …
  Mac/              ← 브리지·CtrlOne·SDM·WorkLog (있는 것만)
  VERSION.txt
  README.txt
  Install-BroadcastApps.bat / .ps1
```

설치 시 `Install-BroadcastApps.bat`로 위치를 고르면 **Windows\만** 복사한다 (바탕화면 바로가기 선택 가능).  
ScheduleReader는 **엑셀 추출**용 Python 패키지입니다(이미지 OCR 모델 불필요). 배포 체크리스트: [docs/DEPLOY-CHECKLIST.md](docs/DEPLOY-CHECKLIST.md).

| 앱 | 산출물 (outRoot 아래) |
|----|----------------|
| BroadcastNasBridge | `BroadcastNasBridge-Windows-x64\`, macOS arm64/x64 |
| CtrlOne | `CtrlOne\CtrlOne.exe`, `CtrlOne-Windows-x64-YYYYMMDD.zip` |
| FileChecker | `FileChecker\FileCheckerFinder.exe`, zip (**data 제외**, Windows만) |
| ScheduleDataManager | `BroadcastingSchedule-Windows-x64\`, zip |
| WorkLog | `WorkLog-Windows-x64\`, zip · macOS `.app` |
| ScheduleReader | `ScheduleReader-portable\` (Python + Setup-And-Run.bat), zip · Windows만 |

- 요약 파일: `BUILD-INFO.md`, `manifest.json`
- 환경 변수: `BROADCAST_BUILD_DIR`, `WORKLOG_BUILD_DIR`, `SCHEDULE_BUILD_DIR`
- FileChecker `data/`는 **배포 zip에 넣지 않는다**.
- FileChecker·ScheduleReader는 Windows 전용. Mac 타겟에서는 건너뛴다.

---

## 2026-09-08 동시 NAS 접속 수정 요약

1. **ScheduleDataManager / WorkLog:** `ForceClearServerSessions` 및 호스트 전체 `net use /delete` 제거. 실패 시 안내만.
2. **FileChecker:** NAS 테스트에서 1219/85/2404 시 기존 세션 재사용 시도.
3. **CtrlOne:** 단일 인스턴스, TCP write 직렬화, 명령 실패 전파, 연결 타임아웃.
