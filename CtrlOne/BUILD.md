# CtrlOne 빌드 및 실행 가이드

Windows 환경 기준입니다. 대상 프레임워크는 **.NET 8** (`net8.0`)입니다.

## 레이아웃 데모 (장비 없이 UI 확인)

실장비/네트워크 없이도 대시보드 레이아웃을 볼 수 있습니다.

### 방법 1 — 빌드 산출물 (서버 불필요)

```powershell
dotnet build
```

`demo-preview\index.html` 을 브라우저로 엽니다. 가상 HyperDeck 4대가 Connected 상태로 표시됩니다.

### 방법 2 — 앱 실행 중

```powershell
dotnet run
```

브라우저에서 `http://127.0.0.1:5177/demo.html` 접속, 또는 대시보드의 **레이아웃 데모** 링크.

일괄 녹화/개별 버튼/순서 변경/가상 검색 UI까지 클릭해 볼 수 있으며, 실제 TCP 연결은 하지 않습니다.

프로젝트 폴더에서 **`빌드실행.bat`** 을 더블클릭하세요.

1. `publish\CtrlOne.exe` 단일 실행 파일을 만듭니다.
2. 자동으로 실행되고 브라우저가 열립니다.

이미 빌드된 exe만 다시 실행할 때는 **`실행.bat`** 또는 `publish\CtrlOne.exe`를 더블클릭하면 됩니다.

- 대시보드: **`http://127.0.0.1:5177/`**
- **콘솔 창 없음** (WinExe). 브라우저만 열림. **브라우저(대시보드 탭)를 닫으면 프로그램도 종료**됩니다.
- 진단용 콘솔: `CtrlOne.exe --console`
- 장비 목록: exe와 같은 폴더의 `devices.json`
- .NET 런타임이 없는 PC에도 `CtrlOne.exe`만 복사해 실행 가능 (wwwroot 폴더 불필요)
- exe 아이콘: `assets\app.ico` 를 넣은 뒤 다시 빌드 (자세한 설명은 `assets\README.txt`)

---

## 1. 사전 준비 (개발/빌드 PC)

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 이상 설치  
2. 프로젝트 폴더로 이동

```powershell
cd d:\Projects\CtrlOne
```

```powershell
dotnet --version
```

---

## 2. 개발 실행 (`dotnet run`)

코드 수정하면서 확인할 때:

```powershell
dotnet run
```

- URL: `http://127.0.0.1:5177/`
- 종료: `Ctrl+C`

---

## 3. 단일 실행 파일 수동 게시

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

| 항목 | 값 |
|------|-----|
| 결과 | `publish\CtrlOne.exe` (단독 실행) |
| UI | exe 내부에 포함 |
| 설정 | exe 옆 `devices.json` |

```powershell
.\publish\CtrlOne.exe
```

---

## 4. 동작 확인

| 확인 | 방법 |
|------|------|
| 서버 기동 | 콘솔에 Listening 메시지 |
| 웹 UI | 브라우저 대시보드 |
| 헬스체크 | `http://127.0.0.1:5177/api/health` |

---

## 5. 자주 나오는 문제

### 포트 5177 사용 중

```powershell
Stop-Process -Name CtrlOne -Force -ErrorAction SilentlyContinue
```

### publish 시 exe 잠김

실행 중인 CtrlOne을 종료한 뒤 다시 빌드하세요.

### HyperDeck 검색 안 됨

동일 LAN, Remote 설정, TCP 9993 확인.

---

## 6. 명령 요약

```powershell
# 원클릭 (권장)
.\빌드실행.bat

# 개발 실행
dotnet run

# 수동 게시
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\CtrlOne.exe
```

사용법은 [README.md](README.md), 아키텍처는 [DEVELOPER.md](DEVELOPER.md)를 참고하세요.
