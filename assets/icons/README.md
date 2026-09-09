# 방송실 스위트 아이콘 (드롭 위치)

아이콘 원본을 **여기**에 넣습니다. (`#100`)  
빌드/설치에 반영하는 작업은 별도. 지금은 경로만 준비.

## 넣는 파일

각 앱 폴더에 Windows용 **`.ico`** 를 둡니다. 파일명은 아래와 같이 맞춥니다.

| 폴더 | 넣을 파일 | 적용 대상(앱 쪽) |
|------|-----------|------------------|
| `BroadcastNasBridge/` | `app.ico` | `BroadcastNasBridge/assets/app.ico` |
| `CtrlOne/` | `app.ico` | `CtrlOne/assets/app.ico` |
| `FileChecker/` | `app.ico` | `FileChecker/assets/app.ico` |
| `BroadcastingSchedule/` | `app.ico` (또는 `BroadcastingSchedules.ico`) | `ScheduleDataManager/LocalBridge/BroadcastingSchedules.ico` · `assets/` |
| `WorkLog/` | `app.ico` (또는 `WorkLog.ico`) | `WorkLog/assets/WorkLog.ico` |
| `ScheduleReader/` | `app.ico` | `ScheduleReader/assets/app.ico` (바로가기용) |

권장: 256×256 포함 multi-size `.ico` (16/32/48/256).

선택: 같은 폴더에 `app.png`(512 또는 1024)를 두면 macOS·허브 UI용으로 쓸 수 있음.

## 앱 레포 `assets/`

형제 앱에도 빈 `assets/` 를 만들어 두었음. 원본은 이 스위트 폴더에 두고, 반영 시 각 앱 `assets/` 로 복사하면 됨.
