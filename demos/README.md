# 방송실 UI 데모 (더미 데이터)

배포와 **같은** 앱 페이지를 `?demo=1`로 엽니다. NAS·브리지·exe 없이, 미리 넣은 더미 데이터로 **메인 화면**부터 봅니다 (로그인·PIN 생략).

일상 레이아웃/디자인 작업에서는 데모 파일을 고치지 않습니다. 더미 동기화는 **「데모 페이지 업데이트」** 요청 시에만 (`demo-boot.js`).

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

| 앱 | URL (서버 기준) | 시작 화면 |
|---|---|---|
| 허브 | `/BroadcastNasBridge/wwwroot/demo.html` | 허브 카드 (정적) |
| SDM | `/ScheduleDataManager/index.html?demo=1` | 일정 보드 |
| WorkLog | `/WorkLog/www/index.html?demo=1` | 메인 에디터 |
| FileChecker | `/FileChecker/wwwroot/index.html?demo=1` | 작업 목록 |
| CtrlOne | `/CtrlOne/wwwroot/demo.html` | 기존 데모 |
| ScheduleReader | `/ScheduleReader/schedule_reader/web/static/demo.html` | 샘플 표 |

저장·연결은 브라우저 메모리에만 남고 NAS에는 쓰지 않습니다.

## Win / Mac

동일합니다. 정적 HTTP + 브라우저만 사용합니다.
