using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BroadcastNasBridge.Services;

namespace BroadcastNasBridge.Apps;

public static class AppRouteMapper
{
    public static void MountAllAppFiles(WebApplication app)
    {
        MountAppFiles(app, "/schedule", "schedule");
        MountAppFiles(app, "/worklog", "worklog");
        MountAppFiles(app, "/files", "files");
    }

    public static void MapBridgeApis(WebApplication app)
    {
        app.MapGet("/api/health", (NasService nas) => Results.Ok(nas.Status()));
        app.MapGet("/api/status", (NasService nas) => Results.Ok(nas.Status()));

        app.MapPost("/api/session/ping", (UiSessionGuard sessions) =>
        {
            sessions.Ping();
            return Results.Ok(new { ok = true });
        });
        app.MapPost("/api/session/hello", (UiSessionGuard sessions) =>
        {
            sessions.ClientConnected();
            return Results.Ok(new { ok = true });
        });
        app.MapPost("/api/session/bye", (UiSessionGuard sessions) =>
        {
            sessions.NotifyUiClosing();
            return Results.Ok(new { stopping = true });
        });
        app.MapPost("/api/goodbye", (UiSessionGuard sessions) =>
        {
            sessions.NotifyUiClosing();
            return Results.Ok(new { stopping = true });
        });

        app.MapGet("/api/setup", (NasService nas) =>
        {
            var cfg = nas.GetSetupDefaults();
            return Results.Ok(new
            {
                config = new
                {
                    cfg.Host,
                    cfg.Username,
                    password = cfg.RememberPassword ? cfg.Password : "",
                    cfg.RememberPassword,
                    cfg.TempShare,
                    cfg.PermanentShare,
                    cfg.ScheduleRelativePath,
                    cfg.WorkLogRelativePath,
                    cfg.MediaRelativePath,
                    cfg.ScheduleJsonFile,
                    cfg.LocalPath,
                },
                status = nas.Status(),
            });
        });

        app.MapPost("/api/setup", (NasConfig body, NasService nas) =>
        {
            try
            {
                nas.SaveConfig(body);
                nas.Connect(body);
                return Results.Ok(nas.Status());
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        app.MapPost("/api/connect", (NasConfig? body, NasService nas) =>
        {
            try
            {
                nas.Connect(body);
                return Results.Ok(nas.Status());
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        app.MapPost("/api/active-schedule-file", (ActiveScheduleFileRequest? body, NasService nas) =>
        {
            try
            {
                var name = body?.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest(new { message = "name이 필요합니다." });
                nas.SetActiveScheduleJsonFile(name);
                return Results.Ok(new { scheduleJsonFile = Path.GetFileName(name) });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        app.MapPost("/api/disconnect", (NasService nas) =>
        {
            nas.Disconnect();
            return Results.Ok(nas.Status());
        });

        app.MapGet("/api/lan-devices", async (CancellationToken ct) =>
        {
            try { return Results.Ok(await LanScanner.DiscoverAsync(ct)); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
    }

    public static void MapScheduleApp(WebApplication app)
    {
        var g = app.MapGroup("/schedule/api");
        g.MapGet("/status", (NasService nas) =>
        {
            try
            {
                if (nas.IsConnected) nas.EnsureConnected();
            }
            catch { /* status only */ }
            return Results.Ok(new
            {
                app = "BroadcastingSchedule",
                port = 17820,
                connected = nas.IsConnected,
                root = nas.ScheduleRoot,
                via = "BroadcastNasBridge",
            });
        });

        g.MapPost("/session/ping", (UiSessionGuard s) => { s.Ping(); return Results.Ok(new { ok = true }); });
        g.MapPost("/session/bye", (UiSessionGuard s) => { s.NotifyUiClosing(); return Results.Ok(new { stopping = true }); });
        g.MapPost("/connect", (ConnectBody body, NasService nas) =>
        {
            try
            {
                if (!nas.IsConnected)
                {
                    var cfg = nas.Config;
                    if (!string.IsNullOrWhiteSpace(body.Host)) cfg.Host = body.Host;
                    if (!string.IsNullOrWhiteSpace(body.Share))
                    {
                        // 레거시 UI가 Temp DATA\_data 를 share로 보낼 수 있음 → 무시하고 브리지 설정 사용
                    }
                    if (!string.IsNullOrWhiteSpace(body.Username)) cfg.Username = body.Username;
                    if (!string.IsNullOrWhiteSpace(body.Password)) cfg.Password = body.Password;
                    nas.Connect(cfg);
                }
                return Results.Ok(new { connected = true, root = nas.ScheduleRoot });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
        g.MapPost("/disconnect", (NasService nas) =>
        {
            // 공유 브리지이므로 앱별 disconnect는 no-op (전체는 허브에서)
            return Results.Ok(new { connected = nas.IsConnected });
        });

        g.MapGet("/files", (NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                return Results.Ok(nas.ScheduleStore.GetJsonFiles());
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/file", (HttpResponse response, string name, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                var (json, revision) = nas.ScheduleStore.ReadJsonWithRevision(name);
                response.Headers["X-File-Revision"] = revision;
                return Results.Text(json, "application/json; charset=utf-8");
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPut("/file", async (string name, HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                var expected = request.Headers["X-Expected-Revision"].FirstOrDefault();
                var result = nas.ScheduleStore.WriteJsonWithRevision(name, body, expected);
                if (!result.Saved)
                {
                    return Results.Json(
                        new { message = "파일이 다른 곳에서 변경되었습니다.", revision = result.Revision, content = result.Content },
                        statusCode: StatusCodes.Status409Conflict);
                }
                return Results.Ok(new { saved = true, revision = result.Revision });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/backup", async (string name, HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                using var reader = new StreamReader(request.Body);
                return Results.Ok(nas.ScheduleStore.CreateBackup(name, await reader.ReadToEndAsync()));
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/backups", (string name, NasService nas) =>
        {
            try { nas.EnsureConnected(); return Results.Ok(nas.ScheduleStore.ListBackups(name)); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/restore", (HttpResponse response, string name, string backup, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                var (json, revision) = nas.ScheduleStore.RestoreBackupWithRevision(name, backup);
                response.Headers["X-File-Revision"] = revision;
                return Results.Text(json, "application/json; charset=utf-8");
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/documents", (NasService nas) =>
        {
            try { nas.EnsureConnected(); return Results.Ok(nas.ScheduleStore.ListOfficialDocuments()); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/document", (string name, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                var (bytes, contentType) = nas.ScheduleStore.ReadOfficialDocument(name);
                return Results.File(bytes, contentType);
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/document/upload", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                if (!request.HasFormContentType) return Results.BadRequest(new { message = "multipart 필요" });
                var form = await request.ReadFormAsync();
                var file = form.Files.FirstOrDefault()
                    ?? throw new InvalidOperationException("파일이 없습니다.");
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                return Results.Ok(nas.ScheduleStore.UploadOfficialDocument(
                    ms.ToArray(),
                    file.FileName,
                    form["scheduleId"].ToString(),
                    form["scheduleDate"].ToString(),
                    form["scheduleTitle"].ToString()));
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/document/link", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                var body = await JsonSerializer.DeserializeAsync<DocumentLinkBody>(request.Body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }) ?? throw new InvalidOperationException("본문 없음");
                return Results.Ok(nas.ScheduleStore.LinkOfficialDocument(
                    body.FileName, body.ScheduleId, body.ScheduleDate, body.ScheduleTitle));
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/document/index", (NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                return Results.Text(nas.ScheduleStore.ReadOfficialDocumentIndex(), "application/json; charset=utf-8");
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPut("/document/index", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                using var reader = new StreamReader(request.Body);
                nas.ScheduleStore.WriteOfficialDocumentIndex(await reader.ReadToEndAsync());
                return Results.Ok(new { saved = true });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/lan-devices", async (CancellationToken ct) =>
        {
            try { return Results.Ok(await LanScanner.DiscoverAsync(ct)); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
    }

    public static void MapWorkLogApp(WebApplication app)
    {
        var store = new WorkLogFileStore();

        var g = app.MapGroup("/worklog/api");
        g.MapGet("/status", (NasService nas) =>
        {
            try
            {
                if (nas.IsConnected)
                    BindWorkLog(store, nas);
            }
            catch
            {
                /* status only — 바인딩 실패해도 응답은 반환 */
            }
            return Results.Ok(new
            {
                app = "WorkLog",
                port = 17820,
                connected = nas.IsConnected && store.IsConnected,
                root = store.Root,
                scheduleRoot = store.ScheduleRoot,
                via = "BroadcastNasBridge",
                nasConnected = nas.IsConnected,
            });
        });
        g.MapPost("/session/ping", (UiSessionGuard s) => { s.Ping(); return Results.Ok(new { ok = true }); });
        g.MapPost("/session/bye", (UiSessionGuard s) => { s.NotifyUiClosing(); return Results.Ok(new { stopping = true }); });
        g.MapPost("/connect", (ConnectBody body, NasService nas) =>
        {
            try
            {
                if (!nas.IsConnected)
                {
                    var cfg = nas.Config;
                    if (!string.IsNullOrWhiteSpace(body.Host)) cfg.Host = body.Host;
                    if (!string.IsNullOrWhiteSpace(body.Username)) cfg.Username = body.Username;
                    if (!string.IsNullOrWhiteSpace(body.Password)) cfg.Password = body.Password;
                    nas.Connect(cfg);
                }
                BindWorkLog(store, nas);
                if (!store.IsConnected)
                    return Results.BadRequest(new { message = "작업일지 경로를 열 수 없습니다. 브리지 NAS 설정을 확인하세요." });
                return Results.Ok(new { connected = true, root = store.Root, scheduleRoot = store.ScheduleRoot });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/disconnect", () => Results.Ok(new { connected = true }));
        g.MapPost("/ensure-layout", (NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/file", (HttpResponse response, string path, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                var (content, revision) = store.ReadTextWithRevision(path);
                response.Headers["X-File-Revision"] = revision;
                var contentType = path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                    ? "application/x-ndjson; charset=utf-8"
                    : "application/json; charset=utf-8";
                return Results.Text(content, contentType);
            }
            catch (FileNotFoundException)
            {
                response.Headers["X-File-Revision"] = "0";
                return Results.Text("null", "application/json; charset=utf-8");
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPut("/file", async (string path, HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                var expected = request.Headers["X-Expected-Revision"].FirstOrDefault();
                var result = store.WriteTextWithRevision(path, body, expected);
                if (!result.Saved)
                {
                    return Results.Json(
                        new { message = "파일이 다른 기기에서 변경되었습니다.", revision = result.Revision, content = result.Content },
                        statusCode: StatusCodes.Status409Conflict);
                }
                return Results.Ok(new { saved = true, revision = result.Revision });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/schedule/files", (NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                return Results.Ok(store.ListScheduleJsonFiles());
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/schedule/file", (HttpResponse response, string name, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                var (json, revision) = store.ReadScheduleJson(name);
                response.Headers["X-File-Revision"] = revision;
                return Results.Text(json, "application/json; charset=utf-8");
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/audit/append", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                var month = request.Query["month"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(month))
                    return Results.BadRequest(new { message = "month=YYYY-MM 쿼리가 필요합니다." });
                store.AppendAudit(month, EnrichAuditWithClientIp(body, request));
                return Results.Ok(new { appended = true });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/audit", (string month, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                return Results.Text(store.ReadAudit(month), "application/x-ndjson; charset=utf-8");
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        g.MapGet("/edit-lock", (string date, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                return Results.Ok(new { @lock = store.GetEditLock(date) });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/edit-lock/acquire", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                var body = await JsonSerializer.DeserializeAsync<EditLockRequest>(request.Body, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                }) ?? throw new ArgumentException("본문이 필요합니다.");
                var (ok, lockState, conflict) = store.AcquireEditLock(body);
                if (!ok)
                    return Results.Json(new { message = "다른 사용자가 작성 중입니다.", @lock = conflict }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { @lock = lockState });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/edit-lock/heartbeat", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                var body = await JsonSerializer.DeserializeAsync<EditLockRequest>(request.Body, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                }) ?? throw new ArgumentException("본문이 필요합니다.");
                var (ok, lockState, conflict) = store.HeartbeatEditLock(body);
                if (!ok)
                    return Results.Json(new { message = "작성 잠금이 없습니다.", @lock = conflict }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { @lock = lockState });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/edit-lock/release", async (HttpRequest request, NasService nas) =>
        {
            try
            {
                nas.EnsureConnected();
                BindWorkLog(store, nas);
                var body = await JsonSerializer.DeserializeAsync<EditLockRequest>(request.Body, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                }) ?? throw new ArgumentException("본문이 필요합니다.");
                store.ReleaseEditLock(body);
                return Results.Ok(new { released = true });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
    }

    public static void MapFilesApp(WebApplication app)
    {
        var g = app.MapGroup("/files/api");

        g.MapPost("/schedules", async (FilesScheduleInput input, FilesAppStore store, CancellationToken ct) =>
        {
            try { return Results.Ok(await store.AddScheduleAsync(input, ct)); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapDelete("/schedules/{id:guid}", async (Guid id, FilesAppStore store, CancellationToken ct) =>
        {
            try
            {
                return await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/schedules/export", async (FilesAppStore store, CancellationToken ct) =>
            Results.File(await store.ExportAsync(ct), "application/json", "schedules.json"));
        g.MapPost("/schedules/import", async (IFormFile file, FilesAppStore store, CancellationToken ct) =>
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var count = await store.ImportAsync(stream, ct);
                return Results.Ok(new { count });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/schedules", async (bool scan, FilesAppStore store, CancellationToken ct) =>
        {
            try { return Results.Ok(await store.GetSchedulesAsync(scan, ct)); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapGet("/settings", async (FilesAppStore store, CancellationToken ct) =>
            Results.Ok(await store.GetSettingsAsync(ct)));
        g.MapPut("/settings", async (FilesSettings settings, FilesAppStore store, CancellationToken ct) =>
            Results.Ok(await store.SaveSettingsAsync(settings, ct)));
        g.MapPost("/settings/nas-test", async (FilesAppStore store, CancellationToken ct) =>
            Results.Ok(await store.TestNasAsync(ct)));
        g.MapPost("/schedules/sync-from-schedule-data", async (FilesAppStore store, CancellationToken ct) =>
        {
            try { return Results.Ok(await store.SyncFromScheduleAsync(ct)); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        g.MapPost("/settings/select-local", () => Results.Ok(new { path = "" }));
        g.MapPost("/files/open-location", (OpenLoc body) =>
        {
            try
            {
                FilesAppStore.OpenLocation(body.Path);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
    }

    private static void BindWorkLog(WorkLogFileStore store, NasService nas)
    {
        if (!nas.IsConnected) return;
        var wl = nas.WorkLogRoot ?? throw new InvalidOperationException("작업일지 루트 없음");
        if (!Directory.Exists(wl))
            Directory.CreateDirectory(wl);
        store.Bind(wl, nas.ScheduleRoot);
    }

    private static void MountAppFiles(WebApplication app, string urlPrefix, string folder)
    {
        var root = Path.Combine(app.Environment.ContentRootPath, "wwwroot", folder);
        if (!Directory.Exists(root))
            Directory.CreateDirectory(root);

        var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(root);
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = provider,
            RequestPath = urlPrefix,
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            RequestPath = urlPrefix,
        });
    }

    private static string EnrichAuditWithClientIp(string body, HttpRequest request)
    {
        var trimmed = string.IsNullOrWhiteSpace(body) ? "{}" : body.Trim();
        var node = JsonNode.Parse(trimmed)?.AsObject()
            ?? throw new ArgumentException("감사 로그 JSON이 올바르지 않습니다.");
        node["ip"] = ResolveClientIp(request);
        return node.ToJsonString();
    }

    private static string ResolveClientIp(HttpRequest request)
    {
        var remote = request.HttpContext.Connection.RemoteIpAddress;
        if (remote is not null)
        {
            if (remote.IsIPv4MappedToIPv6)
                remote = remote.MapToIPv4();
            if (!IPAddress.IsLoopback(remote))
                return remote.ToString();
        }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                    return addr.Address.ToString();
            }
        }

        return remote?.ToString() ?? "";
    }

    private sealed record ConnectBody(string? Host, string? Share, string? Username, string? Password);
    private sealed record DocumentLinkBody(string FileName, string ScheduleId, string ScheduleDate, string ScheduleTitle);
    private sealed record OpenLoc(string Path);
}

/// <summary>원본 UI 파일을 브리지 wwwroot로 복사·패치.</summary>
public static class UiAssetSync
{
    public static void EnsureCopied(string contentRoot)
    {
        var suiteRoot = FindSuiteRoot(contentRoot);
        CopySchedule(suiteRoot, Path.Combine(contentRoot, "wwwroot", "schedule"));
        CopyWorkLog(suiteRoot, Path.Combine(contentRoot, "wwwroot", "worklog"));
        CopyFiles(suiteRoot, Path.Combine(contentRoot, "wwwroot", "files"));
    }

    private static string FindSuiteRoot(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "ScheduleDataManager")) &&
                Directory.Exists(Path.Combine(dir.FullName, "WorkLog")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return contentRoot;
    }

    private static void CopySchedule(string suite, string dest)
    {
        Directory.CreateDirectory(dest);
        Directory.CreateDirectory(Path.Combine(dest, "assets", "icons"));
        var src = Path.Combine(suite, "ScheduleDataManager");
        if (!Directory.Exists(src)) return;
        CopyFile(Path.Combine(src, "index.html"), Path.Combine(dest, "index.html"));
        CopyFile(Path.Combine(src, "styles.css"), Path.Combine(dest, "styles.css"));
        CopyFile(Path.Combine(src, "sw.js"), Path.Combine(dest, "sw.js"));
        var appJs = Path.Combine(src, "app.js");
        if (File.Exists(appJs))
        {
            var text = File.ReadAllText(appJs, Encoding.UTF8);
            text = text.Replace("const API_BASE = \"\";", "const API_BASE = \"/schedule\";", StringComparison.Ordinal);
            text = text.Replace("const API_BASE = '';", "const API_BASE = '/schedule';", StringComparison.Ordinal);
            // 브리지 이미 연결 시 로그인 화면 건너뛰기
            if (!text.Contains("tryEnterFromBridgeOrLogin", StringComparison.Ordinal))
            {
                const string helper = """

async function tryEnterFromBridgeOrLogin() {
  try {
    const r = await fetch(`${API_BASE}/api/status`, { cache: "no-store" });
    if (!r.ok) throw new Error("status");
    const s = await r.json();
    if (!s.connected) throw new Error("not connected");
    bridgeAlive = true;
    bridgeConnected = true;
    connectionRoot = s.root || "";
    const filesRes = await fetch(`${API_BASE}/api/files`, { cache: "no-store" });
    const files = filesRes.ok ? await filesRes.json() : [];
    const list = Array.isArray(files) ? files : [];
    const preferred = typeof applyJsonFileOptions === "function" ? applyJsonFileOptions(list) : "";
    if (preferred) {
      try { await loadSelectedBridgeFile(preferred); } catch (_) { /* 로컬로 진입 */ }
    }
    if (typeof updateConnectionUi === "function") {
      updateConnectionUi({
        state: "linked",
        label: "공유 연결됨",
        detail: connectionRoot || "브리지 NAS 연결됨",
        pathText: preferred || "",
        syncText: preferred ? `NAS 연결됨 · ${preferred}` : "NAS 연결됨",
        syncState: "linked",
      });
    }
    enterAppShell();
    return;
  } catch (_) {
    showLoginScreen();
  }
}

""";
                text += helper;
            }
            text = text.Replace("\nshowLoginScreen();\n", "\ntryEnterFromBridgeOrLogin();\n", StringComparison.Ordinal);
            if (text.EndsWith("showLoginScreen();", StringComparison.Ordinal))
                text = text[..^"showLoginScreen();".Length] + "tryEnterFromBridgeOrLogin();";
            File.WriteAllText(Path.Combine(dest, "app.js"), text, Encoding.UTF8);
        }
        var icons = Path.Combine(src, "assets", "icons");
        if (Directory.Exists(icons))
        {
            foreach (var file in Directory.EnumerateFiles(icons))
                CopyFile(file, Path.Combine(dest, "assets", "icons", Path.GetFileName(file)));
        }
    }

    private static void CopyWorkLog(string suite, string dest)
    {
        Directory.CreateDirectory(dest);
        var src = Path.Combine(suite, "WorkLog", "www");
        if (!Directory.Exists(src)) return;
        foreach (var name in new[] { "index.html", "styles.css", "templates.js" })
            CopyFile(Path.Combine(src, name), Path.Combine(dest, name));
        var indexPath = Path.Combine(dest, "index.html");
        if (File.Exists(indexPath))
        {
            var html = File.ReadAllText(indexPath, Encoding.UTF8);
            // 브리지에서는 절대 경로 — /worklog(슬래시 없음)에서도 CSS·JS가 로드되도록
            html = html.Replace("href=\"/styles.css\"", "href=\"/worklog/styles.css\"");
            html = html.Replace("href=\"styles.css\"", "href=\"/worklog/styles.css\"");
            html = html.Replace("src=\"/app.js\"", "src=\"/worklog/app.js\"");
            html = html.Replace("src=\"app.js\"", "src=\"/worklog/app.js\"");
            File.WriteAllText(indexPath, html, Encoding.UTF8);
        }
        var appJs = Path.Combine(src, "app.js");
        if (File.Exists(appJs))
        {
            var text = File.ReadAllText(appJs, Encoding.UTF8);
            text = text.Replace(
                "let apiBase = \"http://127.0.0.1:17822\";",
                "let apiBase = \"/worklog\";",
                StringComparison.Ordinal);
            text = text.Replace(
                "let apiBase = 'http://127.0.0.1:17822';",
                "let apiBase = '/worklog';",
                StringComparison.Ordinal);
            // defaultApiBase() 가 pathname으로 판별해도, 복사본은 /worklog 고정이 안전
            text = text.Replace(
                "return isBridgeHosted() ? \"/worklog\" : \"http://127.0.0.1:17822\";",
                "return \"/worklog\";",
                StringComparison.Ordinal);
            text = text.Replace(
                "return isBridgeHosted() ? '/worklog' : 'http://127.0.0.1:17822';",
                "return '/worklog';",
                StringComparison.Ordinal);
            text = text.Replace(
                "return \"http://127.0.0.1:17822\";",
                "return \"/worklog\";",
                StringComparison.Ordinal);
            text = text.Replace(
                "return 'http://127.0.0.1:17822';",
                "return '/worklog';",
                StringComparison.Ordinal);
            text = text.Replace("from \"/templates.js\"", "from \"/worklog/templates.js\"");
            text = text.Replace("from '/templates.js'", "from '/worklog/templates.js'");
            text = text.Replace("from \"./templates.js\"", "from \"/worklog/templates.js\"");
            text = text.Replace("from './templates.js'", "from '/worklog/templates.js'");
            File.WriteAllText(Path.Combine(dest, "app.js"), text, Encoding.UTF8);
        }
    }

    private static void CopyFiles(string suite, string dest)
    {
        Directory.CreateDirectory(dest);
        var src = Path.Combine(suite, "FileChecker", "wwwroot");
        if (!Directory.Exists(src)) return;
        foreach (var file in Directory.EnumerateFiles(src))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("app.js", StringComparison.OrdinalIgnoreCase))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                text = text.Replace("fetch(url, options)", "fetch(url.startsWith('/api/') ? '/files' + url : url, options)");
                text = text.Replace("window.location.href = '/api/", "window.location.href = '/files/api/");
                // 브리지: 자체 스케줄 등록/가져오기 대신 SDM Recording 목록 사용 (서버가 GET 시 자동 동기화)
                text = text.Replace(
                    "if(action === 'schedule') openModal('schedule');",
                    "if(action === 'schedule') { toast('작업 목록은 ScheduleDataManager의 Recording 일정에서 자동으로 가져옵니다.'); return; }");
                text = text.Replace(
                    "if(action === 'import-schedules') $('#schedule-import').click();",
                    "if(action === 'import-schedules') { toast('브리지에서는 SDM 공유 스케줄만 사용합니다.'); return; }");
                text = text.Replace(
                    "등록된 스케줄이 없습니다",
                    "Recording 일정이 없습니다");
                File.WriteAllText(Path.Combine(dest, name), text, Encoding.UTF8);
            }
            else if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            {
                var html = File.ReadAllText(file, Encoding.UTF8);
                html = System.Text.RegularExpressions.Regex.Replace(
                    html,
                    """<button class="nav-button active" data-action="schedule">[^<]*</button>""",
                    """<button class="nav-button" data-action="refresh" title="SDM Recording sync">↻ SDM 동기화</button>""");
                html = System.Text.RegularExpressions.Regex.Replace(
                    html,
                    """<h3>[^<]*</h3><p>-</p><button class="primary" data-action="schedule">[^<]*</button>""",
                    """<h3>Recording 일정이 없습니다</h3><p>SDM 스케줄의 preparation에 Recording이 있는 항목만 표시됩니다</p><button class="primary" data-action="refresh">목록 새로고침</button>""");
                html = html.Replace("href=\"styles.css\"", "href=\"/files/styles.css\"");
                html = html.Replace("src=\"app.js\"", "src=\"/files/app.js\"");
                File.WriteAllText(Path.Combine(dest, name), html, Encoding.UTF8);
            }
            else
            {
                CopyFile(file, Path.Combine(dest, name));
            }
        }
    }

    private static void CopyFile(string from, string to)
    {
        if (!File.Exists(from)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
    }
}

file sealed record ActiveScheduleFileRequest(string? Name);
