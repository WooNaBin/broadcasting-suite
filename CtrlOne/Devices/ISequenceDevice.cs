namespace CtrlOne.Devices;

/// <summary>
/// 시퀀스(순서) 관리가 가능한 원격 장비의 공통 계약.
/// HyperDeck 외 AverMedia 등 타사 장비를 같은 DeviceManager로 다룰 수 있도록 확장 지점을 제공한다.
/// </summary>
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
    Task SelectSlotAsync(int slotId);
    Task PreviewAsync(bool enable);
    Task RecordSpillAsync();
    Task StreamAsync(bool enable);
}

/// <summary>
/// 장비가 실제로 지원하는 원격 명령 집합 (commands XML / device info 기반).
/// </summary>
public sealed class DeviceCapabilities
{
    public string Model { get; init; } = "";
    public int SlotCount { get; init; } = 2;
    public bool CanPlay { get; init; }
    public bool CanStop { get; init; }
    public bool CanRecord { get; init; }
    public bool CanPreview { get; init; }
    public bool CanSelectSlot { get; init; }
    public bool CanRecordSpill { get; init; }
    public bool CanStream { get; init; }
    public bool CapabilitiesResolved { get; init; }
}

/// <summary>
/// UI/SignalR로 푸시할 장비 상태 스냅샷.
/// </summary>
public sealed class DeviceSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public int Order { get; init; }
    public bool IsSelected { get; init; }
    public bool IsConnected { get; init; }
    public string TransportStatus { get; init; } = "unknown";
    public int? ActiveSlotId { get; init; }
    public bool SupportsSlotSelect { get; init; }
    public bool IsPreviewMode { get; init; }
    public bool IsStreaming { get; init; }
    /// <summary>idle | ready | blocked | pending | done | error</summary>
    public string SpillStatus { get; init; } = "idle";
    public string SpillMessage { get; init; } = "";
    public DeviceCapabilities Capabilities { get; init; } = new();
    /// <summary>알려진 슬롯 목록 (1..N). 구 UI 호환용 Slot1/Slot2는 별칭.</summary>
    public IReadOnlyList<SlotSnapshot> Slots { get; init; } = Array.Empty<SlotSnapshot>();
    public SlotSnapshot Slot1 => Slots.Count > 0 ? Slots[0] : EmptySlot;
    public SlotSnapshot Slot2 => Slots.Count > 1 ? Slots[1] : EmptySlot;

    private static readonly SlotSnapshot EmptySlot = new();
}

public sealed class SlotSnapshot
{
    public int SlotId { get; init; }
    public string Status { get; init; } = "unknown";
    public string RecordingTime { get; init; } = "--:--:--";
    public bool IsActive { get; init; }
    public long RemainingBytes { get; init; }
    public long TotalBytes { get; init; }
    public double FreePercent { get; init; }
    public string CapacityLabel { get; init; } = "용량 정보 없음";
}
