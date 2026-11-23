using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const string AppUrl = "http://127.0.0.1:17822/";
const string BridgeWorkLogUrl = "http://127.0.0.1:17820/worklog/";
const string AppName = "WorkLog";

// 나스브릿지가 켜져 있으면 단독 17822 대신 공유 세션(/worklog)으로 연다
if (TryOpenViaBroadcastNasBridge())
    return;

// 단일 실행 보장
using var mutex = new Mutex(true, "WorkLog.MacBridge", out var createdNew);
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

    var connection = new MacWorkLogStore();
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    var browserSession = new BrowserSessionGuard(lifetime);

    app.MapGet("/", () => EmbeddedFile("WorkLog.index.html", "text/html; charset=utf-8"));
    app.MapGet("/index.html", () => EmbeddedFile("WorkLog.index.html", "text/html; charset=utf-8"));
    app.MapGet("/styles.css", () => EmbeddedFile("WorkLog.styles.css", "text/css; charset=utf-8"));
    app.MapGet("/app.js", () => EmbeddedFile("WorkLog.app.js", "text/javascript; charset=utf-8"));
    app.MapGet("/templates.js", () => EmbeddedFile("WorkLog.templates.js", "text/javascript; charset=utf-8"));

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
        try { return Results.Ok(connection.ListScheduleJsonFiles()); }
        catch (Exception exception) { return Results.BadRequest(new { message = exception.Message }); }
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
        try { return Results.Text(connection.ReadAudit(month), "application/x-ndjson; charset=utf-8"); }
        catch (Exception exception) { return Results.BadRequest(new { message = exception.Message }); }
    });

    app.MapGet("/api/edit-lock", (string date) =>
    {
        try { return Results.Ok(new { @lock = connection.GetEditLock(date) }); }
        catch (Exception exception) { return Results.BadRequest(new { message = exception.Message }); }
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
        catch (Exception exception) { return Results.BadRequest(new { message = exception.Message }); }
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
        catch (Exception exception) { return Results.BadRequest(new { message = exception.Message }); }
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
        catch (Exception exception) { return Results.BadRequest(new { message = exception.Message }); }
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
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = AppUrl,
            UseShellExecute = false,
        });
    }
    catch { /* ignore */ }
}

/// <summary>
/// BroadcastNasBridge(17820)가 살아 있으면 업무일지를 브리지 /worklog 로 연다.
/// 허브에서 NAS 설정을 끝낸 뒤에도 Mac WorkLog.app 이 단독 로그인 화면을 띄우던 문제 방지.
/// </summary>
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

        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = BridgeWorkLogUrl,
            UseShellExecute = false,
        });
        return true;
    }
    catch
    {
        return false;
    }
}

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

static void ShowInfo(string message) => RunOsascriptDialog(message, "WorkLog");
static void ShowError(string message) => RunOsascriptDialog(message, "WorkLog 오류");

static void RunOsascriptDialog(string message, string title)
{
    try
    {
        var escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var titleEsc = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e \"display dialog \\\"{escaped}\\\" with title \\\"{titleEsc}\\\" buttons {{\\\"확인\\\"}} default button 1\"",
            UseShellExecute = false,
        })?.WaitForExit(8000);
    }
    catch { Console.Error.WriteLine($"{title}: {message}"); }
}

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
                    await Task.Delay(900, token);
                    Stop();
                }
                catch (OperationCanceledException) { }
            }, CancellationToken.None);
        }
    }

    private void CheckWatchdog()
    {
        DateTime? last;
        lock (gate) last = lastPingUtc;
        if (last is null) return;
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
        try { lifetime.StopApplication(); } catch { }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                Environment.Exit(0);
            }
            catch { }
        });
    }

    private void CancelExitLocked()
    {
        if (exitCts is null) return;
        try { exitCts.Cancel(); } catch { }
        exitCts.Dispose();
        exitCts = null;
    }

    public void Dispose()
    {
        watchdog.Dispose();
        lock (gate) CancelExitLocked();
    }
}

