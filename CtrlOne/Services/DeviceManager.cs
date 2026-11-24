using CtrlOne.Devices;
using Microsoft.AspNetCore.SignalR;

namespace CtrlOne.Services;

/// <summary>
/// 가변 멀티 소켓 장비 수명주기/순서/일괄 제어를 담당하는 중앙 서비스.
/// </summary>
public sealed class DeviceManager : IAsyncDisposable
{
    private readonly DeviceStore _store;
    private readonly PresetStore _presets;
    private readonly ActionStore _actions;
    private readonly IHubContext<Hubs.DashboardHub> _hub;
    private readonly ILogger<DeviceManager> _logger;
    private readonly object _sync = new();
    private readonly List<ISequenceDevice> _devices = [];
    private readonly List<SelectionPreset> _selectionPresets = [];
    private readonly List<OneTouchAction> _oneTouchActions = [];
    private readonly List<string> _recentLogs = [];
    private const int MaxLogEntries = 500;

    /// <summary>기본 원터치: 연결 장비 상태 확인 (장비 없어도 Stream Deck에서 실행 가능).</summary>
    private static readonly Guid DeviceStatusActionId = Guid.Parse("c7f01e01-5747-4a7a-9e01-000000000001");
    private const string DeviceStatusActionName = "연결 장비 상태 확인";
    private const string DeviceStatusCommand = "deviceStatus";

