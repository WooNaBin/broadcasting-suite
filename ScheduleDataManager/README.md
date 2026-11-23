# Broadcasting Schedule

Windows / macOS용 **무설치(휴대용)** 일정 관리 프로그램입니다.  
로컬 브리지 하나로 API와 웹 UI를 띄우고, NAS(SMB)의 공유 JSON을 읽고 저장합니다.

| OS | 실행 파일 | 패키지 스크립트 |
| --- | --- | --- |
| Windows 10/11 x64 | `BroadcastingSchedule.exe` | `package-portable.bat` |
| macOS (Apple Silicon / Intel) | `BroadcastingSchedule` | `package-portable-macos.sh` |

## Windows 배포

1. **권장:** 상위 폴더에서 `Build-BroadcastApps.bat` (또는 `scripts\Build-BroadcastApps.ps1`)로 일괄 빌드  
   → `D:\Projects\Builded\ScheduleDataManager\`
2. 또는 이 폴더에서 `package-portable.bat` 실행  
   → 동일하게 `D:\Projects\Builded\ScheduleDataManager\` (환경변수 `SCHEDULE_BUILD_DIR`로 변경 가능)
3. 작업 PC에서 zip 해제 후 `BroadcastingSchedule.exe` 실행

- .NET 별도 설치 불필요
- 설치 프로그램 없음 (폴더 복사만)

또는 `publish-bridge.bat`을 실행해도 동일한 휴대용 패키지를 만듭니다.  
개발용 호환 경로로 `publish\BroadcastingSchedule-portable`에도 복사됩니다.

## macOS 배포

1. Mac에서 `./package-portable-macos.sh` 실행  
   (현재 Mac 아키텍처에 맞춰 `osx-arm64` 또는 `osx-x64`로 빌드)
2. 생성된 압축 파일 사용  
   `publish/BroadcastingSchedule-macOS-arm64-YYYYMMDD.zip`  
   또는 `publish/BroadcastingSchedule-macOS-x64-YYYYMMDD.zip`
3. 압축 해제 후 `BroadcastingSchedule` 실행

```bash
chmod +x package-portable-macos.sh
./package-portable-macos.sh
```

### 첫 실행 / Gatekeeper

다운로드한 바이너리는 macOS가 막을 수 있습니다.

- Finder에서 **우클릭 → 열기**
- 또는 터미널:

```bash
xattr -dr com.apple.quarantine ./BroadcastingSchedule
./BroadcastingSchedule
```

### NAS / 로컬 네트워크

- **시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크**에서 허용이 필요할 수 있습니다.
- macOS는 `mount_smbfs`로 공유를 마운트합니다.  
  마운트 위치: `~/Library/Application Support/BroadcastingSchedule/mnt/`
- 공유 하위 폴더는 `공유명/하위폴더` 또는 `공유명\하위폴더` 모두 입력 가능합니다.

## NAS 연결

1. 프로그램 실행 → 브라우저 자동 오픈 (`http://127.0.0.1:17821`)
2. NAS 주소(기존 연결 / 로컬 네트워크 선택 / 직접 입력), 공유 폴더, 계정 입력
3. **연결하고 시작**

브리지는 `127.0.0.1:17821`만 사용합니다.

## 기능 요약

- 단일/기간 일정, 달력·목록 보기
- 10분 단위 시간, 장소·준비 항목
- NAS JSON 자동 저장 및 다중 PC 충돌 병합
- 공문 이미지 업로드/연결 (`_official_documents`)
- NAS 백업 ZIP 복원

## 개발용 직접 publish

```bash
# macOS (예: Apple Silicon)
dotnet publish LocalBridge/LocalBridge.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -o publish/LocalBridge-macos

# Windows x64 (Windows에서, 또는 EnableWindowsTargeting으로 크로스 publish)
dotnet publish LocalBridge/LocalBridge.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish/LocalBridge
```

## JSON 예시

```json
[
  {
    "id": "unique-id",
    "scheduleDate": "2026-09-06",
    "releaseDate": "2026-09-06",
    "startTime": 1030,
    "scheduleTitle": "주일말씀",
    "accessoryInfo": "기타 전달사항",
    "place": "Main_Large",
    "sermonSpeaker": "강사이름",
    "preparation": ["Recording", "Streaming"],
    "preaparationNote": "송출 장비 확인"
  }
]
```

웹 JSON의 `startTime`은 `HHmm` 정수(`1030`)입니다.
