# CtrlOne Stream Deck 플러그인

CtrlOne 대시보드에서 만든 **원터치 액션**을 Stream Deck 버튼으로 실행합니다.

## 동작

- CtrlOne(`http://127.0.0.1:5177`)이 **실행 중**일 때만 액션 목록·실행이 가능합니다.
- 브라우저를 닫아 CtrlOne이 종료되면 버튼 제목이 **오프라인**으로 바뀝니다.
- CtrlOne을 다시 켜면 약 2~3초 안에 목록이 다시 표시됩니다. (플러그인 상주 서버 없음)

## 설치 (Windows)

배포 패키지에는 `CtrlOne-Windows-x64\streamdeck\` 이 포함됩니다.

1. Stream Deck 앱을 종료합니다.
2. `Install-StreamDeckPlugin.bat` 을 실행합니다.  
   또는 `com.ctrlone.actions.sdPlugin` 전체를 `%appdata%\Elgato\StreamDeck\Plugins\` 로 복사합니다.

```powershell
Copy-Item -Recurse -Force `
  ".\com.ctrlone.actions.sdPlugin" `
  "$env:APPDATA\Elgato\StreamDeck\Plugins\"
```

3. Stream Deck 앱을 다시 실행합니다.
4. 카테고리 **CtrlOne** → **원터치 액션**을 프로필에 끌어다 놓습니다.
5. 버튼 Property Inspector에서 액션을 선택합니다.

## 설치 (Mac)

배포 폴더의 `streamdeck/Install-StreamDeckPlugin.command` 를 실행하거나:

```bash
cp -R ./com.ctrlone.actions.sdPlugin \
  ~/Library/Application\ Support/com.elgato.StreamDeck/Plugins/
```

Stream Deck 앱을 재시작합니다.

## API (CtrlOne)

| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | `/api/health` | 앱 생존 확인 |
| GET | `/api/actions` | 원터치 목록 |
| POST | `/api/actions/{id}/run` | 액션 실행 |

## 문제 해결

- 버튼이 계속 오프라인: CtrlOne 대시보드가 브라우저에 열려 있는지 확인 (`5177`).
- 액션이 목록에 없음: CtrlOne에서 원터치 액션을 저장했는지 확인.
- 실행 시 빨간 경고: 해당 장비가 미연결이거나 액션이 삭제됨.
