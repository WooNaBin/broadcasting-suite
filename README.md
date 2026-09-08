# Broadcasting suite (meta)

방송실 프로그램 **묶음**용 메타 저장소입니다.  
각 앱 소스는 아래 독립 레포에 있고, 여기에는 **문서·TODO·일괄 빌드·버전**만 둡니다.

## 구성 앱

| 앱 | 역할 | 저장소 |
|----|------|--------|
| **BroadcastNasBridge** | NAS 통합 진입 (메타 레포 내) | (이 스위트) |
| ScheduleDataManager | 스케줄 관리 (NAS) | https://github.com/WooNaBin/schedule-data-manager |
| WorkLog | 일일 작업 일지 (NAS) | https://github.com/WooNaBin/WorkLog |
| FileChecker | 렌더링 파일 체크 | https://github.com/WooNaBin/FileChecker |
| CtrlOne | HyperDeck 제어 | https://github.com/WooNaBin/CtrlOne |
| ScheduleReader | 일정표 OCR → JSON | https://github.com/WooNaBin/ScheduleReader |

## 로컬 폴더 배치 (권장)

이 메타 레포를 클론한 디렉터리에 앱 레포를 **형제 폴더**로 둡니다. 일괄 빌드 스크립트가 그 레이아웃을 가정합니다.

```text
Projects/                          ← 이 메타 레포 (또는 클론 루트)
  PROJECTS.md
  TODO.md
  CHANGES.md
  Build-BroadcastApps.bat
  scripts/
  BroadcastNasBridge/              ← 통합 NAS 브리지 (메타 레포에 포함)
  CtrlOne/                         ← git clone (별도 레포)
  FileChecker/
  ScheduleDataManager/
  ScheduleReader/
  WorkLog/
  Builded/                         ← 빌드 산출물 (git 무시)
```

```powershell
cd D:\Projects
git clone https://github.com/WooNaBin/broadcasting-suite.git .
# 또는 빈 폴더에 클론 후 앱들을 같은 상위에 배치

git clone https://github.com/WooNaBin/CtrlOne.git
git clone https://github.com/WooNaBin/FileChecker.git
git clone https://github.com/WooNaBin/schedule-data-manager.git ScheduleDataManager
git clone https://github.com/WooNaBin/WorkLog.git
git clone https://github.com/WooNaBin/ScheduleReader.git
```

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
# 또는
.\scripts\Build-BroadcastApps.ps1
```

산출물: `Builded\` (gitignore)  
통합 zip: `Builded\BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip`

## 이 레포에 올리는 것 / 올리지 않는 것

- **포함:** 스위트 문서, TODO, 빌드 스크립트, Cursor 규칙(방송실 TODO), 버전 파일  
- **제외:** 각 앱 소스, `Builded\`, 비밀·로컬 설정, ImgToText 등 스위트 밖 폴더  
