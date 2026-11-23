using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var dataRootEnv = Environment.GetEnvironmentVariable("DATA_ROOT");
var scheduleRootEnv = Environment.GetEnvironmentVariable("SCHEDULE_ROOT");
var port = Environment.GetEnvironmentVariable("PORT") ?? "17822";

var store = new FileStore(
    string.IsNullOrWhiteSpace(dataRootEnv) ? null : Path.GetFullPath(dataRootEnv),
    string.IsNullOrWhiteSpace(scheduleRootEnv) ? null : Path.GetFullPath(scheduleRootEnv));

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
        .WithExposedHeaders("X-File-Revision")));

var app = builder.Build();
app.UseCors();

var www = Path.Combine(AppContext.BaseDirectory, "www");
if (Directory.Exists(www))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(www) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(www) });
}

app.MapGet("/api/status", () => Results.Ok(new
{
    connected = store.IsConnected,
    root = store.Root,
    scheduleRoot = store.ScheduleRoot,
}));

app.MapPost("/api/connect", (ConnectBody body) =>
{
    try
    {
        if (!string.IsNullOrWhiteSpace(body.DataRoot))
            store.SetRoots(body.DataRoot, body.ScheduleRoot);
        store.EnsureConnectedOrThrow();
        store.EnsureLayout();
        return Results.Ok(new { connected = true, root = store.Root, scheduleRoot = store.ScheduleRoot });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/disconnect", () => Results.Ok(new { connected = store.IsConnected }));

app.MapPost("/api/ensure-layout", () =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        store.EnsureLayout();
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/file", (HttpResponse response, string path) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        var (content, revision) = store.ReadText(path);
        response.Headers["X-File-Revision"] = revision;
        var type = path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            ? "application/x-ndjson; charset=utf-8"
            : "application/json; charset=utf-8";
        return Results.Text(content, type);
    }
    catch (FileNotFoundException)
    {
        response.Headers["X-File-Revision"] = "0";
        return Results.Text("null", "application/json; charset=utf-8");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPut("/api/file", async (string path, HttpRequest request) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var expected = request.Headers["X-Expected-Revision"].FirstOrDefault();
        var result = store.WriteText(path, body, expected);
        if (!result.Saved)
        {
            return Results.Json(
                new { message = "파일이 다른 기기에서 변경되었습니다.", revision = result.Revision, content = result.Content },
                statusCode: StatusCodes.Status409Conflict);
        }
        return Results.Ok(new { saved = true, revision = result.Revision });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/schedule/files", () =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        return Results.Ok(store.ListScheduleFiles());
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/schedule/file", (HttpResponse response, string name) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        var (json, revision) = store.ReadSchedule(name);
        response.Headers["X-File-Revision"] = revision;
        return Results.Text(json, "application/json; charset=utf-8");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/audit/append", async (HttpRequest request) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        var month = request.Query["month"].FirstOrDefault();
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        store.AppendAudit(month ?? "", AuditClientIp.Enrich(body, request));
        return Results.Ok(new { appended = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/audit", (string month) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        return Results.Text(store.ReadAudit(month), "application/x-ndjson; charset=utf-8");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/edit-lock", (string date) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
        return Results.Ok(new { @lock = store.GetEditLock(date) });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});
app.MapPost("/api/edit-lock/acquire", async (HttpRequest request) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
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
app.MapPost("/api/edit-lock/heartbeat", async (HttpRequest request) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
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
app.MapPost("/api/edit-lock/release", async (HttpRequest request) =>
{
    try
    {
        store.EnsureConnectedOrThrow();
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

if (store.IsConnected)
{
    try { store.EnsureLayout(); }
    catch (Exception ex) { Console.WriteLine($"ensureLayout: {ex.Message}"); }
}

Console.WriteLine($"WorkLog NAS API on http://0.0.0.0:{port}");
Console.WriteLine($"DATA_ROOT={store.Root ?? "(unset)"}");
app.Run();

record ConnectBody(string? Host, string? Share, string? Username, string? Password, string? DataRoot, string? ScheduleRoot);
record EditLockRequest(string? Date, string? SessionId, string? ActorId, string? ActorName);
record EditLockState(
    string Date,
    string SessionId,
    string ActorId,
    string ActorName,
    string AcquiredAt,
    string HeartbeatAt);

sealed class FileStore
{
    private string? root;
    private string? scheduleRoot;
    private readonly object gate = new();

    public FileStore(string? dataRoot, string? scheduleRoot)
    {
        if (!string.IsNullOrWhiteSpace(dataRoot))
            SetRoots(dataRoot, scheduleRoot);
    }

    public bool IsConnected => root is not null;
    public string? Root => root;
    public string? ScheduleRoot => scheduleRoot;

    public void SetRoots(string dataRoot, string? scheduleRootPath)
    {
        root = Path.GetFullPath(dataRoot);
        scheduleRoot = string.IsNullOrWhiteSpace(scheduleRootPath)
            ? Directory.GetParent(root)?.FullName
            : Path.GetFullPath(scheduleRootPath);
        Directory.CreateDirectory(root);
    }

    public void EnsureConnectedOrThrow()
    {
        if (root is null)
            throw new InvalidOperationException("DATA_ROOT가 설정되지 않았습니다. 환경변수 또는 connect.dataRoot를 사용하세요.");
    }

    public void EnsureLayout()
    {
        EnsureConnectedOrThrow();
        Directory.CreateDirectory(Path.Combine(root!, "work-logs"));
        Directory.CreateDirectory(Path.Combine(root!, "audit"));
        Directory.CreateDirectory(Path.Combine(root!, "locks"));
        Seed("profiles.json", """
            {"version":1,"profiles":[{"id":"ko_yunseok","displayName":"고윤석"},{"id":"kim_jonghee","displayName":"김종희"},{"id":"lim_wonbin","displayName":"임원빈"}]}
            """);
        Seed("defaults.json", """
            {"version":2,"customTemplates":[],"titleRules":[{"id":"rule-daejundo","conditionType":"schedule_title","value":"대전도","record":"{schedule_title} {place_label} 방송 및 유튜브 송출 준비.","templates":["BibleSeminar_Main"]}],"templateButtons":{"Sermon_Sunday_Primary":{"appendLog":"주일 말씀 방송 준비"},"Sermon_Wednesday":{"appendLog":"수요 말씀 방송 준비"},"Sermon_Thursday":{"appendLog":"오전 수요말씀 방송 준비"}}}
            """);
    }

    private void Seed(string relative, string json)
    {
        var path = Resolve(relative);
        if (File.Exists(path)) return;
        using var doc = JsonDocument.Parse(json);
        File.WriteAllText(path, JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public (string Content, string Revision) ReadText(string relative)
    {
        var path = Resolve(relative);
        if (!File.Exists(path)) throw new FileNotFoundException(relative);
        lock (gate)
            return (File.ReadAllText(path, Encoding.UTF8), Rev(path));
    }

    public WriteResult WriteText(string relative, string contents, string? expected)
    {
        var path = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (gate)
        {
            if (!string.IsNullOrEmpty(expected) && File.Exists(path))
            {
                var current = Rev(path);
                if (!string.Equals(current, expected, StringComparison.Ordinal))
                    return new WriteResult(false, current, File.ReadAllText(path, Encoding.UTF8));
            }

            if (relative.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, contents, Encoding.UTF8);
            else
            {
                using var doc = JsonDocument.Parse(contents);
                File.WriteAllText(path, JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            return new WriteResult(true, Rev(path), null);
        }
    }

    public void AppendAudit(string month, string body)
    {
        if (!Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
            throw new ArgumentException("month=YYYY-MM 필요");
        var trimmed = body.Trim();
        using (JsonDocument.Parse(trimmed)) { }
        var path = Resolve($"audit/{month}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (gate)
            File.AppendAllText(path, trimmed.Replace("\r", "").Replace("\n", " ") + Environment.NewLine, Encoding.UTF8);
    }

    public string ReadAudit(string month)
    {
        if (!Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
            throw new ArgumentException("month=YYYY-MM 필요");
        var path = Resolve($"audit/{month}.jsonl");
        if (!File.Exists(path)) return "";
        lock (gate) return File.ReadAllText(path, Encoding.UTF8);
    }

    private const int EditLockTtlSeconds = 45;

    public EditLockState? GetEditLock(string date)
    {
        ValidateDate(date);
        lock (gate)
        {
            var path = Resolve($"locks/{date}.json");
            var current = ReadEditLockFile(path);
            if (current is null) return null;
            if (!IsEditLockAlive(current))
            {
                TryDeleteEditLock(path);
                return null;
            }
            return current;
        }
    }

    public (bool Ok, EditLockState? Lock, EditLockState? Conflict) AcquireEditLock(EditLockRequest request)
    {
        var date = RequireDate(request.Date);
        var sessionId = RequireToken(request.SessionId, "sessionId");
        var actorId = RequireToken(request.ActorId, "actorId");
        var actorName = string.IsNullOrWhiteSpace(request.ActorName) ? actorId : request.ActorName.Trim();
        Directory.CreateDirectory(Path.Combine(root!, "locks"));
        lock (gate)
        {
            var path = Resolve($"locks/{date}.json");
            var existing = ReadEditLockFile(path);
            if (existing is not null && IsEditLockAlive(existing) &&
                !string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal))
                return (false, null, existing);
            var now = DateTimeOffset.UtcNow;
            var next = new EditLockState(
                date, sessionId, actorId, actorName,
                existing is not null && string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal)
                    ? existing.AcquiredAt : now.ToString("O"),
                now.ToString("O"));
            WriteEditLockFile(path, next);
            var confirm = ReadEditLockFile(path);
            if (confirm is null || !string.Equals(confirm.SessionId, sessionId, StringComparison.Ordinal))
                return (false, null, confirm);
            return (true, confirm, null);
        }
    }

    public (bool Ok, EditLockState? Lock, EditLockState? Conflict) HeartbeatEditLock(EditLockRequest request)
    {
        var date = RequireDate(request.Date);
        var sessionId = RequireToken(request.SessionId, "sessionId");
        lock (gate)
        {
            var path = Resolve($"locks/{date}.json");
            var existing = ReadEditLockFile(path);
            if (existing is null || !IsEditLockAlive(existing) ||
                !string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal))
                return (false, null, existing is not null && IsEditLockAlive(existing) ? existing : null);
            var next = existing with { HeartbeatAt = DateTimeOffset.UtcNow.ToString("O") };
            WriteEditLockFile(path, next);
            return (true, next, null);
        }
    }

    public void ReleaseEditLock(EditLockRequest request)
    {
        var date = RequireDate(request.Date);
        var sessionId = RequireToken(request.SessionId, "sessionId");
        lock (gate)
        {
            var path = Resolve($"locks/{date}.json");
            var existing = ReadEditLockFile(path);
            if (existing is null) return;
            if (!string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal)) return;
            TryDeleteEditLock(path);
        }
    }

    private static bool IsEditLockAlive(EditLockState state)
    {
        if (!DateTimeOffset.TryParse(state.HeartbeatAt, out var heartbeat)) return false;
        return (DateTimeOffset.UtcNow - heartbeat).TotalSeconds < EditLockTtlSeconds;
    }

    private EditLockState? ReadEditLockFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<EditLockState>(
                File.ReadAllText(path, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static void WriteEditLockFile(string path, EditLockState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryDeleteEditLock(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch { /* ignore */ }
    }

    private static string RequireDate(string? date)
    {
        ValidateDate(date ?? "");
        return date!.Trim();
    }

    private static string RequireToken(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name}이(가) 필요합니다.");
        return value.Trim();
    }

    private static void ValidateDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date) || !Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            throw new ArgumentException("date는 YYYY-MM-DD 형식이어야 합니다.");
    }

    public string[] ListScheduleFiles()
    {
        if (string.IsNullOrEmpty(scheduleRoot) || !Directory.Exists(scheduleRoot)) return [];
        return Directory.EnumerateFiles(scheduleRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n is not null && !n.StartsWith('_'))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public (string Json, string Revision) ReadSchedule(string name)
    {
        if (string.IsNullOrEmpty(scheduleRoot)) throw new InvalidOperationException("스케줄 폴더 없음");
        if (Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 파일명");
        var path = Path.Combine(scheduleRoot, name);
        if (!File.Exists(path)) throw new FileNotFoundException(name);
        lock (gate) return (File.ReadAllText(path, Encoding.UTF8), Rev(path));
    }

    private string Resolve(string relative)
    {
        EnsureConnectedOrThrow();
        var normalized = (relative ?? "").Replace('/', Path.DirectorySeparatorChar).Trim().TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("허용되지 않은 경로");
        var full = Path.GetFullPath(Path.Combine(root!, normalized));
        if (!full.StartsWith(Path.GetFullPath(root!), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("루트 밖 경로");
        return full;
    }

    private static string Rev(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "0";

    public readonly record struct WriteResult(bool Saved, string Revision, string? Content);
}

static class AuditClientIp
{
    public static string Enrich(string body, HttpRequest request)
    {
        var trimmed = string.IsNullOrWhiteSpace(body) ? "{}" : body.Trim();
        var node = JsonNode.Parse(trimmed)?.AsObject()
            ?? throw new ArgumentException("감사 로그 JSON이 올바르지 않습니다.");
        node["ip"] = Resolve(request);
        return node.ToJsonString();
    }

    static string Resolve(HttpRequest request)
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
}
