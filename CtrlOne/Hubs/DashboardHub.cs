using CtrlOne.Services;
using Microsoft.AspNetCore.SignalR;

namespace CtrlOne.Hubs;

/// <summary>
/// 대시보드 UI ↔ 서버 실시간 연동 허브.
/// </summary>
public sealed class DashboardHub : Hub
{
    private readonly DeviceManager _devices;
    private readonly HyperDeckDiscoveryService _discovery;
    private readonly UiSessionGuard _sessions;

    public DashboardHub(DeviceManager devices, HyperDeckDiscoveryService discovery, UiSessionGuard sessions)
    {
        _devices = devices;
        _discovery = discovery;
        _sessions = sessions;
    }

    public override async Task OnConnectedAsync()
    {
        _sessions.ClientConnected();
        await Clients.Caller.SendAsync("DevicesUpdated", _devices.GetSnapshots());
        await Clients.Caller.SendAsync("PresetsUpdated", _devices.GetPresets());
        await Clients.Caller.SendAsync("LogsSnapshot", _devices.GetRecentLogs());
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _sessions.ClientDisconnected();
        return base.OnDisconnectedAsync(exception);
    }

    public Task<NetworkScanResult> ScanNetwork() =>
        _discovery.ScanAsync(Context.ConnectionAborted);

    public Task<int> AddDiscoveredDevices(List<DiscoveredDeviceSelection> selections) =>
        _devices.AddDiscoveredAsync(selections ?? []);

    public Task AddDevice(string name, string address) =>
        _devices.AddDeviceAsync(name, address);

    public Task RemoveDevice(Guid id) =>
        _devices.RemoveDeviceAsync(id);

    public Task RenameDevice(Guid id, string name) =>
        _devices.RenameDeviceAsync(id, name);

    public Task UpdateDeviceAddress(Guid id, string address) =>
        _devices.UpdateAddressAsync(id, address);

    public Task SetSelected(Guid id, bool selected) =>
        _devices.SetSelectedAsync(id, selected);

    public Task MoveUp(Guid id) =>
        _devices.MoveDeviceAsync(id, -1);

    public Task MoveDown(Guid id) =>
        _devices.MoveDeviceAsync(id, +1);

    public Task BatchRecord() =>
        _devices.BatchRecordAsync();

    public Task BatchStop() =>
        _devices.BatchStopAsync();

    public Task Play(Guid id) =>
        _devices.PlayAsync(id);

    public Task Record(Guid id) =>
        _devices.RecordAsync(id);

    public Task Stop(Guid id) =>
        _devices.StopAsync(id);

    public Task SelectSlot(Guid id, int slotId) =>
        _devices.SelectSlotAsync(id, slotId);

    public Task Preview(Guid id, bool enable) =>
        _devices.PreviewAsync(id, enable);

    public Task RecordSpill(Guid id) =>
        _devices.RecordSpillAsync(id);

    public Task Stream(Guid id, bool enable) =>
        _devices.StreamAsync(id, enable);

    public Task SavePreset(string name) =>
        _devices.SavePresetAsync(name);

    public Task DeletePreset(Guid id) =>
        _devices.DeletePresetAsync(id);

    public Task ApplyPreset(Guid id) =>
        _devices.ApplyPresetAsync(id);
}
