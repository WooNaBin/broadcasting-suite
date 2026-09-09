using System.Diagnostics;
using System.Text.Json;
using BroadcastNasBridge.Apps;
using BroadcastNasBridge.Services;
using Microsoft.Extensions.FileProviders;

const string ListenUrl = "http://127.0.0.1:17820";
const string MutexName = "BroadcastNasBridge";

StartupConsole.EnableIfRequested(args);

var mutexName = OperatingSystem.IsWindows() ? $@"Local\{MutexName}" : MutexName;
using var mutex = new Mutex(true, mutexName, out var createdNew);
if (!createdNew)
{
    OpenBrowser(ResolveOpenUrl(args));
    StartupConsole.WriteLine("BroadcastNasBridge가 이미 실행 중입니다. 요청한 페이지를 엽니다.");
    return;
}

var appData = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "BroadcastNasBridge");
Directory.CreateDirectory(appData);

var contentRoot = AppContext.BaseDirectory;
// 개발 시 프로젝트 폴더를 ContentRoot로
var projectDir = FindProjectDir();
if (projectDir is not null)
    contentRoot = projectDir;

UiAssetSync.EnsureCopied(contentRoot);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot"),
});
builder.WebHost.UseUrls(ListenUrl);

if (!StartupConsole.Enabled)
    builder.Logging.ClearProviders();
else
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
        .WithExposedHeaders("X-File-Revision")));

builder.Services.AddSingleton(new NasConfigStore(appData));
builder.Services.AddSingleton<NasService>();
builder.Services.AddSingleton<UiSessionGuard>();
builder.Services.AddSingleton<FilesAppStore>();

var app = builder.Build();
app.UseCors();

// /worklog → /worklog/ (상대 CSS·모듈 경로 깨짐 방지)
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (path is "/worklog" or "/schedule" or "/files")
    {
        ctx.Response.Redirect(path + "/" + ctx.Request.QueryString, permanent: false);
        return;
    }
    await next();
});

var webRoot = new PhysicalFileProvider(Path.Combine(contentRoot, "wwwroot"));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webRoot });
app.UseStaticFiles(new StaticFileOptions { FileProvider = webRoot });
AppRouteMapper.MountAllAppFiles(app);

AppRouteMapper.MapBridgeApis(app);
AppRouteMapper.MapScheduleApp(app);
AppRouteMapper.MapWorkLogApp(app);
AppRouteMapper.MapFilesApp(app);

// setup page is static wwwroot/setup.html

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var nas = app.Services.GetRequiredService<NasService>();

lifetime.ApplicationStarted.Register(() =>
{
    StartupConsole.WriteLine("BroadcastNasBridge");
    StartupConsole.WriteLine($"수신 대기: {ListenUrl}/");
    StartupConsole.WriteLine($"설정: {app.Services.GetRequiredService<NasConfigStore>().ConfigPath}");
    StartupConsole.WriteLine("시작 인덱스·모든 탭을 닫으면 5초 후 종료합니다.");

    // 저장된 설정이 있으면 자동 연결 시도
    _ = Task.Run(() =>
    {
        try
        {
            var cfg = nas.Config;
            if (!string.IsNullOrWhiteSpace(cfg.Host) && !string.IsNullOrWhiteSpace(cfg.Username) &&
                (!string.IsNullOrEmpty(cfg.Password) || !cfg.RememberPassword))
            {
                if (!string.IsNullOrEmpty(cfg.Password))
                {
                    nas.Connect();
                    StartupConsole.WriteLine("저장된 NAS 설정으로 자동 연결됨");
                }
            }
        }
        catch (Exception ex)
        {
            StartupConsole.WriteLine($"자동 연결 실패: {ex.Message}");
        }
    });

    _ = Task.Run(async () =>
    {
        await Task.Delay(400);
        OpenBrowser(ResolveOpenUrl(args));
    });
});

lifetime.ApplicationStopping.Register(() =>
{
    try { nas.Disconnect(); } catch { /* ignore */ }
});

try
{
    app.Run();
}
catch (IOException ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
                             ex.InnerException?.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) == true)
{
    OpenBrowser(ResolveOpenUrl(args));
    StartupConsole.WriteLine("포트 17820이 이미 사용 중입니다. 기존 브리지를 엽니다.");
}

static string ResolveOpenUrl(string[] args)
{
    var path = args.FirstOrDefault(a =>
        a.StartsWith('/') && !a.StartsWith("//") && !a.StartsWith("--"));
    if (string.IsNullOrWhiteSpace(path)) path = "/";
    if (!path.StartsWith('/')) path = "/" + path;
    return $"{ListenUrl.TrimEnd('/')}{path}";
}

static void OpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo { FileName = "open", ArgumentList = { url }, UseShellExecute = false });
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo { FileName = "xdg-open", ArgumentList = { url }, UseShellExecute = false });
    }
    catch
    {
        // ignore
    }
}

static string? FindProjectDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "BroadcastNasBridge.csproj")))
            return dir.FullName;
    }
    return null;
}
