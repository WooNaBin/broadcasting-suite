# 방송실 UI 데모 (NAS·빌드 없이 확인)

현재 **소스 트리**의 웹 UI를 브라우저에서만 미리 봅니다. 브리지/NAS/exe 빌드가 필요 없습니다.

## 실행

| OS | 실행 |
|---|---|
| Windows | `demos\Open-UiDemos.bat` |
| Mac | `demos/Open-UiDemos.command` 또는 `demos/Open-UiDemos.sh` |

브라우저: [http://127.0.0.1:17990/demos/](http://127.0.0.1:17990/demos/)

중지: 서버 터미널에서 `Ctrl+C`.

Windows: PowerShell `HttpListener` (Python 불필요)  
Mac: `python3 -m http.server`

## 앱별

| 앱 | URL (서버 기준) | 방식 |
|---|---|---|
| 허브 | `/BroadcastNasBridge/wwwroot/demo.html` | 정적 목업 |
| SDM | `/ScheduleDataManager/index.html?demo=1` | fetch mock |
| WorkLog | `/WorkLog/www/index.html?demo=1` | fetch mock |
| FileChecker | `/FileChecker/wwwroot/index.html?demo=1` | fetch mock |
| CtrlOne | `/CtrlOne/wwwroot/demo.html` | 기존 데모 |
| ScheduleReader | `/ScheduleReader/schedule_reader/web/static/demo.html` | 정적 샘플 |

데모에서는 저장·연결이 **브라우저 메모리(또는 localStorage)** 에만 남고 NAS에는 쓰지 않습니다.

## Win / Mac

동일합니다. 정적 HTTP + 브라우저만 사용합니다.
