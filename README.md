# Broadcasting suite

> **Canonical host repo:** [WooNaBin/AppBSS](https://github.com/WooNaBin/AppBSS) (ex-BackendBSS).  
> Server/suite sources live there as `suite/apps/broadcasting-suite`. Prefer AppBSS for deploy. This repo stays for desktop builds; archive optional — do not force-delete without noting.

방송실 프로그램 **모노레포**입니다.  
문서·TODO·일괄 빌드·버전과 **앱 소스**를 이 저장소 하나에서 관리합니다.

## 구성 앱

| 앱 | 역할 | 폴더 |
|----|------|------|
| **BroadcastNasBridge** | NAS 통합 진입 (권장) | `BroadcastNasBridge/` |
| ScheduleDataManager | 스케줄 관리 (NAS) — 레거시 단독 | `ScheduleDataManager/` |
| WorkLog | 일일 작업 일지 (NAS) — 레거시 단독 | `WorkLog/` |
| FileChecker | 렌더링 파일 체크 — 레거시 단독 | `FileChecker/` |
| CtrlOne | HyperDeck 제어 | `CtrlOne/` |
| ScheduleReader | 일정표 → JSON | `ScheduleReader/` |

## 클론

```powershell
git clone https://github.com/WooNaBin/broadcasting-suite.git
cd broadcasting-suite
```

```text
broadcasting-suite/
  PROJECTS.md · TODO.md · CHANGES.md
  Build-BroadcastApps.bat / .command
  scripts/
  BroadcastNasBridge/
  CtrlOne/
  FileChecker/
  ScheduleDataManager/
  ScheduleReader/
  WorkLog/
  Builded/                 ← 빌드 산출물 (git 무시)
```

VS Code/Cursor: `BroadcastingApp.code-workspace` 로 폴더 묶음 열기.

## 문서

| 파일 | 용도 |
|------|------|
| [PROJECTS.md](PROJECTS.md) | 개요·NAS·포트·빌드 |
| [TODO.md](TODO.md) | 버그·개선 → 「방송실 TODO 해줘」 |
| [CHANGES.md](CHANGES.md) | 수정·배포 기록 |
| `broadcast-suite.version.json` | 스위트 버전·build |

## 일괄 빌드

```powershell
.\Build-BroadcastApps.bat
# 산출 폴더 변경 (기록: broadcast-suite.build.json)
.\Configure-BroadcastBuild.bat
# 또는
.\scripts\Build-BroadcastApps.ps1 -Target All
```

```bash
./Build-BroadcastApps.command
./Configure-BroadcastBuild.command
# 또는
./scripts/Build-BroadcastApps.sh --set-out ~/Releases --target all
```

산출물: 설정한 `outRoot` (기본 `<레포>/Builded`, gitignore)  
통합 zip: `BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip`

## 이 레포에 올리는 것 / 올리지 않는 것

- **포함:** 스위트 문서, TODO, 빌드 스크립트, Cursor 규칙, 버전 파일, **각 앱 소스**
- **제외:** `Builded\`, `broadcast-suite.build.json`(머신별 경로), 비밀·로컬 설정, ImgToText 등 스위트 밖 폴더
