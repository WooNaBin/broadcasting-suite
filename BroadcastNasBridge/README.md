# BroadcastNasBridge

방송실 NAS 형제 앱(스케줄 · 작업일지 · 파일체크)용 **통합 로컬 브리지**.

- 수신: `http://127.0.0.1:17820`
- 설정: OS별 Application Support / `%AppData%\BroadcastNasBridge\nas.json`
- 세션: 시작 페이지·앱 탭이 모두 닫히면 **5초** 후 종료

## 실행

```bash
# Mac / 개발
./scripts/sync-ui.sh          # 형제 앱 UI 복사·패치
./scripts/launch-macos.sh     # 또는
dotnet run -c Release -- --console
```

Windows: `scripts/Launch-BroadcastNasBridge.bat` 또는 게시된 `BroadcastNasBridge.exe`

## 경로

| URL | 역할 |
|-----|------|
| `/` | 시작 인덱스 |
| `/setup.html` | NAS 경로 마법사 |
| `/schedule/` | Broadcasting Schedule |
| `/worklog/` | WorkLog |
| `/files/` | FileChecker |

설계: [docs/NAS-BRIDGE-PLAN.md](../docs/NAS-BRIDGE-PLAN.md)