/// <summary>
/// macOS에서 SMB 공유를 mount_smbfs로 연결한 뒤 일지 폴더를 다룬다.
/// </summary>
sealed class MacWorkLogStore
{
    private string? root;
    private string? scheduleRoot;
    private string? mountPoint;
    private readonly object fileGate = new();

    private static readonly string SeedProfiles = """
        {"version":1,"profiles":[{"id":"ko_yunseok","displayName":"고윤석"},{"id":"kim_jonghee","displayName":"김종희"},{"id":"lim_wonbin","displayName":"임원빈"}]}
        """;

    private static readonly string SeedDefaults = """
        {"version":2,"customTemplates":[],"titleRules":[{"id":"rule-daejundo","conditionType":"schedule_title","value":"대전도","record":"{schedule_title} {place_label} 방송 및 유튜브 송출 준비.","templates":["BibleSeminar_Main"]}],"templateButtons":{"Sermon_Sunday_Primary":{"appendLog":"주일 말씀 방송 준비"},"Sermon_Wednesday":{"appendLog":"수요 말씀 방송 준비"},"Sermon_Thursday":{"appendLog":"오전 수요말씀 방송 준비"}}}
        """;

    public bool IsConnected => root is not null;
    public string? Root => root;
    public string? ScheduleRoot => scheduleRoot;

    public void Connect(string host, string share, string username, string password)
    {
        var (normalizedHost, normalizedShare) = NormalizeHostAndShare(host, share);
        if (string.IsNullOrWhiteSpace(normalizedHost))
            throw new ArgumentException("NAS IP 또는 호스트 이름을 확인하세요.");
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("사용자 계정을 입력하세요.");
        if (string.IsNullOrWhiteSpace(normalizedShare))
            throw new ArgumentException("공유 경로를 입력하세요. 예: Temp DATA/_data/_work_log");

        Disconnect();

        var parts = normalizedShare.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var shareName = parts[0];
        var subPath = parts.Length > 1 ? string.Join('/', parts.Skip(1)) : "";

        mountPoint = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "WorkLog", "mnt");
        Directory.CreateDirectory(mountPoint);

        // 이미 마운트돼 있으면 정리
        TryUnmount(mountPoint);

        var userEnc = Uri.EscapeDataString(username.Trim());
        var passEnc = Uri.EscapeDataString(password ?? "");
        var shareEnc = string.Join('/', shareName.Split('/').Select(Uri.EscapeDataString));
        var smbUrl = $"//{userEnc}:{passEnc}@{normalizedHost}/{shareEnc}";

        var mountResult = RunArgs("mount_smbfs", smbUrl, mountPoint);
        if (mountResult.ExitCode != 0)
        {
            // Finder 마운트 볼륨이 이미 있는 경우 탐색
            var volumesFallback = FindExistingVolume(normalizedHost, shareName);
            if (volumesFallback is null)
            {
                throw new InvalidOperationException(
                    $"SMB 마운트에 실패했습니다.\n{mountResult.Output}\n\n" +
                    "Finder에서 먼저 smb:// 로 연결한 뒤, 공유 경로를 다시 확인해 주세요.");
            }
            mountPoint = volumesFallback;
        }

        var workRoot = string.IsNullOrEmpty(subPath)
            ? mountPoint
            : Path.Combine(mountPoint, subPath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(workRoot))
        {
            try { Directory.CreateDirectory(workRoot); }
            catch (Exception ex)
            {
                Disconnect();
                throw new InvalidOperationException($"작업일지 폴더에 접근할 수 없습니다.\n{workRoot}\n{ex.Message}", ex);
            }
        }

