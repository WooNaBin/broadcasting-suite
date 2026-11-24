using System.Diagnostics;
using System.Reflection;
using CtrlOne;
using CtrlOne.Hubs;
using CtrlOne.Services;
using Microsoft.Extensions.FileProviders;

const string ListenUrl = "http://127.0.0.1:5177";
const string MutexName = "CtrlOne.Dashboard";

using var mutex = new Mutex(true, MutexName, out var createdNew);
if (!createdNew)
{
    try
    {
        OpenBrowser($"{ListenUrl}/");
    }
    catch
    {
        // ignore
    }

    StartupConsole.EnableIfRequested(args);
    StartupConsole.WriteLine("CtrlOne이 이미 실행 중입니다. 기존 대시보드를 엽니다.");
    return;
}

StartupConsole.EnableIfRequested(args);

StartupConsole.WriteLine("CtrlOne HyperDeck Dashboard");
StartupConsole.WriteLine($"앱 폴더: {AppPaths.AppDirectory}");
StartupConsole.WriteLine($"devices.json: {AppPaths.DevicesJsonPath}");
StartupConsole.WriteLine("웹 서버 준비 중…");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppPaths.AppDirectory
});

builder.WebHost.UseUrls(ListenUrl);

// WinExe에서 불필요한 호스팅 로그 소음 줄이기 (--console 일 때만 요약 표시)
if (!StartupConsole.Enabled)
{
    builder.Logging.ClearProviders();
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

builder.Services.AddSingleton<DeviceStore>();
builder.Services.AddSingleton<PresetStore>();
builder.Services.AddSingleton<ActionStore>();
builder.Services.AddSingleton<DeviceManager>();
builder.Services.AddSingleton<HyperDeckDiscoveryService>();
builder.Services.AddSingleton<UiSessionGuard>();
builder.Services.AddSignalR();

var app = builder.Build();

var webRoot = CreateWebRootFileProvider();
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = webRoot
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = webRoot
});

app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapGet("/api/health", (DeviceManager devices) => Results.Ok(new
{
    status = "ok",
    app = "CtrlOne",
    devicesJson = AppPaths.DevicesJsonPath,
    actionsJson = AppPaths.ActionsJsonPath,
    actionCount = devices.GetActions().Count
}));
app.MapGet("/api/actions", (DeviceManager devices) =>
{
    var snaps = devices.GetSnapshots().ToDictionary(d => d.Id, d => d.Name);
    var list = devices.GetActions().Select(a => new
    {
        id = a.Id,
        name = a.Name,
        command = a.Command,
        deviceIds = a.DeviceIds,
        deviceNames = a.DeviceIds
            .Select(id => snaps.TryGetValue(id, out var n) ? n : id.ToString()[..8])
            .ToList()
    });
    return Results.Ok(list);
});
app.MapGet("/api/actions/{id:guid}", (Guid id, DeviceManager devices) =>
{
    var action = devices.GetAction(id);
    if (action is null)
        return Results.NotFound(new { error = "액션 없음" });
    var snaps = devices.GetSnapshots().ToDictionary(d => d.Id, d => d.Name);
    return Results.Ok(new
    {
        id = action.Id,
        name = action.Name,
        command = action.Command,
        deviceIds = action.DeviceIds,
        deviceNames = action.DeviceIds
            .Select(deviceId => snaps.TryGetValue(deviceId, out var n) ? n : deviceId.ToString()[..8])
            .ToList()
    });
});
app.MapPost("/api/actions/{id:guid}/run", async (Guid id, DeviceManager devices) =>
{
    try
    {
        await devices.RunActionAsync(id);
        return Results.Ok(new { ok = true, id });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { ok = false, error = "액션 없음" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});
app.MapPost("/api/goodbye", (UiSessionGuard sessions) =>
{
    sessions.NotifyUiClosing();
    return Results.Ok(new { stopping = true });
});

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var manager = app.Services.GetRequiredService<DeviceManager>();

lifetime.ApplicationStarted.Register(() =>
{
    StartupConsole.WriteLine($"수신 대기: {ListenUrl}/");
    StartupConsole.WriteLine("브라우저를 닫으면 프로그램이 종료됩니다.");
    if (StartupConsole.Enabled)
        StartupConsole.WriteLine("종료: 브라우저 닫기 또는 Ctrl+C");

    _ = Task.Run(async () =>
    {
        try
        {
            StartupConsole.WriteLine("장비 목록 로딩…");
            await manager.InitializeAsync(lifetime.ApplicationStopping);
            var count = manager.GetSnapshots().Count;
            StartupConsole.WriteLine(count == 0
                ? "등록된 장비 없음 (대시보드에서 추가)"
                : $"등록 장비 {count}대 연결 시도 중…");
        }
        catch (Exception ex)
        {
            StartupConsole.WriteLine($"장비 초기화 실패: {ex.Message}");
        }
    });

    _ = Task.Run(async () =>
    {
        await Task.Delay(400);
        StartupConsole.WriteLine($"브라우저 여는 중 → {ListenUrl}/");
        OpenBrowser($"{ListenUrl}/");
    });
});

lifetime.ApplicationStopping.Register(() =>
{
    StartupConsole.WriteLine("장비 연결 정리 중…");
    manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
    StartupConsole.WriteLine("종료 완료.");
});

app.Run();

static IFileProvider CreateWebRootFileProvider()
{
    var physicalWwwroot = Path.Combine(AppPaths.AppDirectory, "wwwroot");
    if (Directory.Exists(physicalWwwroot))
        return new PhysicalFileProvider(physicalWwwroot);

    var projectWwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    if (Directory.Exists(projectWwwroot))
        return new PhysicalFileProvider(projectWwwroot);

    return new ManifestEmbeddedFileProvider(
        Assembly.GetExecutingAssembly(),
        "wwwroot");
}

static void OpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { url },
                UseShellExecute = false
            });
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { url },
                UseShellExecute = false
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        StartupConsole.WriteLine($"브라우저 실행 실패: {ex.Message}");
        StartupConsole.WriteLine($"수동으로 열어 주세요: {url}");
    }
}
