# Projects — 방송실 도구 모음

상위 폴더(이 **메타 레포** 루트) 아래에 여러 프로그램 소스가 형제 폴더로 있고, 배포 산출물은 `Builded`에 모은다.  
앱 소스는 각각 별도 GitHub 레포이며, 이 저장소는 문서·TODO·일괄 빌드만 관리한다. → [README.md](README.md)

| 문서 | 용도 |
|------|------|
| [README.md](README.md) | 메타 레포 소개·클론 배치 |
| [PROJECTS.md](PROJECTS.md) | 프로젝트 개요·NAS·포트·빌드 |
| [TODO.md](TODO.md) | **버그·개선 할 일** → 「방송실 TODO 해줘」로 일괄 작업 |
| [CHANGES.md](CHANGES.md) | 수정·배포 기록 |
| `broadcast-suite.version.json` | 스위트 버전·build 번호 |

| 폴더 | 역할 |
|------|------|
| **ScheduleDataManager** | 방송실 스케줄 관리 (NAS JSON R/W) |
| **WorkLog** | 일일 작업 일지 (NAS, 스케줄 읽기 + `_work_log` 쓰기) |
| **FileChecker** | 렌더링 파일 존재 여부 체크 (로컬 + NAS 스캔) |
| **CtrlOne** | HyperDeck 등 방송 장비 TCP 제어 (NAS 무관) |
| **ScheduleReader** | 스케줄 OCR → JSON (로컬; NAS 세션 관리 없음) |
| **ImgToText** | 이미지→텍스트 보조 도구 |
| **Builded** | 배포용 빌드 산출물 |

---

## 형제 앱 (같은 NAS, 동시 실행)

다음 세 앱은 **같은 PC에서 동시에** 실행될 수 있고, **같은 NAS 호스트**에 SMB로 붙는다.

| 앱 | 기본 포트 | 기본 공유 경로 | NAS 역할 |
|----|-----------|----------------|----------|
| ScheduleDataManager | `17821` | `Temp DATA\_data` | 스케줄 JSON·공문·백업 R/W |
| WorkLog | `17822` | `Temp DATA\_data\_work_log` | 일지 R/W, 상위 `_data` 스케줄 **읽기만** |
| FileChecker | `5187` (배포 bat) | `Permanent DATA\H264_mp4 DATA` | 미디어 파일 **열거/검색** |

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
| ScheduleDataManager | 17821 |
| WorkLog | 17822 |
| ScheduleReader | 17823 |
| CtrlOne | 5177 |
| FileChecker | 5187 (배포) / launchSettings는 개발용 |

겹치면 안 된다. WorkLog `docs/ports.md` 참고.

---

## 기술 스택 요약

| 앱 | 스택 |
|----|------|
| ScheduleDataManager | .NET LocalBridge + 임베드 웹(vanilla JS) |
| WorkLog | .NET LocalBridge / MacBridge + `www/` (+ 선택 `nas-api`) |
| FileChecker | ASP.NET Core + `wwwroot` |
| CtrlOne | ASP.NET Core + SignalR + HyperDeck TCP |
| ScheduleReader | Python FastAPI + OCR |

---

## 배포 · 최신화 (일괄 빌드)

방송실 5개 앱의 **최신 Windows 배포본**을 한 번에 `Builded`에 뽑고, **버전을 올려** 통합 zip까지 만든다.

| 방법 | 설명 |
|------|------|
| **`Build-BroadcastApps.bat`** | 더블클릭 → 전체 빌드 + 통합 zip |
| `scripts\Build-BroadcastApps.ps1` | PowerShell (옵션 가능) |
| `scripts\Show-BuildManifest.bat` | 마지막 빌드·버전 요약 |

버전 원본: `broadcast-suite.version.json` (빌드마다 `build` 번호 +1)

```powershell
# 전체 (개별 portable + 개별 zip + 통합 zip)
.\scripts\Build-BroadcastApps.ps1
# → Builded\BroadcastingApp_1.0.0.1_20260908.zip

# 마이너/메이저 버전 올림
.\scripts\Build-BroadcastApps.ps1 -Bump Minor
.\scripts\Build-BroadcastApps.ps1 -Version 1.2.0

# 일부만 / 통합 zip 생략
.\scripts\Build-BroadcastApps.ps1 -Apps CtrlOne,WorkLog
.\scripts\Build-BroadcastApps.ps1 -SkipBundle

# 마지막 빌드 정보
.\scripts\Build-BroadcastApps.ps1 -List
```

| 산출물 | 경로 |
|--------|------|
| 통합 배포 zip | `Builded\BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip` |
| 최신 포인터 | `Builded\LATEST.txt` |
| 버전 이력 | `Builded\versions\*.json` |
| 앱별 portable | 아래 표 |

통합 zip 안에는 압축되지 않은 각 앱 폴더가 들어 있다 (`CtrlOne-Windows-x64`, `FileChecker-Windows-x64`, `BroadcastingSchedule-Windows-x64`, `WorkLog-Windows-x64`, `ScheduleReader-portable` + `VERSION.txt`).

| 앱 | Builded 산출물 |
|----|----------------|
| CtrlOne | `CtrlOne\CtrlOne.exe`, `CtrlOne-Windows-x64-YYYYMMDD.zip` |
| FileChecker | `FileChecker\FileCheckerFinder.exe`, zip (**data 제외**) |
| ScheduleDataManager | `BroadcastingSchedule-Windows-x64\`, zip |
| WorkLog | `WorkLog-Windows-x64\`, zip |
| ScheduleReader | `ScheduleReader-portable\` (Python + Setup-And-Run.bat), zip |

- 요약 파일: `Builded\BUILD-INFO.md`, `Builded\manifest.json`
- 환경 변수: `BROADCAST_BUILD_DIR`, `WORKLOG_BUILD_DIR`, `SCHEDULE_BUILD_DIR`
- FileChecker `data/`는 **배포 zip에 넣지 않는다**.

---

## 2026-09-08 동시 NAS 접속 수정 요약

1. **ScheduleDataManager / WorkLog:** `ForceClearServerSessions` 및 호스트 전체 `net use /delete` 제거. 실패 시 안내만.
2. **FileChecker:** NAS 테스트에서 1219/85/2404 시 기존 세션 재사용 시도.
3. **CtrlOne:** 단일 인스턴스, TCP write 직렬화, 명령 실패 전파, 연결 타임아웃.
