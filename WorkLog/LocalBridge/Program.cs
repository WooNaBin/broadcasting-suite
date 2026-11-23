using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string AppUrl = "http://127.0.0.1:17822/";
const string BridgeWorkLogUrl = "http://127.0.0.1:17820/worklog/";
const string AppName = "WorkLog";

if (TryOpenViaBroadcastNasBridge())
    return;

using var mutex = new Mutex(true, @"Local\WorkLog.LocalBridge", out var createdNew);
if (!createdNew)
{
    if (TryOpenViaBroadcastNasBridge())
        return;
    if (IsWorkLogListening())
    {
        OpenAppPage();
        ShowInfo("WorkLog이 이미 실행 중입니다.\n기존 창을 사용해주세요.");
    }
    else
        ShowError("WorkLog 뮤텍스는 잡혔지만 17822에서 WorkLog API를 찾지 못했습니다.");
    return;
}

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls(AppUrl.TrimEnd('/'));
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.SerializerOptions.PropertyNameCaseInsensitive = true;
    });
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("X-File-Revision")));
    var app = builder.Build();
    app.UseCors();

    var connection = new WorkLogStore();
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    var browserSession = new BrowserSessionGuard(lifetime);

    app.MapGet("/", () => EmbeddedFile("WorkLog.index.html", "text/html; charset=utf-8"));
    app.MapGet("/index.html", () => EmbeddedFile("WorkLog.index.html", "text/html; charset=utf-8"));
    app.MapGet("/styles.css", () => EmbeddedFile("WorkLog.styles.css", "text/css; charset=utf-8"));
    app.MapGet("/app.js", () => EmbeddedFile("WorkLog.app.js", "text/javascript; charset=utf-8"));
    app.MapGet("/templates.js", () => EmbeddedFile("WorkLog.templates.js", "text/javascript; charset=utf-8"));

    // 브라우저가 살아 있을 때 ping, 창을 닫으면 bye → 프로세스 종료
    app.MapPost("/api/session/ping", () =>
    {
        browserSession.Ping();
        return Results.Ok(new { ok = true });
    });
    app.MapPost("/api/session/bye", () =>
    {
        browserSession.RequestExit();
        return Results.Ok(new { stopping = true });
    });

    app.MapGet("/api/status", () => Results.Ok(new
    {
        app = AppName,
        port = 17822,
        connected = connection.IsConnected,
        root = connection.Root,
        scheduleRoot = connection.ScheduleRoot,
    }));

    app.MapPost("/api/connect", (ConnectRequest request) =>
    {
        try
        {
            connection.Connect(request.Host, request.Share, request.Username, request.Password);
            return Results.Ok(new
            {
                connected = true,
                root = connection.Root,
                scheduleRoot = connection.ScheduleRoot,
            });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/disconnect", () =>
    {
        connection.Disconnect();
        return Results.Ok(new { connected = false });
    });

    app.MapGet("/api/file", (HttpResponse response, string path) =>
    {
        try
        {
            var (content, revision) = connection.ReadTextWithRevision(path);
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
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPut("/api/file", async (string path, HttpRequest request) =>
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var expected = request.Headers["X-Expected-Revision"].FirstOrDefault();
            var result = connection.WriteTextWithRevision(path, body, expected);
            if (!result.Saved)
            {
                return Results.Json(
                    new { message = "파일이 다른 기기에서 변경되었습니다.", revision = result.Revision, content = result.Content },
                    statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Ok(new { saved = true, revision = result.Revision });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/schedule/files", () =>
    {
        try
        {
            return Results.Ok(connection.ListScheduleJsonFiles());
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/schedule/file", (HttpResponse response, string name) =>
    {
        try
        {
            var (json, revision) = connection.ReadScheduleJson(name);
            response.Headers["X-File-Revision"] = revision;
            return Results.Text(json, "application/json; charset=utf-8");
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/audit/append", async (HttpRequest request) =>
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var month = request.Query["month"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(month))
                return Results.BadRequest(new { message = "month=YYYY-MM 쿼리가 필요합니다." });
            connection.AppendAudit(month, EnrichAuditWithClientIp(body, request));
            return Results.Ok(new { appended = true });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/audit", (string month) =>
    {
        try
        {
            return Results.Text(connection.ReadAudit(month), "application/x-ndjson; charset=utf-8");
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/edit-lock", (string date) =>
    {
        try
        {
            var lockState = connection.GetEditLock(date);
            return Results.Ok(new { @lock = lockState });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/edit-lock/acquire", async (HttpRequest request) =>
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<EditLockRequest>(request.Body, JsonOpts())
                ?? throw new ArgumentException("본문이 필요합니다.");
            var (ok, lockState, conflict) = connection.AcquireEditLock(body);
            if (!ok)
                return Results.Json(new { message = "다른 사용자가 작성 중입니다.", @lock = conflict }, statusCode: StatusCodes.Status409Conflict);
            return Results.Ok(new { @lock = lockState });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/edit-lock/heartbeat", async (HttpRequest request) =>
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<EditLockRequest>(request.Body, JsonOpts())
                ?? throw new ArgumentException("본문이 필요합니다.");
            var (ok, lockState, conflict) = connection.HeartbeatEditLock(body);
            if (!ok)
                return Results.Json(new { message = "작성 잠금이 없습니다.", @lock = conflict }, statusCode: StatusCodes.Status409Conflict);
            return Results.Ok(new { @lock = lockState });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/edit-lock/release", async (HttpRequest request) =>
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<EditLockRequest>(request.Body, JsonOpts())
                ?? throw new ArgumentException("본문이 필요합니다.");
            connection.ReleaseEditLock(body);
            return Results.Ok(new { released = true });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/ensure-layout", () =>
    {
        try
        {
            connection.EnsureLayout();
            return Results.Ok(new { ok = true });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.Lifetime.ApplicationStopping.Register(() =>
    {
        connection.Disconnect();
        browserSession.Dispose();
    });
    app.Lifetime.ApplicationStarted.Register(OpenAppPage);
    app.Run();
}
catch (IOException exception) when (IsAddressInUse(exception))
{
    if (TryOpenViaBroadcastNasBridge())
        return;
    if (IsWorkLogListening())
    {
        OpenAppPage();
        ShowInfo("포트 17822가 이미 사용 중입니다.\n기존 WorkLog 창을 사용해주세요.");
    }
    else
    {
        ShowError(
            "포트 17822가 다른 프로그램에 사용 중입니다.\n" +
            "ScheduleReader는 17823, 스케줄 관리자는 17821, CtrlOne은 5177입니다.\n" +
            "해당 프로그램을 종료한 뒤 WorkLog을 다시 실행하세요.");
    }
}
catch (Exception exception)
{
    ShowError($"WorkLog을 시작하지 못했습니다.\n\n{exception.Message}");
}

static string EnrichAuditWithClientIp(string body, HttpRequest request)
{
    var trimmed = string.IsNullOrWhiteSpace(body) ? "{}" : body.Trim();
    var node = JsonNode.Parse(trimmed)?.AsObject()
        ?? throw new ArgumentException("감사 로그 JSON이 올바르지 않습니다.");
    node["ip"] = ResolveClientIp(request);
    return node.ToJsonString();
}

static string ResolveClientIp(HttpRequest request)
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

static void OpenAppPage()
{
    try
    {
        Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true });
    }
    catch
    {
        // ignore
    }
}

/// <summary>브리지(17820)가 있으면 /worklog 로 열어 허브 NAS 세션을 공유한다.</summary>
static bool TryOpenViaBroadcastNasBridge()
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        using var res = client.GetAsync("http://127.0.0.1:17820/api/status").GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode) return false;
        var json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!json.Contains("BroadcastNasBridge", StringComparison.Ordinal) &&
            !json.Contains("\"port\":17820", StringComparison.Ordinal))
            return false;

        Process.Start(new ProcessStartInfo(BridgeWorkLogUrl) { UseShellExecute = true });
        return true;
    }
    catch
    {
        return false;
    }
}

/// <summary>17822가 WorkLog인지 확인 (다른 앱이 같은 포트를 쓰면 브라우저를 열지 않음).</summary>
static bool IsWorkLogListening()
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        var json = client.GetStringAsync("http://127.0.0.1:17822/api/status").GetAwaiter().GetResult();
        return json.Contains("\"app\":\"WorkLog\"", StringComparison.Ordinal)
            || json.Contains("\"app\": \"WorkLog\"", StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static bool IsAddressInUse(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
            return true;
        if (current.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}

static void ShowInfo(string message) =>
    NativeMethods.MessageBox(IntPtr.Zero, message, "WorkLog", 0x00000040);

static void ShowError(string message) =>
    NativeMethods.MessageBox(IntPtr.Zero, message, "WorkLog", 0x00000010);

static IResult EmbeddedFile(string resourceName, string contentType)
{
    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
    return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
}

static JsonSerializerOptions JsonOpts() => new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

record ConnectRequest(string Host, string Share, string Username, string Password);
record EditLockRequest(string? Date, string? SessionId, string? ActorId, string? ActorName);
record EditLockState(
    string Date,
    string SessionId,
    string ActorId,
    string ActorName,
    string AcquiredAt,
    string HeartbeatAt);

/// <summary>
/// 브라우저 탭이 닫히면 LocalBridge 프로세스를 종료한다.
/// ping이 오면 생존으로 보고, bye(창 닫기) 또는 ping 끊김 시 종료한다.
/// </summary>
sealed class BrowserSessionGuard : IDisposable
{
    private readonly IHostApplicationLifetime lifetime;
    private readonly object gate = new();
    private readonly Timer watchdog;
    private DateTime? lastPingUtc;
    private CancellationTokenSource? exitCts;
    private bool stopping;

    public BrowserSessionGuard(IHostApplicationLifetime lifetime)
    {
        this.lifetime = lifetime;
        watchdog = new Timer(_ => CheckWatchdog(), null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(4));
    }

    public void Ping()
    {
        lock (gate)
        {
            lastPingUtc = DateTime.UtcNow;
            CancelExitLocked();
        }
    }

    public void RequestExit()
    {
        lock (gate)
        {
            if (stopping) return;
            CancelExitLocked();
            exitCts = new CancellationTokenSource();
            var token = exitCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    // 새로고침 직후 ping이 오면 취소. 창 닫기는 빠르게 종료
                    await Task.Delay(900, token);
                    Stop();
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
        }
    }

    private void CheckWatchdog()
    {
        DateTime? last;
        lock (gate) last = lastPingUtc;
        if (last is null) return;
        // ping 주기가 5초이므로 25초 무응답이면 종료
        if (DateTime.UtcNow - last.Value > TimeSpan.FromSeconds(25))
            Stop();
    }

    private void Stop()
    {
        lock (gate)
        {
            if (stopping) return;
            stopping = true;
            CancelExitLocked();
        }
        try { lifetime.StopApplication(); }
        catch { /* ignore */ }
        // StopApplication이 지연되면 강제 종료
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                Environment.Exit(0);
            }
            catch { /* ignore */ }
        });
    }

    private void CancelExitLocked()
    {
        if (exitCts is null) return;
        try { exitCts.Cancel(); }
        catch { /* ignore */ }
        exitCts.Dispose();
        exitCts = null;
    }

    public void Dispose()
    {
        watchdog.Dispose();
        lock (gate) CancelExitLocked();
    }
}

sealed class WorkLogStore
{
    private string? root;
    private string? scheduleRoot;
    private readonly object fileGate = new();

    public bool IsConnected => root is not null;
    public string? Root => root;
    public string? ScheduleRoot => scheduleRoot;

    private static readonly string SeedProfiles = """
        {
          "version": 1,
          "profiles": [
            { "id": "ko_yunseok", "displayName": "고윤석" },
            { "id": "kim_jonghee", "displayName": "김종희" },
            { "id": "lim_wonbin", "displayName": "임원빈" }
          ]
        }
        """;

    private static readonly string SeedDefaults = """
        {
          "version": 2,
          "titleRules": [
            {
              "id": "rule-daejundo",
              "conditionType": "schedule_title",
              "value": "대전도",
              "record": "{schedule_title} {place_label} 방송 및 유튜브 송출 준비.",
              "templates": ["BibleSeminar_Main"]
            }
          ],
          "templateButtons": {
            "Sermon_Sunday_Primary": { "appendLog": "주일 말씀 방송 준비" },
            "Sermon_Wednesday": { "appendLog": "수요 말씀 방송 준비" },
            "Sermon_Thursday": { "appendLog": "오전 수요말씀 방송 준비" }
          },
          "customTemplates": []
        }
        """;

    public void Connect(string host, string share, string username, string password)
    {
        var (normalizedHost, normalizedShare) = NormalizeHostAndShare(host, share);
        if (string.IsNullOrWhiteSpace(normalizedHost))
            throw new ArgumentException("NAS IP 또는 호스트 이름을 확인하세요.");
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("사용자 계정을 입력하세요.");
        if (string.IsNullOrWhiteSpace(normalizedShare))
            throw new ArgumentException("공유 폴더명을 입력하세요. 예: Temp DATA\\_data\\_work_log");

        var parts = normalizedShare.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var rootShareName = parts[0];
        var rootNetworkPath = $@"\\{normalizedHost}\{rootShareName}";
        var fullNetworkPath = parts.Length > 1
            ? $@"{rootNetworkPath}\{string.Join('\\', parts.Skip(1))}"
            : rootNetworkPath;

        // 같은 NAS를 쓰는 다른 앱(스케줄 관리자 등) 세션을 끊지 않도록
        // 먼저 기존 연결 재사용 → 추가 연결 → 최후에만 강제 정리
        DisconnectLocalOnly();

        if (CanUseUncPath(fullNetworkPath))
        {
            root = fullNetworkPath;
            scheduleRoot = Directory.GetParent(fullNetworkPath)?.FullName;
            EnsureLayout();
            return;
        }

        var candidates = BuildUsernameCandidates(username, normalizedHost);
        var lastError = 0;
        string? usedUser = null;
        foreach (var candidate in candidates)
        {
            var result = TryAddConnection(rootNetworkPath, password, candidate);
            if (result == 0)
            {
                usedUser = candidate;
                lastError = 0;
                break;
            }

            // 이미 다른 자격 증명/세션이 있으면 경로만 쓸 수 있는지 확인 (강제 끊기 금지)
            if (result is 1219 or 85 or 2404)
            {
                if (CanUseUncPath(fullNetworkPath))
                {
                    usedUser = candidate;
                    lastError = 0;
                    break;
                }
            }

            lastError = result;
        }

        if (usedUser is null && CanUseUncPath(fullNetworkPath))
        {
            usedUser = username;
            lastError = 0;
        }

        // 다른 앱(스케줄 관리자·FileChecker·탐색기)이 쓰는 SMB 세션을 끊지 않음.
        // 자격 증명 충돌(1219 등)이면 강제 해제 대신 안내만 한다.
        if (lastError != 0 || usedUser is null)
            throw new InvalidOperationException(DescribeSmbError(lastError, rootNetworkPath, fullNetworkPath));

        if (!Directory.Exists(fullNetworkPath))
        {
            try
            {
                Directory.CreateDirectory(fullNetworkPath);
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new InvalidOperationException(
                    $"작업일지 폴더를 만들 수 없습니다.\n경로: {fullNetworkPath}\n{ex.Message}", ex);
            }
        }

        root = fullNetworkPath;
        scheduleRoot = Directory.GetParent(fullNetworkPath)?.FullName;
        EnsureLayout();
    }

    public void EnsureLayout()
    {
        EnsureConnected();
        Directory.CreateDirectory(Path.Combine(root!, "work-logs"));
        Directory.CreateDirectory(Path.Combine(root!, "audit"));
        Directory.CreateDirectory(Path.Combine(root!, "locks"));
        SeedIfMissing("profiles.json", SeedProfiles);
        SeedIfMissing("defaults.json", SeedDefaults);
    }

    private void SeedIfMissing(string relativePath, string contents)
    {
        var path = ResolvePath(relativePath);
        if (File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var document = JsonDocument.Parse(contents);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    public void Disconnect() => DisconnectLocalOnly();

    /// <summary>로컬 루트만 해제. SMB 세션은 유지해 동시 실행 앱이 같은 NAS를 쓰게 함.</summary>
    private void DisconnectLocalOnly()
    {
        root = null;
        scheduleRoot = null;
    }

    private static bool CanUseUncPath(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            _ = Directory.GetFileSystemEntries(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public (string Content, string Revision) ReadTextWithRevision(string relativePath)
    {
        var path = ResolvePath(relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException(relativePath);
        lock (fileGate)
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            return (content, GetFileRevision(path));
        }
    }

    public WriteResult WriteTextWithRevision(string relativePath, string contents, string? expectedRevision)
    {
        var path = ResolvePath(relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        lock (fileGate)
        {
            if (!string.IsNullOrEmpty(expectedRevision) && File.Exists(path))
            {
                var current = GetFileRevision(path);
                if (!string.Equals(current, expectedRevision, StringComparison.Ordinal))
                {
                    return new WriteResult
                    {
                        Saved = false,
                        Revision = current,
                        Content = File.ReadAllText(path, Encoding.UTF8),
                    };
                }
            }

            if (relativePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, contents, Encoding.UTF8);
            }
            else
            {
                using var document = JsonDocument.Parse(contents);
                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }

            return new WriteResult { Saved = true, Revision = GetFileRevision(path) };
        }
    }

    public void AppendAudit(string month, string lineOrJson)
    {
        EnsureConnected();
        ValidateMonth(month);
        var relative = $"audit/{month}.jsonl";
        var path = ResolvePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var trimmed = lineOrJson.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("감사 로그 내용이 비어 있습니다.");
        // Validate JSON object
        using (JsonDocument.Parse(trimmed)) { }

        lock (fileGate)
        {
            var line = trimmed.Replace("\r", "").Replace("\n", " ") + Environment.NewLine;
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    public string ReadAudit(string month)
    {
        EnsureConnected();
        ValidateMonth(month);
        var path = ResolvePath($"audit/{month}.jsonl");
        if (!File.Exists(path)) return "";
        lock (fileGate)
            return File.ReadAllText(path, Encoding.UTF8);
    }

    private const int EditLockTtlSeconds = 45;

    public EditLockState? GetEditLock(string date)
    {
        EnsureConnected();
        ValidateDate(date);
        lock (fileGate)
        {
            var path = EditLockPath(date);
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
        EnsureConnected();
        var date = RequireDate(request.Date);
        var sessionId = RequireToken(request.SessionId, "sessionId");
        var actorId = RequireToken(request.ActorId, "actorId");
        var actorName = string.IsNullOrWhiteSpace(request.ActorName) ? actorId : request.ActorName.Trim();
        Directory.CreateDirectory(Path.Combine(root!, "locks"));

        lock (fileGate)
        {
            var path = EditLockPath(date);
            var existing = ReadEditLockFile(path);
            if (existing is not null && IsEditLockAlive(existing) &&
                !string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal))
                return (false, null, existing);

            var now = DateTimeOffset.UtcNow;
            var next = new EditLockState(
                date,
                sessionId,
                actorId,
                actorName,
                existing is not null && string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal)
                    ? existing.AcquiredAt
                    : now.ToString("O"),
                now.ToString("O"));
            WriteEditLockFile(path, next);

            // SMB 경쟁 시 마지막 기록 확인
            var confirm = ReadEditLockFile(path);
            if (confirm is null || !string.Equals(confirm.SessionId, sessionId, StringComparison.Ordinal))
                return (false, null, confirm);
            return (true, confirm, null);
        }
    }

    public (bool Ok, EditLockState? Lock, EditLockState? Conflict) HeartbeatEditLock(EditLockRequest request)
    {
        EnsureConnected();
        var date = RequireDate(request.Date);
        var sessionId = RequireToken(request.SessionId, "sessionId");
        lock (fileGate)
        {
            var path = EditLockPath(date);
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
        EnsureConnected();
        var date = RequireDate(request.Date);
        var sessionId = RequireToken(request.SessionId, "sessionId");
        lock (fileGate)
        {
            var path = EditLockPath(date);
            var existing = ReadEditLockFile(path);
            if (existing is null) return;
            if (!string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal)) return;
            TryDeleteEditLock(path);
        }
    }

    private string EditLockPath(string date) => ResolvePath($"locks/{date}.json");

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
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<EditLockState>(json, EditLockJsonOptions());
        }
        catch
        {
            return null;
        }
    }

    private static void WriteEditLockFile(string path, EditLockState state)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(state, EditLockJsonOptions());
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    private static JsonSerializerOptions EditLockJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static void TryDeleteEditLock(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch
        {
            // ignore
        }
    }

    private static string RequireDate(string? date)
    {
        ValidateDate(date ?? "");
        return date!.Trim();
    }

    private static string RequireToken(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name}이(가) 필요합니다.");
        return value.Trim();
    }

    private static void ValidateDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date) ||
            !System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            throw new ArgumentException("date는 YYYY-MM-DD 형식이어야 합니다.");
    }

    public string[] ListScheduleJsonFiles()
    {
        EnsureConnected();
        if (string.IsNullOrEmpty(scheduleRoot) || !Directory.Exists(scheduleRoot))
            return [];

        return Directory.EnumerateFiles(scheduleRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => !name.StartsWith("_", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public (string Json, string Revision) ReadScheduleJson(string name)
    {
        EnsureConnected();
        if (string.IsNullOrEmpty(scheduleRoot))
            throw new InvalidOperationException("스케줄 폴더를 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name ||
            !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 스케줄 파일명입니다.");

        var path = Path.Combine(scheduleRoot, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"스케줄 파일이 없습니다: {name}");
        lock (fileGate)
            return (File.ReadAllText(path, Encoding.UTF8), GetFileRevision(path));
    }

    private string ResolvePath(string relativePath)
    {
        EnsureConnected();
        var normalized = (relativePath ?? "").Replace('/', '\\').Trim().TrimStart('\\');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
            throw new ArgumentException("허용되지 않은 경로입니다.");

        var full = Path.GetFullPath(Path.Combine(root!, normalized));
        var rootFull = Path.GetFullPath(root!);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("루트 밖 경로는 허용되지 않습니다.");
        return full;
    }

    private static void ValidateMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month) ||
            !System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
            throw new ArgumentException("month는 YYYY-MM 형식이어야 합니다.");
    }

    private static string GetFileRevision(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "0";

    private void EnsureConnected()
    {
        if (root is null) throw new InvalidOperationException("먼저 SMB 연결을 확인하세요.");
    }

    private static int TryAddConnection(string remoteName, string password, string username)
    {
        var resource = new NetResource
        {
            Scope = 0,
            Type = 1,
            DisplayType = 0,
            Usage = 0,
            LocalName = null,
            RemoteName = remoteName,
            Comment = null,
            Provider = null,
        };
        return NativeMethods.WNetAddConnection2(ref resource, password, username, 0);
    }


    private static (string host, string share) NormalizeHostAndShare(string host, string share)
    {
        var rawHost = (host ?? string.Empty).Trim().Trim('"');
        var rawShare = (share ?? string.Empty).Trim().Trim('"');

        var fromHost = TryParseUnc(rawHost);
        if (fromHost is not null)
        {
            rawHost = fromHost.Value.host;
            if (string.IsNullOrWhiteSpace(rawShare))
                rawShare = fromHost.Value.shareAndPath;
            else if (!rawShare.Contains('\\') && !rawShare.Contains('/'))
                rawShare = fromHost.Value.shareAndPath;
            else
                rawShare = $"{fromHost.Value.shareAndPath}\\{rawShare.Trim('\\', '/')}";
        }

        var fromShare = TryParseUnc(rawShare);
        if (fromShare is not null)
        {
            if (string.IsNullOrWhiteSpace(rawHost))
                rawHost = fromShare.Value.host;
            rawShare = fromShare.Value.shareAndPath;
        }

        rawHost = rawHost
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Trim('\\', '/');

        var colon = rawHost.IndexOf(':');
        if (colon > 0 && rawHost.IndexOf(':') == rawHost.LastIndexOf(':') && !rawHost.Contains(']'))
        {
            var maybePort = rawHost[(colon + 1)..];
            if (int.TryParse(maybePort, out _))
                rawHost = rawHost[..colon];
        }

        rawShare = rawShare.Replace('/', '\\').Trim().Trim('\\');
        return (rawHost, rawShare);
    }

    private static (string host, string shareAndPath)? TryParseUnc(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim().Replace('/', '\\');
        var hadUncPrefix = text.StartsWith(@"\\", StringComparison.Ordinal);
        while (text.StartsWith(@"\\", StringComparison.Ordinal))
            text = text[2..];

        if (!text.Contains('\\')) return null;
        var parts = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var maybeHost = parts[0];
        if (!hadUncPrefix && !LooksLikeHost(maybeHost))
            return null;

        return (maybeHost, string.Join('\\', parts.Skip(1)));
    }

    private static bool LooksLikeHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains(' ')) return false;
        if (System.Net.IPAddress.TryParse(value, out _)) return true;
        if (value.Contains('.')) return true;
        return value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static IEnumerable<string> BuildUsernameCandidates(string username, string host)
    {
        var user = username.Trim();
        var set = new List<string>();
        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !set.Contains(value, StringComparer.OrdinalIgnoreCase))
                set.Add(value);
        }
        Add(user);
        if (!user.Contains('\\') && !user.Contains('@'))
        {
            Add($@"{host}\{user}");
            Add($@".\{user}");
        }
        return set;
    }

    private static string DescribeSmbError(int code, string rootSharePath, string fullPath)
    {
        var hint = code switch
        {
            5 => "권한이 없습니다. 계정 권한 또는 공유 권한을 확인하세요.",
            53 => "네트워크 경로를 찾을 수 없습니다. NAS IP/이름과 PC가 같은 네트워크인지 확인하세요.",
            67 => "네트워크 이름(공유 폴더)을 찾을 수 없습니다. 공유명\\하위폴더 형식을 확인하세요.",
            86 => "지정한 네트워크 암호가 올바르지 않습니다.",
            1326 => "사용자 이름 또는 비밀번호가 올바르지 않습니다.",
            1219 => "같은 NAS에 다른 계정으로 이미 연결되어 있습니다.\n형제 앱(스케줄 관리자·FileChecker)과 탐색기가 쓰는 세션을 보호하기 위해 자동으로 끊지 않습니다.\n· 모든 앱·탐색기에서 같은 NAS 계정을 쓰거나\n· 해당 NAS 탐색기 창을 닫고 cmd에서  net use \\\\서버 /delete /y  후 다시 연결하세요.",
            64 => "네트워크 이름이 삭제되었거나 서버에 닿지 않습니다.",
            1231 => "NAS SMB 서비스 활성화를 확인하세요.",
            _ => "공유명, IP, 계정 정보를 다시 확인하세요.",
        };
        return $"SMB 연결에 실패했습니다.\n대상: {rootSharePath}\n작업 경로: {fullPath}\n오류 코드: {code} (0x{code:X})\n{hint}";
    }

    public sealed class WriteResult
    {
        public bool Saved { get; init; }
        public string Revision { get; init; } = "";
        public string? Content { get; init; }
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct NetResource
{
    public int Scope;
    public int Type;
    public int DisplayType;
    public int Usage;
    public string? LocalName;
    public string? RemoteName;
    public string? Comment;
    public string? Provider;
}

static class NativeMethods
{
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    public static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? username, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
