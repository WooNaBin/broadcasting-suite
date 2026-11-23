# FileCheckerFinder

영상 렌더링 작업 스케줄과 로컬/NAS 파일 확인 상태를 브라우저에서 관리하는 ASP.NET Core 앱입니다.  
**Windows / macOS** 모두 지원합니다.

## 실행

### Windows
프로젝트 폴더의 `FileCheckerFinder.bat`를 더블클릭하거나:

```powershell
dotnet run
```

브라우저: `http://127.0.0.1:5187`

### macOS
```bash
./package-portable-macos.sh
# 산출물: ../Builded/FileChecker/FileChecker-macOS-arm64 (또는 x64)
./FileCheckerFinder
```

Gatekeeper: 우클릭 → 열기, 또는 `xattr -dr com.apple.quarantine FileCheckerFinder`  
로컬 경로는 직접 입력 (폴더 대화상자는 Windows만).

## 다른 컴퓨터에 배포

배포용 빌드 출력 경로는 `Builded/FileChecker`입니다.

- **권장:** 상위 `Build-BroadcastApps.bat` / `scripts/Build-BroadcastApps.sh` 일괄 빌드 (`data/` 제외)
- Windows: `Publish-Release.bat` 또는 win-x64 publish
- macOS: `package-portable-macos.sh` (arm64 + x64)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o D:\Projects\Builded\FileChecker
```

Windows에서는 `Start-FileCheckerFinder.bat`를 실행합니다. 각 컴퓨터의 로컬 경로와 NAS 설정은 해당 기기에서 다시 입력하세요.

배포 시 기존 `data` 폴더는 복사하지 마세요. NAS 비밀번호가 설정 파일에 저장될 수 있습니다.

## 구조

- `Program.cs`: Minimal API, JSON 저장소, 파일 검색 로직
- `wwwroot/`: 정적 웹 UI (`index.html`, `styles.css`, `app.js`)
- `data/`: 최초 저장 시 자동 생성되는 스케줄/설정 JSON

## 파일 확인 규칙

스케줄의 날짜(`yyyyMMdd`), 설명, 강사 이름이 파일명에 모두 포함된 파일을 각 경로의 하위 폴더까지 검색합니다. NAS는 설정한 IP와 검색 시작 경로를 UNC 경로로 조합합니다. Windows에서 앱을 실행하는 계정이 해당 NAS 공유 폴더에 접근할 수 있어야 합니다.

파일 확인은 경로별 파일 인덱스를 5분 동안 메모리에 캐시합니다. 또한 폴더 이름에서 `yyyyMMdd` 형식의 날짜 또는 `yyyy` 형식의 연도를 식별할 수 있으면 일정과 다른 폴더의 하위 검색을 건너뜁니다. 예를 들어 `폴더이름_20260901_부가설명`은 `20260902` 일정 확인 시, `완료본_2025`는 `2026` 일정 확인 시 탐색하지 않습니다. 날짜/연도가 없는 폴더는 파일 누락 방지를 위해 계속 검색합니다. 상단의 목록 새로고침 버튼을 누르면 캐시를 무효화하고 한 번만 다시 검색합니다. 기간 필터와 정렬 변경은 서버를 호출하지 않습니다.

각 경로에서 다음 세 종류를 독립적으로 확인합니다.

- 음성: 파일명 조건 + `.mp3`
- 일반영상: 파일명 조건 + `.mp4` (단, `_숫자M.mp4` 제외)
- 고화질영상: 파일명 조건 + `_숫자M.mp4` (예: `_5M.mp4`, `_10M.mp4`)

파일명 조건을 비교할 때 공백과 영문 대소문자는 무시합니다. 따라서 `설명텍스트`는 `설명 텍스트`와 매칭되며, `EventName`은 `eventname`과 매칭됩니다. 이 처리는 이미 확보한 파일 인덱스의 메모리 비교 단계에서 수행되므로 추가 NAS 트래픽이 발생하지 않습니다.

스케줄 목록에는 `설명_메모_yyyyMMdd_강사이름` 형식의 기본파일명이 표시됩니다. 메모 또는 강사 이름이 없으면 해당 구간은 생략되며, 옆의 `복사` 버튼으로 전체 이름을 클립보드에 복사할 수 있습니다.

NAS 계정을 입력하면 접속 테스트 시 Windows SMB 공유 인증을 시도합니다. 계정을 비워두면 현재 Windows 로그인 계정으로 접근합니다. 운영 환경에서는 설정 파일의 비밀번호 보호와 NAS 전용 권한 계정 사용을 권장합니다.

NAS 검색 시작 경로의 기본값은 `Permanent DATA\H264_mp4 DATA`입니다. 설정 화면에서 `기본값` 버튼을 누르면 해당 경로가 입력되며, `설정 저장`을 눌러 저장합니다.

설정 화면의 `전체 내보내기`는 모든 스케줄을 JSON 백업 파일로 다운로드합니다. `전체 불러오기`는 FileCheckerFinder JSON 파일을 검증한 뒤 현재 스케줄을 가져온 데이터로 교체합니다.

목록 날짜는 실행 중인 PC의 현재 날짜를 기준으로 과거는 회색, 오늘은 배경 강조, 미래는 별도 날짜 색상으로 표시합니다.

## 개발

```powershell
dotnet build
dotnet watch run
```