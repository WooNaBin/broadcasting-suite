using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace CtrlOne.Devices;

/// <summary>
/// Blackmagic HyperDeck TCP Ethernet Protocol (port 9993) 클라이언트.
/// Companion의 줄 단위(\n) 버퍼 파싱 및 5초 주기 자동 재연결을 이식한다.
/// </summary>
public sealed class HyperDeckDevice : ISequenceDevice
{
    public const int DefaultPort = 9993;
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly StringBuilder _rxBuffer = new();
    private CancellationTokenSource? _lifetimeCts;
    private Task? _runTask;
    private TcpClient? _client;
    private NetworkStream? _stream;

    private const int RxBufferCapChars = 256 * 1024;
    private const int MaxSlots = 8;

    private string _transportStatus = "unknown";
    private readonly Dictionary<int, SlotState> _slots = new()
    {
        [1] = new SlotState(1),
        [2] = new SlotState(2),
    };
    private int? _activeSlotId;
    private int _parsingSlotId = 1;
    private bool _awaitingActiveSlot;
    private string _model = "";
    private int _slotCount = 2;
    private bool _capabilitiesResolved;
    private bool _isPreviewMode;
    private bool _isStreaming;
    private string _spillStatus = "idle";
    private string _spillMessage = "";
    private int? _spillFromSlot;
    private CancellationTokenSource? _spillFeedbackCts;
    private readonly HashSet<string> _supportedCommands = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>slot info 응답 블록 안에서만 슬롯 필드를 해당 슬롯에 기록한다.</summary>
    private bool _inSlotInfoBlock;
    private bool _rxOverflowWarned;

    public Guid Id { get; }
    public string Name { get; set; }
    public string Address { get; private set; }
    public int Port { get; }
    public int Order { get; set; }
    public bool IsSelected { get; set; }
    public bool IsConnected { get; private set; }

    public event Action<ISequenceDevice, string>? LogReceived;
    public event Action<ISequenceDevice>? StatusChanged;

    public DeviceSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                EnsureSlotsLocked();
                var slotCount = QueriedSlotCountLocked();
                var slots = new List<SlotSnapshot>(slotCount);
                for (var i = 1; i <= slotCount; i++)
                {
                    if (!_slots.TryGetValue(i, out var state))
                        state = new SlotState(i);
                    slots.Add(state.ToSnapshot(_activeSlotId == i));
                }

