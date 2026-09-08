using System.Text;
using System.Text.Json;
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

        app.MapPost("/api/disconnect", (NasService nas) =>
        {
            nas.Disconnect();
            return Results.Ok(nas.Status());
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
        g.MapGet("/lan-devices", () => Results.Ok(Array.Empty<object>()));
    }

    public static void MapWorkLogApp(WebApplication app)
    {
        var store = new WorkLogFileStore();

        var g = app.MapGroup("/worklog/api");
        g.MapGet("/status", (NasService nas) =>
        {
            BindWorkLog(store, nas);
            return Results.Ok(new
            {
                app = "WorkLog",
                port = 17820,
                connected = nas.IsConnected && store.IsConnected,
                root = store.Root,
                scheduleRoot = store.ScheduleRoot,
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
                    if (!string.IsNullOrWhiteSpace(body.Username)) cfg.Username = body.Username;
                    if (!string.IsNullOrWhiteSpace(body.Password)) cfg.Password = body.Password;
                    nas.Connect(cfg);
                }
                BindWorkLog(store, nas);
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
                store.AppendAudit(month, body);
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
    }

    public static void MapFilesApp(WebApplication app)
    {
        var g = app.MapGroup("/files/api");

        g.MapGet("/schedules", async (bool scan, FilesAppStore store, CancellationToken ct) =>
            Results.Ok(await store.GetSchedulesAsync(scan, ct)));
        g.MapPost("/schedules", async (FilesScheduleInput input, FilesAppStore store, CancellationToken ct) =>
            Results.Ok(await store.AddScheduleAsync(input, ct)));
        g.MapDelete("/schedules/{id:guid}", async (Guid id, FilesAppStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        g.MapGet("/schedules/export", async (FilesAppStore store, CancellationToken ct) =>
            Results.File(await store.ExportAsync(ct), "application/json", "schedules.json"));
        g.MapPost("/schedules/import", async (IFormFile file, FilesAppStore store, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var count = await store.ImportAsync(stream, ct);
            return Results.Ok(new { count });
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
            // 세션 bye는 브리지 grace와 맞춤 — NotifyUiClosing
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
            text = text.Replace(
                "return \"http://127.0.0.1:17822\";",
                "return \"/worklog\";",
                StringComparison.Ordinal);
            text = text.Replace(
                "return 'http://127.0.0.1:17822';",
                "return '/worklog';",
                StringComparison.Ordinal);
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
                File.WriteAllText(Path.Combine(dest, name), text, Encoding.UTF8);
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
