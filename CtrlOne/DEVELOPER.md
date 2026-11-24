# CtrlOne 개발자 문서

## 아키텍처 개요

```
CtrlOne.exe
 ├─ ASP.NET Core Minimal Host (http://127.0.0.1:5177)
 ├─ Static UI (wwwroot) + SignalR Hub
 ├─ DeviceManager (장비 수명주기 / 일괄 제어 / 원터치 액션 / 영속화)
 ├─ DeviceStore (devices.json)
 ├─ PresetStore (presets.json)
 ├─ ActionStore (actions.json)
 └─ ISequenceDevice 구현체
      └─ HyperDeckDevice (TCP 9993)
```

실행 시 `Program.cs`가 웹 호스트를 띄우고, 시작 콜백에서 `DeviceManager.InitializeAsync`로 `devices.json`을 로드한 뒤 기본 브라우저를 엽니다.

브라우저를 닫으면 프로세스가 종료됩니다. Stream Deck 플러그인은 CtrlOne이 살아 있을 때만 `/api/actions`를 폴링하고, 종료 시 오프라인으로 표시한 뒤 재실행 시 다시 목록을 불러옵니다.

## 원터치 액션 · Stream Deck

| 경로 | 역할 |
|------|------|
| `Services/ActionStore.cs` | `actions.json` 로드/저장 |
| `GET /api/actions` | 액션 목록 (Stream Deck용) |
| `POST /api/actions/{id}/run` | 액션 실행 |
| `streamdeck/com.ctrlone.actions.sdPlugin/` | Elgato Stream Deck 플러그인 |
| `streamdeck/README.md` | 설치·동작 안내 |

대시보드에서 장비 + 명령(녹화/중지/재생 등)을 묶어 액션을 만들고, UI 버튼 또는 Stream Deck이 그 액션만 실행합니다.

## 주요 구성 요소

| 경로 | 역할 |
|------|------|
| `Devices/ISequenceDevice.cs` | 순서·선택·Transport 제어 공통 인터페이스 |
| `Devices/HyperDeckDevice.cs` | HyperDeck TCP 클라이언트 + Companion식 버퍼 파싱 |
| `Services/HyperDeckDiscoveryService.cs` | 로컬 서브넷 9993 스캔 및 배너 식별 |
| `Services/DeviceStore.cs` | `devices.json` 로드/저장 |
| `Services/DeviceManager.cs` | 멀티 소켓 관리, 일괄 명령, SignalR 푸시 |
| `Hubs/DashboardHub.cs` | UI 호출 진입점 |
| `wwwroot/` | 단일 페이지 대시보드 |

## ISequenceDevice

타사 장비(AverMedia 등)를 같은 매니저로 다루기 위한 계약입니다.

```csharp
public interface ISequenceDevice : IAsyncDisposable
{
    Guid Id { get; }
    string Name { get; set; }
    string Address { get; }
    int Order { get; set; }
    bool IsSelected { get; set; }
    bool IsConnected { get; }
    DeviceSnapshot Snapshot { get; }

    event Action<ISequenceDevice, string>? LogReceived;
    event Action<ISequenceDevice>? StatusChanged;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopDeviceAsync();
    Task PlayAsync();
    Task RecordAsync();
    Task StopTransportAsync();
}
```

`DeviceManager`는 이 인터페이스만 의존합니다. UI에는 `DeviceSnapshot` DTO만 전달합니다.

## HyperDeck TCP (Companion 이식 포인트)

### 연결

1. `TcpClient.ConnectAsync(ip, 9993)`
2. 연결 직후 `notify: transport: true slot: true\n` 전송
3. `transport info` / `slot info: slot id: 1|2` 로 초기 상태 조회
4. 끊기면 5초 대기 후 자동 재연결

### 줄 단위 버퍼 파싱

TCP는 메시지를 임의로 분할합니다. Companion과 같이 `StringBuilder`에 누적한 뒤 `\n`이 완성된 라인만 파싱합니다.

```csharp
_rxBuffer.Append(chunk);
var lines = ExtractCompleteLines(_rxBuffer); // \n 기준 분리, 미완성 잔여 유지
foreach (var line in lines)
    ApplyProtocolLine(line);
```

`key: value` 형태를 파싱해 transport status, slot status, recording time을 갱신합니다.

## HyperDeck 네트워크 검색

`HyperDeckDiscoveryService`가 활성 NIC의 IPv4 대역(기본 `/24`)을 병렬 스캔합니다.

1. TCP `9993` 연결 시도 (타임아웃 ~350ms)
2. 연결 직후 배너에서 `connection info` / `model` / `protocol version` 파싱
3. 이미 `DeviceManager`에 등록된 IP는 `AlreadyRegistered`로 표시

UI는 `ScanNetwork` → 선택 → `AddDiscoveredDevices` 순으로 호출합니다.

## devices.json

실행 파일 디렉터리에 저장됩니다. 추가/삭제/이름 변경/선택/순서 변경 시 즉시 기록합니다.

```json
[
  {
    "id": "...",
    "name": "Mini_Camera_01",
    "address": "192.168.1.50",
    "order": 0,
    "isSelected": true,
    "deviceType": "HyperDeck"
  }
]
```

## SignalR 이벤트

| 방향 | 메서드 | 설명 |
|------|--------|------|
| Server → Client | `DevicesUpdated` | 전체 목록 갱신 |
| Server → Client | `DeviceUpdated` | 단일 장비 상태 |
| Server → Client | `Log` / `LogsSnapshot` | 로그 |
| Client → Server | `AddDevice`, `RemoveDevice`, `RenameDevice`, … | UI 조작 |

## AverMedia 등 확장 방법

1. `ISequenceDevice`를 구현하는 `AverMediaDevice` 클래스 작성
2. `DeviceManager.CreateDevice`에서 `DeviceRecord.DeviceType` 분기 추가

```csharp
return record.DeviceType switch
{
    "AverMedia" => new AverMediaDevice(...),
    _ => new HyperDeckDevice(...)
};
```

3. (선택) 플러그인 어셈블리 로드용 팩토리 인터페이스를 두고 `DeviceType` → 구현체 매핑 테이블로 분리

UI 카드는 `DeviceSnapshot`만 소비하므로, 새 장비가 같은 스냅샷 필드를 채우면 추가 화면 작업 없이 그리드에 표시됩니다.

## 빌드 / 게시

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

프로젝트는 `net8.0`을 타깃으로 합니다. 설치된 SDK에 해당 타깃 팩이 없으면 `CtrlOne.csproj`의 `TargetFramework`를 사용 가능한 TFM으로 바꾸면 됩니다.

## 유지보수 팁

- 통신/프로토콜 변경 → `HyperDeckDevice`만 수정
- 목록·순서·저장 → `DeviceManager` + `DeviceStore`
- UI 레이아웃 → `wwwroot` (서버 C# 변경 최소)
- 포트/URL → `Program.cs`의 `UseUrls`