                return new DeviceSnapshot
                {
                    Id = Id,
                    Name = Name,
                    Address = Address,
                    Order = Order,
                    IsSelected = IsSelected,
                    IsConnected = IsConnected,
                    TransportStatus = _transportStatus,
                    ActiveSlotId = _activeSlotId,
                    SupportsSlotSelect = BuildCapabilities().CanSelectSlot,
                    IsPreviewMode = _isPreviewMode,
                    IsStreaming = _isStreaming,
                    SpillStatus = ResolveSpillStatusLocked(),
                    SpillMessage = _spillMessage,
                    Capabilities = BuildCapabilities(),
                    Slots = slots
                };
            }
        }
    }

    public HyperDeckDevice(Guid id, string name, string address, int order, bool isSelected = true, int port = DefaultPort)
    {
        Id = id;
        Name = name;
        Address = address;
        Order = order;
        IsSelected = isSelected;
        Port = port;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask is { IsCompleted: false })
            return Task.CompletedTask;

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunLoopAsync(_lifetimeCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopDeviceAsync()
    {
        if (_lifetimeCts is null)
            return;

        _lifetimeCts.Cancel();
        try
        {
            if (_runTask is not null)
                await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        await CloseSocketAsync().ConfigureAwait(false);
        _lifetimeCts.Dispose();
        _lifetimeCts = null;
        _runTask = null;
    }

    public Task PlayAsync() => SendCommandAsync("play");
    public Task RecordAsync() => SendCommandAsync("record");
    public Task StopTransportAsync() => SendCommandAsync("stop");

    public async Task RecordSpillAsync()
    {
        lock (_sync)
        {
            if (!IsRecordingStatus(_transportStatus))
            {
                _spillStatus = "error";
                _spillMessage = "녹화 중에만 Spill 가능";
                ScheduleSpillFeedbackClear();
            }
            else
            {
                _spillStatus = "pending";
                _spillMessage = "슬롯 넘김 요청 중…";
                _spillFromSlot = _activeSlotId;
            }
        }

        RaiseStatusChanged();

        lock (_sync)
        {
            if (_spillStatus == "error")
                throw new InvalidOperationException(_spillMessage);
        }

        var fromSlot = _spillFromSlot;
        await SendCommandAsync("record spill").ConfigureAwait(false);
        await SendCommandAsync("transport info").ConfigureAwait(false);
        await QueryAllSlotInfoAsync().ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(900).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_spillStatus != "pending")
                        return;

                    if (_activeSlotId is int now && fromSlot is int prev && now != prev && IsRecordingStatus(_transportStatus))
                    {
                        SetSpillDoneLocked(prev, now);
                    }
                    else if (IsRecordingStatus(_transportStatus))
                    {
                        _spillStatus = "done";
                        _spillMessage = fromSlot is int s
                            ? $"Spill 명령 전송됨 (활성 슬롯 {s})"
                            : "Spill 명령 전송됨";
                        ScheduleSpillFeedbackClear();
                    }
                    else
                    {
                        _spillStatus = "error";
                        _spillMessage = "Spill 후 녹화 상태 확인 실패";
                        ScheduleSpillFeedbackClear();
                    }
                }

                RaiseStatusChanged();
            }
            catch
            {
                // ignore
            }
        });
    }

    public async Task PreviewAsync(bool enable)
    {
        await SendCommandAsync($"preview: enable: {(enable ? "true" : "false")}").ConfigureAwait(false);
        lock (_sync)
        {
            _isPreviewMode = enable;
            if (enable)
                _transportStatus = "preview";
        }
        RaiseStatusChanged();
    }

    public async Task StreamAsync(bool enable)
    {
        await SendCommandAsync($"stream: enable: {(enable ? "true" : "false")}").ConfigureAwait(false);
        lock (_sync)
        {
            _isStreaming = enable;
        }
        RaiseStatusChanged();
    }

    public async Task SelectSlotAsync(int slotId)
    {
        // 슬롯 전환 원격 명령은 위험·불안정하여 UI에서 제거. 모니터링만 유지.
        EmitLog($"슬롯 선택 명령 무시 (모니터링 전용): {slotId}");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>연결 주소 변경. 재연결 루프가 새 Address를 사용한다.</summary>
    public void SetAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("주소를 입력하세요.", nameof(address));
        Address = address.Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await StopDeviceAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                EmitLog($"연결 오류: {ex.Message}");
                SetConnected(false);
            }

            if (ct.IsCancellationRequested)
                break;

            EmitLog($"재연결 대기 {ReconnectInterval.TotalSeconds:0}초...");
            try
            {
                await Task.Delay(ReconnectInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetConnected(false);
    }

    private async Task ConnectAndServeAsync(CancellationToken ct)
    {
        await CloseSocketAsync().ConfigureAwait(false);

        EmitLog($"연결 시도 {Address}:{Port}");
        var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync(Address, Port, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"장비 연결 시간 초과 ({ConnectTimeout.TotalSeconds:0}초): {Address}:{Port}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
        client.NoDelay = true;

        var stream = client.GetStream();
    lock (_sync)
    {
        _client = client;
        _stream = stream;
        _rxBuffer.Clear();
        _supportedCommands.Clear();
        _capabilitiesResolved = false;
        _model = "";
        _isStreaming = false;
        _inSlotInfoBlock = false;
        _parsingSlotId = 1;
    }

        SetConnected(true);
        EmitLog("연결됨");

        // 원격 제어 활성화 + 기능 탐색 + 상태 구독
        await WriteLineAsync("remote: enable: true", ct).ConfigureAwait(false);
        await WriteLineAsync("notify: transport: true slot: true", ct).ConfigureAwait(false);
        await WriteLineAsync("device info", ct).ConfigureAwait(false);
        await WriteLineAsync("commands", ct).ConfigureAwait(false);
        ScheduleCapabilitiesFallback(ct);
        await WriteLineAsync("transport info", ct).ConfigureAwait(false);
        await QueryAllSlotInfoAsync(ct).ConfigureAwait(false);

        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException)
            {
                EmitLog("소켓 읽기 실패 (연결 끊김)");
                break;
            }

            if (read == 0)
            {
                EmitLog("상대방이 연결을 종료함");
                break;
            }

            var chunk = Encoding.UTF8.GetString(buffer, 0, read);
            AppendAndParse(chunk);
        }

        SetConnected(false);
        await CloseSocketAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Companion 방식: TCP 스트림을 StringBuilder에 누적한 뒤 \n 기준으로 라인을 분리해 파싱한다.
    /// 미완료 prefix가 상한을 넘으면 버리고 경고를 한 번 남긴다.
    /// </summary>
    private void AppendAndParse(string chunk)
    {
        List<string> lines;
        var overflow = false;
        lock (_sync)
        {
            _rxBuffer.Append(chunk);
            lines = ExtractCompleteLines(_rxBuffer);
            if (_rxBuffer.Length > RxBufferCapChars)
            {
                _rxBuffer.Clear();
                overflow = !_rxOverflowWarned;
                _rxOverflowWarned = true;
            }
            else if (_rxBuffer.Length == 0)
            {
                _rxOverflowWarned = false;
            }
        }

        if (overflow)
            EmitLog($"경고: 수신 버퍼 상한({RxBufferCapChars}자) 초과 — 미완료 데이터 폐기");

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            EmitLog($"Rx: {line}");
            ApplyProtocolLine(line);
            NoticeCommandResultLine(line);
        }

        RaiseStatusChanged();
    }

    private void NoticeCommandResultLine(string line)
    {
        // HyperDeck: 1xx = error, 2xx = success header
        var codeMatch = Regex.Match(line, @"^\s*(\d{3})\s+(.*)$");
        if (!codeMatch.Success)
            return;

        var code = int.Parse(codeMatch.Groups[1].Value);
        var text = codeMatch.Groups[2].Value.Trim();
        lock (_sync)
        {
            if (_spillStatus != "pending")
                return;

            if (code >= 100 && code < 200)
            {
                _spillStatus = "error";
                _spillMessage = string.IsNullOrWhiteSpace(text) ? $"Spill 실패 ({code})" : $"Spill 실패: {text}";
                ScheduleSpillFeedbackClear();
            }
        }
    }

    private static List<string> ExtractCompleteLines(StringBuilder buffer)
    {
        var text = buffer.ToString();
        var lines = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;

            var length = i - start;
            if (length > 0 && text[i - 1] == '\r')
                length--;

            lines.Add(text.Substring(start, length));
            start = i + 1;
        }

        if (start > 0)
        {
            buffer.Clear();
            if (start < text.Length)
                buffer.Append(text.AsSpan(start));
        }

        return lines;
    }

    private void ApplyProtocolLine(string line)
    {
        // commands XML: <command name="play"> ...
        foreach (Match cmdMatch in Regex.Matches(line, @"name\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase))
            RegisterSupportedCommand(cmdMatch.Groups[1].Value);

        // HyperDeck 응답 헤더: "202 slot info:" / "208 transport info:"
        var header = Regex.Match(line, @"^\s*(\d{3})\s+(.+?)\s*:?\s*$");
        if (header.Success)
        {
            var kind = header.Groups[2].Value.Trim().ToLowerInvariant();
            lock (_sync)
            {
                _inSlotInfoBlock = kind.StartsWith("slot info", StringComparison.Ordinal);
            }
            return;
        }

        var match = Regex.Match(line, @"^\s*([^:]+)\s*:\s*(.*)\s*$");
        if (!match.Success)
            return;

        var key = match.Groups[1].Value.Trim().ToLowerInvariant();
        var value = match.Groups[2].Value.Trim().Trim('"');

        lock (_sync)
        {
            switch (key)
            {
                case "status":
                    if (IsSlotStatus(value))
                    {
                        if (_inSlotInfoBlock)
                            ApplySlotField("status", value);
                    }
                    else if (!value.All(char.IsDigit))
                    {
                        _transportStatus = value;
                        _isPreviewMode = value.Equals("preview", StringComparison.OrdinalIgnoreCase);
                        _awaitingActiveSlot = true;
                        if (!IsRecordingStatus(value) && _spillStatus is "pending" or "ready")
                        {
                            if (_spillStatus == "pending")
                            {
                                _spillStatus = "error";
                                _spillMessage = "Spill 중 녹화가 종료됨";
                                ScheduleSpillFeedbackClear();
                            }
                        }
                    }
                    break;
                case "model":
                    _model = value;
                    break;
                case "slot count":
                    if (int.TryParse(value, out var count) && count > 0)
                    {
                        var clamped = Math.Clamp(count, 1, MaxSlots);
                        var grew = clamped > _slotCount;
                        _slotCount = clamped;
                        EnsureSlotsLocked();
                        if (grew)
                            ScheduleSlotInfoRefresh();
                    }
                    break;
                case "slot id":
                    if (int.TryParse(value, out var slotId))
                    {
                        // slot info 블록에서만 파싱 대상 슬롯을 바꾼다.
                        // transport info의 slot id는 활성 슬롯만 갱신 (슬롯1 데이터 오염 방지).
                        if (_inSlotInfoBlock)
                            _parsingSlotId = slotId;
                        if (_awaitingActiveSlot)
                        {
                            NoticeActiveSlotLocked(slotId);
                            _awaitingActiveSlot = false;
                        }
                        else if (!_inSlotInfoBlock)
                        {
                            NoticeActiveSlotLocked(slotId);
                        }
                    }
                    break;
                case "recording time":
                    if (_inSlotInfoBlock)
                        ApplySlotField("recording time", FormatTimecode(value));
                    break;
                case "remaining size":
                    if (_inSlotInfoBlock)
                        ApplySlotField("remaining size", value);
                    break;
                case "total size":
                    if (_inSlotInfoBlock)
                        ApplySlotField("total size", value);
                    break;
                case "slot":
                    // notify: 활성 슬롯만 (파싱 컨텍스트는 유지)
                    if (int.TryParse(value, out var notifySlot))
                        NoticeActiveSlotLocked(notifySlot);
                    break;
                case "enable":
                    // preview/stream notify payloads may include enable
                    break;
                default:
                    if (_inSlotInfoBlock)
                        ApplySlotField(key, value);
                    break;
            }
        }
    }

    private void RegisterSupportedCommand(string rawName)
    {
        var name = rawName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_sync)
        {
            _supportedCommands.Add(name);
            // "slot select" / "record spill" 등 공백 포함 이름도 첫 토큰으로 보조 등록
            var head = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
            _supportedCommands.Add(head);
            _capabilitiesResolved = true;
        }
    }

    private DeviceCapabilities BuildCapabilities()
    {
        lock (_sync)
        {
            var known = _capabilitiesResolved;

            // commands 응답 전에는 버튼을 숨기고 일괄 명령에서도 제외 (오작동 방지)
            if (!known)
            {
                return new DeviceCapabilities
                {
                    Model = _model,
                    SlotCount = _slotCount,
                    CapabilitiesResolved = false
                };
            }

            bool Has(string command) =>
                _supportedCommands.Contains(command) ||
                _supportedCommands.Any(c => c.Equals(command, StringComparison.OrdinalIgnoreCase) ||
                                            c.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase));

            var canPlay = Has("play");
            var canRecord = Has("record");
            var canStop = Has("stop") || canPlay || canRecord;
            var canPreview = Has("preview");
            var canSelectSlot = false; // 슬롯 전환 UI/명령 비활성 (모니터링만)
            var canSpill = Has("record spill") || _supportedCommands.Any(c => c.Contains("spill", StringComparison.OrdinalIgnoreCase));
            var canStream = _supportedCommands.Any(c =>
                c.Contains("stream", StringComparison.OrdinalIgnoreCase) ||
                c.Contains("onair", StringComparison.OrdinalIgnoreCase) ||
                c.Contains("on air", StringComparison.OrdinalIgnoreCase));

            return new DeviceCapabilities
            {
                Model = _model,
                SlotCount = _slotCount,
                CanPlay = canPlay,
                CanStop = canStop,
                CanRecord = canRecord,
                CanPreview = canPreview,
                CanSelectSlot = canSelectSlot,
                CanRecordSpill = canSpill,
                CanStream = canStream,
                CapabilitiesResolved = true
            };
        }
    }

    /// <summary>
    /// commands XML이 비어 있거나 파싱 실패해도 일정 시간 후 기본 HyperDeck 전송 기능으로 확정한다.
    /// </summary>
    private void ScheduleCapabilitiesFallback(CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2.5), ct).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_capabilitiesResolved)
                        return;

                    _supportedCommands.Add("play");
                    _supportedCommands.Add("stop");
                    _supportedCommands.Add("record");
                    _supportedCommands.Add("slot select");
                    _supportedCommands.Add("slot");
                    _capabilitiesResolved = true;
                    EmitLog("commands 응답 지연 — 기본 재생/녹화/중지 기능으로 확정");
                }

                RaiseStatusChanged();
            }
            catch (OperationCanceledException)
            {
                // disconnect
            }
        }, CancellationToken.None);
    }

    private static bool IsSlotStatus(string value)
    {
        return value is "mounted" or "empty" or "mounting" or "error" or "unknown";
    }

    private void ApplySlotField(string key, string value)
    {
        var target = CurrentSlotForWrite();
        switch (key)
        {
            case "status":
                target.Status = value;
                break;
            case "recording time":
                target.RecordingTime = value;
                break;
            case "remaining size":
                if (long.TryParse(value, out var remaining) && remaining >= 0)
                    target.RemainingBytes = remaining;
                break;
            case "total size":
                if (long.TryParse(value, out var total) && total >= 0)
                    target.TotalBytes = total;
                break;
        }
    }

    private SlotState CurrentSlotForWrite()
    {
        var id = _parsingSlotId < 1 ? 1 : Math.Min(_parsingSlotId, MaxSlots);
        if (!_slots.TryGetValue(id, out var slot))
        {
            slot = new SlotState(id);
            _slots[id] = slot;
        }
        return slot;
    }

    private int QueriedSlotCountLocked()
    {
        return Math.Clamp(Math.Max(2, _slotCount), 1, MaxSlots);
    }

    private void EnsureSlotsLocked()
    {
        var n = QueriedSlotCountLocked();
        for (var i = 1; i <= n; i++)
        {
            if (!_slots.ContainsKey(i))
                _slots[i] = new SlotState(i);
        }
    }

    private async Task QueryAllSlotInfoAsync(CancellationToken ct = default)
    {
        int n;
        lock (_sync)
        {
            EnsureSlotsLocked();
            n = QueriedSlotCountLocked();
        }

        for (var i = 1; i <= n; i++)
            await WriteLineAsync($"slot info: slot id: {i}", ct).ConfigureAwait(false);
    }

    private void ScheduleSlotInfoRefresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await QueryAllSlotInfoAsync().ConfigureAwait(false);
            }
            catch
            {
                // disconnect / not ready
            }
        }, CancellationToken.None);
    }

    private static string FormatTimecode(string raw)
    {
        // HyperDeck은 초 단위 정수 또는 timecode 문자열을 줄 수 있음
        if (Regex.IsMatch(raw, @"^\d{2}:\d{2}:\d{2}"))
            return raw.Length >= 8 ? raw[..8] : raw;

        if (int.TryParse(raw, out var seconds) && seconds >= 0)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        return raw;
    }

    private static bool IsRecordingStatus(string status)
    {
        var value = (status ?? "").Trim().ToLowerInvariant();
        return value is "record" or "recording";
    }

    private string ResolveSpillStatusLocked()
    {
        if (_spillStatus is "pending" or "done" or "error")
            return _spillStatus;

        if (!IsConnected)
            return "idle";

        return IsRecordingStatus(_transportStatus) ? "ready" : "blocked";
    }

    private void NoticeActiveSlotLocked(int slotId)
    {
        var prev = _activeSlotId;
        _activeSlotId = slotId;
        if (_spillStatus == "pending" &&
            prev is int from &&
            from != slotId &&
            IsRecordingStatus(_transportStatus))
        {
            SetSpillDoneLocked(from, slotId);
        }
    }

    private void SetSpillDoneLocked(int fromSlot, int toSlot)
    {
        _spillStatus = "done";
        _spillMessage = $"Spill 완료: 슬롯 {fromSlot} → {toSlot}";
        _spillFromSlot = null;
        ScheduleSpillFeedbackClear();
        EmitLog(_spillMessage);
    }

    private void ScheduleSpillFeedbackClear()
    {
        _spillFeedbackCts?.Cancel();
        _spillFeedbackCts?.Dispose();
        _spillFeedbackCts = new CancellationTokenSource();
        var token = _spillFeedbackCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3500, token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_spillStatus is "done" or "error")
                    {
                        _spillStatus = "idle";
                        _spillMessage = "";
                    }
                }

                RaiseStatusChanged();
            }
            catch (OperationCanceledException)
            {
                // replaced
            }
        }, CancellationToken.None);
    }

    private async Task SendCommandAsync(string command)
    {
        EmitLog($"Tx: {command}");
        try
        {
            await WriteLineAsync(command, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            EmitLog($"명령 전송 실패 ({command}): {ex.Message}");
            throw;
        }
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            NetworkStream? stream;
            lock (_sync)
                stream = _stream;

            if (stream is null || !IsConnected)
                throw new InvalidOperationException("장비가 연결되어 있지 않습니다.");

            var payload = Encoding.UTF8.GetBytes(line + "\n");
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task CloseSocketAsync()
    {
        NetworkStream? stream;
        TcpClient? client;
        lock (_sync)
        {
            stream = _stream;
            client = _client;
            _stream = null;
            _client = null;
            _rxBuffer.Clear();
        }

        if (stream is not null)
            await stream.DisposeAsync().ConfigureAwait(false);
        client?.Dispose();
    }

    private void SetConnected(bool connected)
    {
        lock (_sync)
        {
            if (IsConnected == connected)
                return;
            IsConnected = connected;
            if (!connected)
                _transportStatus = "disconnected";
        }

        RaiseStatusChanged();
    }

    private void EmitLog(string message)
    {
        LogReceived?.Invoke(this, message);
    }

    private CancellationTokenSource? _statusRaiseCts;
    private readonly object _statusRaiseSync = new();

    private void RaiseStatusChanged()
    {
        // TCP 라인마다 UI를 두드리지 않도록 짧은 디바운스
        lock (_statusRaiseSync)
        {
            _statusRaiseCts?.Cancel();
            _statusRaiseCts?.Dispose();
            _statusRaiseCts = new CancellationTokenSource();
            var token = _statusRaiseCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;
                    StatusChanged?.Invoke(this);
                }
                catch (OperationCanceledException)
                {
                    // coalesced
                }
            }, CancellationToken.None);
        }
    }

    private sealed class SlotState(int slotId)
    {
        public int SlotId { get; } = slotId;
        public string Status { get; set; } = "unknown";
        public string RecordingTime { get; set; } = "--:--:--";
        public long RemainingBytes { get; set; }
        public long TotalBytes { get; set; }

        public SlotSnapshot ToSnapshot(bool isActive)
        {
            var freePercent = TotalBytes > 0
                ? Math.Clamp(RemainingBytes * 100.0 / TotalBytes, 0, 100)
                : 0;

            return new SlotSnapshot
            {
                SlotId = SlotId,
                Status = Status,
                RecordingTime = RecordingTime,
                IsActive = isActive,
                RemainingBytes = RemainingBytes,
                TotalBytes = TotalBytes,
                FreePercent = freePercent,
                CapacityLabel = FormatCapacityLabel(RemainingBytes, TotalBytes)
            };
        }

        private static string FormatCapacityLabel(long remaining, long total)
        {
            if (total <= 0 && remaining <= 0)
                return "용량 정보 없음";
            if (total <= 0)
                return FormatBytes(remaining);
            return $"{FormatBytes(remaining)} / {FormatBytes(total)}";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            const double kb = 1024d;
            const double mb = kb * 1024d;
            const double gb = mb * 1024d;
            const double tb = gb * 1024d;

            if (bytes >= tb) return $"{bytes / tb:0.##} TB";
            if (bytes >= gb) return $"{bytes / gb:0.##} GB";
            if (bytes >= mb) return $"{bytes / mb:0.##} MB";
            if (bytes >= kb) return $"{bytes / kb:0.##} KB";
            return $"{bytes} B";
        }
    }
}