    private static readonly HashSet<string> AllowedActionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "play", "record", "stop", "preview", "previewOff", "stream", "streamOff", "recordSpill",
        DeviceStatusCommand
    };

    private static bool IsDeviceStatusCommand(string command) =>
        command.Equals(DeviceStatusCommand, StringComparison.OrdinalIgnoreCase);

    public DeviceManager(
        DeviceStore store,
        PresetStore presets,
        ActionStore actions,
        IHubContext<Hubs.DashboardHub> hub,
        ILogger<DeviceManager> logger)
    {
        _store = store;
        _presets = presets;
        _actions = actions;
        _hub = hub;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var records = _store.Load();
        foreach (var record in records)
        {
            var device = CreateDevice(record);
            Attach(device);
            lock (_sync)
                _devices.Add(device);
            await device.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _selectionPresets.Clear();
            _selectionPresets.AddRange(_presets.Load());
            _oneTouchActions.Clear();
            _oneTouchActions.AddRange(_actions.Load());
            EnsureDefaultActionsLocked();
        }

        await BroadcastStateAsync().ConfigureAwait(false);
        await BroadcastPresetsAsync().ConfigureAwait(false);
        await BroadcastActionsAsync().ConfigureAwait(false);
        await AppendLogAsync("시스템", $"devices.json에서 {_devices.Count}대 로드 완료").ConfigureAwait(false);
    }

    /// <summary>장비가 없어도 Stream Deck에서 바로 쓸 기본 원터치를 보장한다.</summary>
    private void EnsureDefaultActionsLocked()
    {
        if (_oneTouchActions.Any(a => a.Id == DeviceStatusActionId))
            return;

        _oneTouchActions.Insert(0, new OneTouchAction
        {
            Id = DeviceStatusActionId,
            Name = DeviceStatusActionName,
            Command = DeviceStatusCommand,
            DeviceIds = []
        });
        _actions.Save(_oneTouchActions);
    }

    public IReadOnlyList<DeviceSnapshot> GetSnapshots()
    {
        lock (_sync)
            return _devices.OrderBy(d => d.Order).Select(d => d.Snapshot).ToList();
    }

    public IReadOnlyList<string> GetRecentLogs()
    {
        lock (_sync)
            return _recentLogs.ToList();
    }

    public bool IsAddressRegistered(string address)
    {
        lock (_sync)
            return _devices.Any(d => string.Equals(d.Address, address, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DeviceSnapshot> AddDeviceAsync(string name, string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("장비 이름이 필요합니다.", nameof(name));
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("IP 주소가 필요합니다.", nameof(address));

        address = address.Trim();
        if (IsAddressRegistered(address))
            throw new InvalidOperationException($"이미 등록된 IP입니다: {address}");

        int order;
        lock (_sync)
            order = _devices.Count == 0 ? 0 : _devices.Max(d => d.Order) + 1;

        var record = new DeviceRecord
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Address = address,
            Order = order,
            IsSelected = true,
            DeviceType = "HyperDeck"
        };

        var device = CreateDevice(record);
        Attach(device);

        lock (_sync)
            _devices.Add(device);

        Persist();
        await device.StartAsync(ct).ConfigureAwait(false);
        await AppendLogAsync("시스템", $"장비 추가: {device.Name} ({device.Address})").ConfigureAwait(false);
        await BroadcastStateAsync().ConfigureAwait(false);
        return device.Snapshot;
    }

    public async Task<int> AddDiscoveredAsync(IEnumerable<DiscoveredDeviceSelection> selections, CancellationToken ct = default)
    {
        var added = 0;
        foreach (var item in selections)
        {
            if (string.IsNullOrWhiteSpace(item.Address))
                continue;
            if (IsAddressRegistered(item.Address))
                continue;

            var name = string.IsNullOrWhiteSpace(item.Name)
                ? $"HyperDeck_{item.Address.Split('.').LastOrDefault() ?? "0"}"
                : item.Name.Trim();

            await AddDeviceAsync(name, item.Address, ct).ConfigureAwait(false);
            added++;
        }

        return added;
    }

    public async Task RemoveDeviceAsync(Guid id)
    {
        ISequenceDevice? device;
        lock (_sync)
        {
            device = _devices.FirstOrDefault(d => d.Id == id);
            if (device is null)
                return;
            _devices.Remove(device);
            ReindexLocked();
        }

        Detach(device);
        await device.DisposeAsync().ConfigureAwait(false);
        Persist();
        await AppendLogAsync("시스템", $"장비 삭제: {device.Name}").ConfigureAwait(false);
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task RenameDeviceAsync(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("장비 이름이 필요합니다.", nameof(name));

        var device = FindRequired(id);
        device.Name = name.Trim();
        Persist();
        await AppendLogAsync("시스템", $"이름 변경: {device.Address} → {device.Name}").ConfigureAwait(false);
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task UpdateAddressAsync(Guid id, string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("IP 또는 호스트를 입력하세요.", nameof(address));

        var trimmed = address.Trim();
        var device = FindRequired(id);
        if (device.IsConnected)
            throw new InvalidOperationException("연결된 장비는 IP를 바꿀 수 없습니다. 연결 해제 후 변경하세요.");

        lock (_sync)
        {
            if (_devices.Any(d => d.Id != id && string.Equals(d.Address, trimmed, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"이미 등록된 주소입니다: {trimmed}");
        }

        if (device is HyperDeckDevice hyper)
            hyper.SetAddress(trimmed);
        else
            throw new NotSupportedException("이 장비 유형은 주소 변경을 지원하지 않습니다.");

        Persist();
        await AppendLogAsync("시스템", $"주소 변경: {device.Name} → {trimmed}").ConfigureAwait(false);
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task SetSelectedAsync(Guid id, bool selected)
    {
        var device = FindRequired(id);
        device.IsSelected = selected;
        Persist();
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task MoveDeviceAsync(Guid id, int direction)
    {
        lock (_sync)
        {
            var ordered = _devices.OrderBy(d => d.Order).ToList();
            var index = ordered.FindIndex(d => d.Id == id);
            if (index < 0)
                return;

            var target = index + direction;
            if (target < 0 || target >= ordered.Count)
                return;

            (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].Order = i;

            _devices.Clear();
            _devices.AddRange(ordered);
        }

        Persist();
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task BatchRecordAsync()
    {
        List<ISequenceDevice> targets;
        List<string> offlineNames;
        List<string> probingNames;
        List<string> unsupportedNames;
        lock (_sync)
        {
            var selected = _devices.Where(d => d.IsSelected).ToList();
            offlineNames = selected.Where(d => !d.IsConnected).Select(d => d.Name).ToList();
            probingNames = selected
                .Where(d => d.IsConnected && !d.Snapshot.Capabilities.CapabilitiesResolved)
                .Select(d => d.Name)
                .ToList();
            unsupportedNames = selected
                .Where(d => d.IsConnected &&
                            d.Snapshot.Capabilities.CapabilitiesResolved &&
                            !d.Snapshot.Capabilities.CanRecord)
                .Select(d => d.Name)
                .ToList();
            targets = selected
                .Where(d => d.IsConnected &&
                            d.Snapshot.Capabilities.CapabilitiesResolved &&
                            d.Snapshot.Capabilities.CanRecord &&
                            !IsRecordingStatus(d.Snapshot.TransportStatus))
                .ToList();
        }

        if (offlineNames.Count > 0)
            await AppendLogAsync("시스템", $"일괄 녹화 건너뜀(미연결): {string.Join(", ", offlineNames)}").ConfigureAwait(false);
        if (probingNames.Count > 0)
            await AppendLogAsync("시스템", $"일괄 녹화 건너뜀(기능 확인 중): {string.Join(", ", probingNames)}").ConfigureAwait(false);
        if (unsupportedNames.Count > 0)
            await AppendLogAsync("시스템", $"일괄 녹화 건너뜀(녹화 미지원): {string.Join(", ", unsupportedNames)}").ConfigureAwait(false);

        if (targets.Count == 0)
        {
            await AppendLogAsync("시스템", "일괄 녹화: 시작할 장비 없음 (연결·기능확인·녹화지원·미녹화만 대상)").ConfigureAwait(false);
            return;
        }

        await AppendLogAsync("시스템", $"일괄 녹화 시작 ({targets.Count}대)").ConfigureAwait(false);
        await Task.WhenAll(targets.Select(d => SafeTransportAsync(d, x => x.RecordAsync()))).ConfigureAwait(false);
    }

    public async Task BatchStopAsync()
    {
        List<ISequenceDevice> targets;
        List<string> offlineNames;
        List<string> probingNames;
        List<string> unsupportedNames;
        lock (_sync)
        {
            var selected = _devices.Where(d => d.IsSelected).ToList();
            offlineNames = selected.Where(d => !d.IsConnected).Select(d => d.Name).ToList();
            probingNames = selected
                .Where(d => d.IsConnected && !d.Snapshot.Capabilities.CapabilitiesResolved)
                .Select(d => d.Name)
                .ToList();
            unsupportedNames = selected
                .Where(d => d.IsConnected &&
                            d.Snapshot.Capabilities.CapabilitiesResolved &&
                            !d.Snapshot.Capabilities.CanStop)
                .Select(d => d.Name)
                .ToList();
            targets = selected
                .Where(d => d.IsConnected &&
                            d.Snapshot.Capabilities.CapabilitiesResolved &&
                            d.Snapshot.Capabilities.CanStop &&
                            IsRecordingStatus(d.Snapshot.TransportStatus))
                .ToList();
        }

        if (offlineNames.Count > 0)
            await AppendLogAsync("시스템", $"일괄 중지 건너뜀(미연결): {string.Join(", ", offlineNames)}").ConfigureAwait(false);
        if (probingNames.Count > 0)
            await AppendLogAsync("시스템", $"일괄 중지 건너뜀(기능 확인 중): {string.Join(", ", probingNames)}").ConfigureAwait(false);
        if (unsupportedNames.Count > 0)
            await AppendLogAsync("시스템", $"일괄 중지 건너뜀(중지 미지원): {string.Join(", ", unsupportedNames)}").ConfigureAwait(false);

        if (targets.Count == 0)
        {
            await AppendLogAsync("시스템", "일괄 중지: 대상 없음 (연결·기능확인·중지지원·녹화중만)").ConfigureAwait(false);
            return;
        }

        await AppendLogAsync("시스템", $"일괄 중지 ({targets.Count}대)").ConfigureAwait(false);
        await Task.WhenAll(targets.Select(d => SafeTransportAsync(d, x => x.StopTransportAsync()))).ConfigureAwait(false);
    }

    public IReadOnlyList<SelectionPreset> GetPresets()
    {
        lock (_sync)
            return _selectionPresets
                .Select(p => new SelectionPreset
                {
                    Id = p.Id,
                    Name = p.Name,
                    DeviceIds = p.DeviceIds.ToList()
                })
                .ToList();
    }

    public async Task<SelectionPreset> SavePresetAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("프리셋 이름이 필요합니다.", nameof(name));

        name = name.Trim();
        List<Guid> selectedIds;
        lock (_sync)
            selectedIds = _devices.Where(d => d.IsSelected).Select(d => d.Id).ToList();

        if (selectedIds.Count == 0)
            throw new InvalidOperationException("선택된 장비가 없습니다. 그룹 대상에 체크한 뒤 저장하세요.");

        SelectionPreset preset;
        lock (_sync)
        {
            preset = _selectionPresets.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? new SelectionPreset { Id = Guid.NewGuid(), Name = name };

            preset.Name = name;
            preset.DeviceIds = selectedIds;

            if (_selectionPresets.All(p => p.Id != preset.Id))
                _selectionPresets.Add(preset);

            _presets.Save(_selectionPresets);
        }

        await AppendLogAsync("시스템", $"프리셋 저장: {preset.Name} ({selectedIds.Count}대)").ConfigureAwait(false);
        await BroadcastPresetsAsync().ConfigureAwait(false);
        return preset;
    }

    public async Task DeletePresetAsync(Guid id)
    {
        string? name;
        lock (_sync)
        {
            var preset = _selectionPresets.FirstOrDefault(p => p.Id == id);
            if (preset is null)
                return;
            name = preset.Name;
            _selectionPresets.Remove(preset);
            _presets.Save(_selectionPresets);
        }

        await AppendLogAsync("시스템", $"프리셋 삭제: {name}").ConfigureAwait(false);
        await BroadcastPresetsAsync().ConfigureAwait(false);
    }

    public async Task ApplyPresetAsync(Guid id)
    {
        HashSet<Guid> ids;
        string name;
        List<string> missing;
        List<string> offline;
        int selectedCount;
        lock (_sync)
        {
            var preset = _selectionPresets.FirstOrDefault(p => p.Id == id)
                         ?? throw new KeyNotFoundException("프리셋을 찾을 수 없습니다.");
            ids = preset.DeviceIds.ToHashSet();
            name = preset.Name;

            var registeredIds = _devices.Select(d => d.Id).ToHashSet();
            missing = preset.DeviceIds
                .Where(deviceId => !registeredIds.Contains(deviceId))
                .Select(deviceId => deviceId.ToString()[..8])
                .ToList();

            foreach (var device in _devices)
                device.IsSelected = ids.Contains(device.Id);

            offline = _devices
                .Where(d => d.IsSelected && !d.IsConnected)
                .Select(d => d.Name)
                .ToList();
            selectedCount = _devices.Count(d => d.IsSelected);
        }

        Persist();
        await AppendLogAsync("시스템", $"프리셋 적용: {name} (선택 {selectedCount}대)").ConfigureAwait(false);

        if (missing.Count > 0)
            await AppendLogAsync("시스템", $"프리셋 경고: 목록에 없는 장비 {missing.Count}대 (삭제됐을 수 있음)").ConfigureAwait(false);

        if (offline.Count > 0)
            await AppendLogAsync("시스템", $"프리셋 경고: 미연결 장비 선택됨 — {string.Join(", ", offline)}").ConfigureAwait(false);

        if (selectedCount == 0)
            await AppendLogAsync("시스템", "프리셋 경고: 적용 가능한 등록 장비가 없습니다.").ConfigureAwait(false);

        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<OneTouchAction> GetActions()
    {
        lock (_sync)
            return _oneTouchActions
                .Select(CloneAction)
                .ToList();
    }

    public OneTouchAction? GetAction(Guid id)
    {
        lock (_sync)
        {
            var action = _oneTouchActions.FirstOrDefault(a => a.Id == id);
            return action is null ? null : CloneAction(action);
        }
    }

    /// <summary>
    /// 원터치 액션 저장(같은 Id면 수정, 없으면 추가). 이름 중복 시 기존 Id를 덮어쓴다.
    /// </summary>
    public async Task<OneTouchAction> SaveActionAsync(Guid? id, string name, string command, IEnumerable<Guid> deviceIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("액션 이름이 필요합니다.", nameof(name));
        if (string.IsNullOrWhiteSpace(command) || !AllowedActionCommands.Contains(command.Trim()))
            throw new ArgumentException($"지원하지 않는 명령입니다: {command}", nameof(command));

        name = name.Trim();
        command = NormalizeCommand(command);
        var ids = deviceIds?.Distinct().ToList() ?? [];
        var isStatus = IsDeviceStatusCommand(command);
        if (!isStatus && ids.Count == 0)
            throw new InvalidOperationException("대상 장비를 하나 이상 선택하세요.");
        if (isStatus)
            ids = [];

        if (ids.Count > 0)
        {
            lock (_sync)
            {
                var unknown = ids.Where(deviceId => _devices.All(d => d.Id != deviceId)).ToList();
                if (unknown.Count > 0)
                    throw new InvalidOperationException("목록에 없는 장비가 포함되어 있습니다.");
            }
        }

        OneTouchAction action;
        lock (_sync)
        {
            action = id.HasValue
                ? _oneTouchActions.FirstOrDefault(a => a.Id == id.Value)
                  ?? new OneTouchAction { Id = id.Value }
                : _oneTouchActions.FirstOrDefault(a =>
                      string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                  ?? new OneTouchAction { Id = Guid.NewGuid() };

            action.Name = name;
            action.Command = command;
            action.DeviceIds = ids;

            if (_oneTouchActions.All(a => a.Id != action.Id))
                _oneTouchActions.Add(action);

            _actions.Save(_oneTouchActions);
        }

        var targetLabel = IsDeviceStatusCommand(action.Command)
            ? "전체 장비"
            : $"{ids.Count}대";
        await AppendLogAsync("시스템", $"원터치 저장: {action.Name} ({action.Command}, {targetLabel})").ConfigureAwait(false);
        await BroadcastActionsAsync().ConfigureAwait(false);
        return CloneAction(action);
    }

    public async Task DeleteActionAsync(Guid id)
    {
        string? name;
        lock (_sync)
        {
            var action = _oneTouchActions.FirstOrDefault(a => a.Id == id);
            if (action is null)
                return;
            name = action.Name;
            _oneTouchActions.Remove(action);
            _actions.Save(_oneTouchActions);
        }

        await AppendLogAsync("시스템", $"원터치 삭제: {name}").ConfigureAwait(false);
        await BroadcastActionsAsync().ConfigureAwait(false);
    }

    public async Task RunActionAsync(Guid id)
    {
        OneTouchAction action;
        lock (_sync)
        {
            var found = _oneTouchActions.FirstOrDefault(a => a.Id == id)
                        ?? throw new KeyNotFoundException("원터치 액션을 찾을 수 없습니다.");
            action = CloneAction(found);
        }

        await AppendLogAsync("시스템", $"원터치 실행: {action.Name} ({action.Command})").ConfigureAwait(false);

        if (IsDeviceStatusCommand(action.Command))
        {
            await LogConnectedDeviceStatusAsync().ConfigureAwait(false);
            return;
        }

        List<ISequenceDevice> targets;
        List<string> missing;
        List<string> offline;
        lock (_sync)
        {
            var registered = _devices.ToDictionary(d => d.Id);
            missing = action.DeviceIds
                .Where(deviceId => !registered.ContainsKey(deviceId))
                .Select(deviceId => deviceId.ToString()[..8])
                .ToList();
            offline = action.DeviceIds
                .Where(deviceId => registered.TryGetValue(deviceId, out var d) && !d.IsConnected)
                .Select(deviceId => registered[deviceId].Name)
                .ToList();
            targets = action.DeviceIds
                .Where(deviceId => registered.TryGetValue(deviceId, out var d) && d.IsConnected)
                .Select(deviceId => registered[deviceId])
                .ToList();
        }

        if (missing.Count > 0)
            await AppendLogAsync("시스템", $"원터치 경고: 없는 장비 {missing.Count}대 건너뜀").ConfigureAwait(false);
        if (offline.Count > 0)
            await AppendLogAsync("시스템", $"원터치 건너뜀(미연결): {string.Join(", ", offline)}").ConfigureAwait(false);

        if (targets.Count == 0)
        {
            await AppendLogAsync("시스템", "원터치: 실행할 연결 장비 없음").ConfigureAwait(false);
            return;
        }

        await Task.WhenAll(targets.Select(d => RunCommandOnDeviceAsync(d, action.Command))).ConfigureAwait(false);
    }

    /// <summary>등록된 장비의 연결·수송 상태를 로그에 남긴다. 장비가 없어도 메시지를 남긴다.</summary>
    private async Task LogConnectedDeviceStatusAsync()
    {
        List<DeviceSnapshot> snaps;
        lock (_sync)
            snaps = _devices.OrderBy(d => d.Order).Select(d => d.Snapshot).ToList();

        if (snaps.Count == 0)
        {
            await AppendLogAsync("시스템", "연결 장비 상태: 등록된 장비 없음").ConfigureAwait(false);
            return;
        }

        var connected = snaps.Count(s => s.IsConnected);
        var offline = snaps.Count - connected;
        await AppendLogAsync(
            "시스템",
            $"연결 장비 상태 ({snaps.Count}대) — 연결 {connected} / 끊김 {offline}").ConfigureAwait(false);

        foreach (var snap in snaps)
        {
            await AppendLogAsync(
                "시스템",
                $"[{snap.Name}] {snap.Address} · {DescribeDeviceStatus(snap)}").ConfigureAwait(false);
        }
    }

    private static string DescribeDeviceStatus(DeviceSnapshot s)
    {
        if (!s.IsConnected)
            return "연결 끊김";
        if (!s.Capabilities.CapabilitiesResolved)
            return "연결됨 · 기능 확인 중";

        var transport = (s.TransportStatus ?? "").Trim().ToLowerInvariant();
        string mode;
        if (IsRecordingStatus(transport))
            mode = "녹화중";
        else if (transport is "play" or "playing")
            mode = "재생중";
        else if (transport is "preview" || s.IsPreviewMode)
            mode = "미리보기";
        else if (transport is "stopped" or "stop" or "")
            mode = "중지";
        else
            mode = string.IsNullOrWhiteSpace(s.TransportStatus) ? "알 수 없음" : s.TransportStatus;

        var onAir = s.IsStreaming ? " · OnAir" : "";
        var slot = s.ActiveSlotId is int slotId ? $" · 슬롯{slotId}" : "";
        return $"연결됨 · {mode}{onAir}{slot}";
    }

    private async Task RunCommandOnDeviceAsync(ISequenceDevice device, string command)
    {
        switch (command)
        {
            case "play":
                await InvokeTransportAsync(device.Id, "재생", d => d.PlayAsync(), s => RequireCap(s, c => c.CanPlay, "재생")).ConfigureAwait(false);
                break;
            case "record":
                await InvokeTransportAsync(device.Id, "녹화", d => d.RecordAsync(), s => RequireCap(s, c => c.CanRecord, "녹화")).ConfigureAwait(false);
                break;
            case "stop":
                await InvokeTransportAsync(device.Id, "중지", d => d.StopTransportAsync(), s => RequireCap(s, c => c.CanStop, "중지")).ConfigureAwait(false);
                break;
            case "preview":
                await InvokeTransportAsync(device.Id, "미리보기", d => d.PreviewAsync(true), s => RequireCap(s, c => c.CanPreview, "미리보기")).ConfigureAwait(false);
                break;
            case "previewOff":
                await InvokeTransportAsync(device.Id, "출력", d => d.PreviewAsync(false), s => RequireCap(s, c => c.CanPreview, "미리보기")).ConfigureAwait(false);
                break;
            case "stream":
                await InvokeTransportAsync(device.Id, "OnAir 시작", d => d.StreamAsync(true), s => RequireCap(s, c => c.CanStream, "OnAir")).ConfigureAwait(false);
                break;
            case "streamOff":
                await InvokeTransportAsync(device.Id, "OnAir 중지", d => d.StreamAsync(false), s => RequireCap(s, c => c.CanStream, "OnAir")).ConfigureAwait(false);
                break;
            case "recordSpill":
                await InvokeTransportAsync(device.Id, "Spill(슬롯 넘김)", d => d.RecordSpillAsync(), s => RequireCap(s, c => c.CanRecordSpill, "Spill")).ConfigureAwait(false);
                break;
            case DeviceStatusCommand:
                // 장비 단위가 아니라 RunActionAsync에서 일괄 처리
                break;
            default:
                await AppendLogAsync("시스템", $"원터치 알 수 없는 명령: {command}").ConfigureAwait(false);
                break;
        }
    }

    private static string NormalizeCommand(string command) =>
        command.Trim() switch
        {
            var c when c.Equals("play", StringComparison.OrdinalIgnoreCase) => "play",
            var c when c.Equals("record", StringComparison.OrdinalIgnoreCase) => "record",
            var c when c.Equals("stop", StringComparison.OrdinalIgnoreCase) => "stop",
            var c when c.Equals("preview", StringComparison.OrdinalIgnoreCase) => "preview",
            var c when c.Equals("previewOff", StringComparison.OrdinalIgnoreCase) => "previewOff",
            var c when c.Equals("stream", StringComparison.OrdinalIgnoreCase) => "stream",
            var c when c.Equals("streamOff", StringComparison.OrdinalIgnoreCase) => "streamOff",
            var c when c.Equals("recordSpill", StringComparison.OrdinalIgnoreCase) => "recordSpill",
            var c when c.Equals(DeviceStatusCommand, StringComparison.OrdinalIgnoreCase) => DeviceStatusCommand,
            _ => command.Trim()
        };

    private static OneTouchAction CloneAction(OneTouchAction a) =>
        new()
        {
            Id = a.Id,
            Name = a.Name,
            Command = a.Command,
            DeviceIds = a.DeviceIds.ToList()
        };

    public Task PlayAsync(Guid id) =>
        InvokeTransportAsync(id, "재생", d => d.PlayAsync(), s => RequireCap(s, c => c.CanPlay, "재생"));
    public Task RecordAsync(Guid id) =>
        InvokeTransportAsync(id, "녹화", d => d.RecordAsync(), s => RequireCap(s, c => c.CanRecord, "녹화"));
    public Task StopAsync(Guid id) =>
        InvokeTransportAsync(id, "중지", d => d.StopTransportAsync(), s => RequireCap(s, c => c.CanStop, "중지"));
    public Task SelectSlotAsync(Guid id, int slotId) =>
        InvokeTransportAsync(id, $"슬롯 {slotId} 선택", d => d.SelectSlotAsync(slotId), s => RequireCap(s, c => c.CanSelectSlot, "슬롯 선택"));
    public Task PreviewAsync(Guid id, bool enable) =>
        InvokeTransportAsync(id, enable ? "미리보기" : "출력", d => d.PreviewAsync(enable), s => RequireCap(s, c => c.CanPreview, "미리보기"));
    public Task RecordSpillAsync(Guid id) =>
        InvokeTransportAsync(id, "Spill(슬롯 넘김)", d => d.RecordSpillAsync(), s => RequireCap(s, c => c.CanRecordSpill, "Spill"));
    public Task StreamAsync(Guid id, bool enable) =>
        InvokeTransportAsync(id, enable ? "OnAir 시작" : "OnAir 중지", d => d.StreamAsync(enable), s => RequireCap(s, c => c.CanStream, "OnAir"));

    public async ValueTask DisposeAsync()
    {
        List<ISequenceDevice> devices;
        lock (_sync)
        {
            devices = _devices.ToList();
            _devices.Clear();
        }

        foreach (var device in devices)
        {
            Detach(device);
            await device.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ISequenceDevice CreateDevice(DeviceRecord record)
    {
        // 향후 AverMedia 등은 DeviceType 분기 또는 플러그인 팩토리로 확장
        return new HyperDeckDevice(
            record.Id,
            record.Name,
            record.Address,
            record.Order,
            record.IsSelected);
    }

    private void Attach(ISequenceDevice device)
    {
        device.LogReceived += OnDeviceLog;
        device.StatusChanged += OnDeviceStatusChanged;
    }

    private void Detach(ISequenceDevice device)
    {
        device.LogReceived -= OnDeviceLog;
        device.StatusChanged -= OnDeviceStatusChanged;
    }

    private void OnDeviceLog(ISequenceDevice device, string message)
    {
        _ = AppendLogLineAsync($"[{DateTime.Now:HH:mm:ss}] [{device.Name}] {message}");
    }

    private void OnDeviceStatusChanged(ISequenceDevice device)
    {
        _ = BroadcastDeviceAsync(device.Snapshot);
    }

    private ISequenceDevice FindRequired(Guid id)
    {
        lock (_sync)
        {
            return _devices.FirstOrDefault(d => d.Id == id)
                   ?? throw new KeyNotFoundException($"장비를 찾을 수 없습니다: {id}");
        }
    }

    private void ReindexLocked()
    {
        var ordered = _devices.OrderBy(d => d.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
        _devices.Clear();
        _devices.AddRange(ordered);
    }

    private void Persist()
    {
        List<DeviceRecord> records;
        lock (_sync)
        {
            records = _devices
                .OrderBy(d => d.Order)
                .Select(d => new DeviceRecord
                {
                    Id = d.Id,
                    Name = d.Name,
                    Address = d.Address,
                    Order = d.Order,
                    IsSelected = d.IsSelected,
                    DeviceType = d is HyperDeckDevice ? "HyperDeck" : d.GetType().Name
                })
                .ToList();
        }

        _store.Save(records);
    }

    private async Task InvokeTransportAsync(
        Guid id,
        string actionLabel,
        Func<ISequenceDevice, Task> action,
        Func<DeviceSnapshot, string?>? capabilityGate = null)
    {
        ISequenceDevice device;
        try
        {
            device = FindRequired(id);
        }
        catch (KeyNotFoundException)
        {
            await AppendLogAsync("시스템", $"{actionLabel} 실패: 등록되지 않은 장비").ConfigureAwait(false);
            return;
        }

        if (!device.IsConnected)
        {
            await AppendLogAsync("시스템", $"{actionLabel} 실패: [{device.Name}] 미연결 (재연결 대기 중)").ConfigureAwait(false);
            return;
        }

        if (capabilityGate is not null)
        {
            var reason = capabilityGate(device.Snapshot);
            if (reason is not null)
            {
                await AppendLogAsync("시스템", $"{actionLabel} 실패: [{device.Name}] {reason}").ConfigureAwait(false);
                return;
            }
        }

        await SafeTransportAsync(device, action).ConfigureAwait(false);
    }

    private static string? RequireCap(DeviceSnapshot snapshot, Func<DeviceCapabilities, bool> pred, string featureName)
    {
        var caps = snapshot.Capabilities;
        if (!caps.CapabilitiesResolved)
            return "지원 기능 확인 중 — 잠시 후 다시 시도";
        if (!pred(caps))
            return $"{featureName} 미지원 장비";
        return null;
    }

    private async Task SafeTransportAsync(ISequenceDevice device, Func<ISequenceDevice, Task> action)
    {
        try
        {
            await action(device).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await AppendLogAsync("시스템", $"명령 실패 [{device.Name}]: {ex.Message}").ConfigureAwait(false);
        }
    }

    private Task AppendLogAsync(string source, string message) =>
        AppendLogLineAsync($"[{DateTime.Now:HH:mm:ss}] [{source}] {message}");

    private async Task AppendLogLineAsync(string line)
    {
        lock (_sync)
        {
            _recentLogs.Add(line);
            while (_recentLogs.Count > MaxLogEntries)
                _recentLogs.RemoveAt(0);
        }

        _logger.LogInformation("{LogLine}", line);
        await _hub.Clients.All.SendAsync("Log", line).ConfigureAwait(false);
    }

    private async Task BroadcastStateAsync()
    {
        var snapshots = GetSnapshots();
        await _hub.Clients.All.SendAsync("DevicesUpdated", snapshots).ConfigureAwait(false);
    }

    private async Task BroadcastDeviceAsync(DeviceSnapshot snapshot)
    {
        await _hub.Clients.All.SendAsync("DeviceUpdated", snapshot).ConfigureAwait(false);
    }

    private async Task BroadcastPresetsAsync()
    {
        await _hub.Clients.All.SendAsync("PresetsUpdated", GetPresets()).ConfigureAwait(false);
    }

    private async Task BroadcastActionsAsync()
    {
        await _hub.Clients.All.SendAsync("ActionsUpdated", GetActions()).ConfigureAwait(false);
    }

    private static bool IsRecordingStatus(string? status)
    {
        var value = (status ?? "").Trim().ToLowerInvariant();
        return value is "record" or "recording";
    }
}