        root = workRoot;
        scheduleRoot = Directory.GetParent(workRoot)?.FullName;
        EnsureLayout();
    }

    private static string? FindExistingVolume(string host, string shareName)
    {
        var volumes = "/Volumes";
        if (!Directory.Exists(volumes)) return null;
        foreach (var dir in Directory.EnumerateDirectories(volumes))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals(shareName, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(shareName, StringComparison.OrdinalIgnoreCase))
                return dir;
        }
        return null;
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

    public void Disconnect()
    {
        root = null;
        scheduleRoot = null;
        if (!string.IsNullOrEmpty(mountPoint) &&
            mountPoint.Contains("Application Support/WorkLog", StringComparison.Ordinal))
        {
            TryUnmount(mountPoint);
        }
        mountPoint = null;
    }

    private static void TryUnmount(string point)
    {
        RunArgs("umount", point);
        RunArgs("diskutil", "unmount", "force", point);
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
                File.WriteAllText(path, contents, Encoding.UTF8);
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
        var path = ResolvePath($"audit/{month}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var trimmed = lineOrJson.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("감사 로그 내용이 비어 있습니다.");
        using (JsonDocument.Parse(trimmed)) { }
        lock (fileGate)
            File.AppendAllText(path, trimmed.Replace("\r", "").Replace("\n", " ") + Environment.NewLine, Encoding.UTF8);
    }

    public string ReadAudit(string month)
    {
        EnsureConnected();
        ValidateMonth(month);
        var path = ResolvePath($"audit/{month}.jsonl");
        if (!File.Exists(path)) return "";
        lock (fileGate) return File.ReadAllText(path, Encoding.UTF8);
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
        catch { return null; }
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

    private static JsonSerializerOptions EditLockJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string[] ListScheduleJsonFiles()
    {
        EnsureConnected();
        if (string.IsNullOrEmpty(scheduleRoot) || !Directory.Exists(scheduleRoot)) return [];
        return Directory.EnumerateFiles(scheduleRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith('_'))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public (string Json, string Revision) ReadScheduleJson(string name)
    {
        EnsureConnected();
        if (string.IsNullOrEmpty(scheduleRoot)) throw new InvalidOperationException("스케줄 폴더를 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name ||
            !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 스케줄 파일명입니다.");
        var path = Path.Combine(scheduleRoot, name);
        if (!File.Exists(path)) throw new FileNotFoundException($"스케줄 파일이 없습니다: {name}");
        lock (fileGate) return (File.ReadAllText(path, Encoding.UTF8), GetFileRevision(path));
    }

    private string ResolvePath(string relativePath)
    {
        EnsureConnected();
        var normalized = (relativePath ?? "").Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
            throw new ArgumentException("허용되지 않은 경로입니다.");
        var full = Path.GetFullPath(Path.Combine(root!, normalized));
        var rootFull = Path.GetFullPath(root!);
        if (!full.StartsWith(rootFull, StringComparison.Ordinal))
            throw new ArgumentException("루트 밖 경로는 허용되지 않습니다.");
        return full;
    }

    private static void ValidateMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month) || !Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
            throw new ArgumentException("month는 YYYY-MM 형식이어야 합니다.");
    }

    private static string GetFileRevision(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "0";

    private void EnsureConnected()
    {
        if (root is null) throw new InvalidOperationException("먼저 SMB 연결을 확인하세요.");
    }

    private static (string host, string share) NormalizeHostAndShare(string host, string share)
    {
        var rawHost = (host ?? "").Trim().Trim('"');
        var rawShare = (share ?? "").Trim().Trim('"').Replace('\\', '/').Trim('/');

        rawHost = rawHost
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("smb://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Trim('/', '\\');

        if (rawHost.Contains('/'))
        {
            var idx = rawHost.IndexOf('/');
            if (string.IsNullOrWhiteSpace(rawShare))
                rawShare = rawHost[(idx + 1)..];
            rawHost = rawHost[..idx];
        }

        var colon = rawHost.IndexOf(':');
        if (colon > 0 && int.TryParse(rawHost[(colon + 1)..], out _))
            rawHost = rawHost[..colon];

        return (rawHost, rawShare);
    }

    private static (int ExitCode, string Output) RunArgs(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi);
            if (process is null) return (-1, "프로세스를 시작하지 못했습니다.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, (stdout + "\n" + stderr).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public sealed class WriteResult
    {
        public bool Saved { get; init; }
        public string Revision { get; init; } = "";
        public string? Content { get; init; }
    }
}
