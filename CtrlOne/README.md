# CtrlOne — HyperDeck 일괄 스위치

Windows에서 실행되는 Blackmagic HyperDeck 다중 장비 제어·모니터링 대시보드입니다.  
실행 파일 하나로 내장 웹 서버가 뜨고, 기본 브라우저가 자동으로 열립니다.

> **빌드·실행 상세:** [BUILD.md](BUILD.md)  
> **할 일·메모:** [할일.md](할일.md) — 적어 두고 「할 일 해줘」라고 하면 처리

## 실행 방법

### 단일 프로그램 (권장)

1. **`빌드실행.bat`** 더블클릭 → `publish\CtrlOne.exe` 생성 후 자동 실행  
2. 이후에는 **`실행.bat`** 또는 `publish\CtrlOne.exe`만 더블클릭

콘솔 창 없이 브라우저만 열립니다. **대시보드 탭/브라우저를 닫으면 프로그램도 종료**됩니다.  
진단이 필요하면 `CtrlOne.exe --console` 로 실행하세요.

장비 없이 레이아웃만 보려면 빌드 후 `demo-preview\index.html` 을 열거나, 실행 중 `http://127.0.0.1:5177/demo.html` 로 접속하세요.  
카드 열(A~E) 폭·간격 조절: `wwwroot/css/layout.css` 또는 `/layout-editor.html` · 설명은 `LAYOUT.md`.

### 개발 실행

```powershell
cd d:\Projects\CtrlOne
dotnet run
```

브라우저가 `http://127.0.0.1:5177/` 로 자동 오픈됩니다.

다른 방송실 앱과 포트가 겹치지 않습니다: 스케줄 관리자 17821 · WorkLog 17822 · ScheduleReader 17823 · **CtrlOne 5177**.


### 수동 게시

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\CtrlOne.exe
```

## 사용법

### 장비 추가

1. 상단 **＋ 장비 추가** 클릭 → 로컬 네트워크 자동 검색 시작
2. 발견된 HyperDeck 목록에서 추가할 장비 선택 (표시 이름 수정 가능)
3. **선택 추가** → 등록 및 TCP 연결 시작

이미 등록된 IP는 목록에 “이미 등록됨”으로 표시되며 다시 추가되지 않습니다.  
검색은 활성 네트워크 어댑터의 IPv4 `/24` 대역에서 HyperDeck 제어 포트(`9993`) 배너를 확인하는 방식입니다.

### 그룹(일괄) 제어

1. **그룹 제어 대상**에서 포함할 장비만 체크
2. **● 일괄 녹화** → 선택된 장비에 `record` 동시 전송
3. **■ 일괄 중지** → 선택된 장비에 `stop` 동시 전송

### 개별 제어·모니터링

각 카드에서 다음을 확인할 수 있습니다.

- 연결 상태 (Connected 초록 / Disconnected 빨강)
- Transport 상태 (`preview`, `record`, `play` 등)
- Slot 1 / Slot 2 미디어 상태와 남은 녹화 시간
- **▶ 재생 / ● 녹화 / ■ 중지** 개별 명령

### 순서 변경·이름 변경·삭제

- **▲ 위로 / ▼ 아래로**: 대시보드 카드 순서 변경 (`devices.json`에 즉시 저장)
- **✎**: 표시 이름 변경
- **✕**: 소켓 종료 후 목록에서 제거

### 실시간 로그

하단 로그 창에 Tx/Rx·연결 이벤트가 쌓이며, 새 로그가 오면 자동으로 맨 아래로 스크롤됩니다.

## 요구 환경

- Windows 10/11
- HyperDeck과 동일 네트워크 (원격 제어 활성화, 포트 9993)
- 개발 시: .NET 8 SDK 이상 (현재 환경 SDK가 더 높으면 해당 TFM으로 조정 가능)

## 문제 해결

| 증상 | 확인 |
|------|------|
| Disconnected 유지 | IP, 방화벽, HyperDeck Remote 설정, 포트 9993 |
| 브라우저가 안 열림 | `http://127.0.0.1:5177/` 수동 접속 |
| 설정이 안 남음 | exe 폴더에 `devices.json` 쓰기 권한 확인 |
