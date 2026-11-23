using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

const string AppUrl = "http://127.0.0.1:17821/";
const string BridgeScheduleUrl = "http://127.0.0.1:17820/schedule/";

// 나스브릿지가 켜져 있으면 단독 17821 마운트 대신 공유 세션(/schedule) 사용 (Mac 3중 마운트 방지)
if (TryOpenViaBroadcastNasBridge())
    return;

var mutexName = OperatingSystem.IsWindows()
    ? @"Local\ScheduleDataManager.LocalBridge"
    : "ScheduleDataManager.LocalBridge";
using var mutex = new Mutex(true, mutexName, out var createdNew);
if (!createdNew)
{
    if (TryOpenViaBroadcastNasBridge())
        return;
    OpenAppPage();
    ShowInfo("Broadcasting Schedule가 이미 실행 중입니다.\n기존 창을 브라우저에서 엽니다.");
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

    var connection = new SmbConnection();
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    var browserSession = new BrowserSessionGuard(lifetime);

    app.MapGet("/", () => EmbeddedFile("ScheduleDataManager.index.html", "text/html; charset=utf-8"));
    app.MapGet("/index.html", () => EmbeddedFile("ScheduleDataManager.index.html", "text/html; charset=utf-8"));
    app.MapGet("/styles.css", () => EmbeddedFile("ScheduleDataManager.styles.css", "text/css; charset=utf-8"));
    app.MapGet("/app.js", () => EmbeddedFile("ScheduleDataManager.app.js", "text/javascript; charset=utf-8"));
    app.MapGet("/sw.js", () => EmbeddedFile("ScheduleDataManager.sw.js", "text/javascript; charset=utf-8"));
    app.MapGet("/assets/icons/{name}", (string name) =>
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains('/') ||
            name.Contains('\\') ||
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            name.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.')))
        {
            return Results.BadRequest();
        }

        return EmbeddedFile($"ScheduleDataManager.assets.icons.{name}", "image/png");
    });

    app.MapGet("/api/status", () => Results.Ok(new
    {
        app = "BroadcastingSchedule",
        port = 17821,
        connected = connection.IsConnected,
        root = connection.Root,
    }));
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

    app.MapPost("/api/connect", async (ConnectRequest request) =>
    {
        try
        {
            connection.Connect(request.Host, request.Share, request.Username, request.Password);
            return Results.Ok(new { connected = true, root = connection.Root });
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

    app.MapGet("/api/files", () =>
    {
        try
        {
            var files = connection.GetJsonFiles();
            return Results.Ok(files);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/file", (HttpResponse response, string name) =>
    {
        try
        {
            var (json, revision) = connection.ReadJsonWithRevision(name);
            response.Headers["X-File-Revision"] = revision;
            return Results.Text(json, "application/json; charset=utf-8");
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPut("/api/file", async (string name, HttpRequest request) =>
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var expected = request.Headers["X-Expected-Revision"].FirstOrDefault();
            var result = connection.WriteJsonWithRevision(name, body, expected);
            if (!result.Saved)
            {
                return Results.Json(
                    new { message = "파일이 다른 곳에서 변경되었습니다.", revision = result.Revision, content = result.Content },
                    statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Ok(new { saved = true, revision = result.Revision });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/backup", async (string name, HttpRequest request) =>
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var result = connection.CreateBackup(name, await reader.ReadToEndAsync());
            return Results.Ok(result);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/backups", (string name) =>
    {
        try
        {
            return Results.Ok(connection.ListBackups(name));
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/restore", (HttpResponse response, string name, string backup) =>
    {
        try
        {
            var (json, revision) = connection.RestoreBackupWithRevision(name, backup);
            response.Headers["X-File-Revision"] = revision;
            return Results.Text(json, "application/json; charset=utf-8");
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/documents", () =>
    {
        try
        {
            return Results.Ok(connection.ListOfficialDocuments());
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/document", (string name) =>
    {
        try
        {
            var (bytes, contentType) = connection.ReadOfficialDocument(name);
            return Results.File(bytes, contentType);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/document/upload", async (HttpRequest request) =>
    {
        try
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "multipart/form-data 가 필요합니다." });
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length <= 0)
                return Results.BadRequest(new { message = "이미지 파일이 필요합니다." });
            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var result = connection.UploadOfficialDocument(
                memory.ToArray(),
                file.FileName,
                form["scheduleId"].ToString(),
                form["scheduleDate"].ToString(),
                form["scheduleTitle"].ToString());
            return Results.Ok(result);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPost("/api/document/link", async (HttpRequest request) =>
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var payload = JsonSerializer.Deserialize<DocumentLinkRequest>(
                await reader.ReadToEndAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null)
                return Results.BadRequest(new { message = "요청 본문이 필요합니다." });
            var result = connection.LinkOfficialDocument(
                payload.FileName,
                payload.ScheduleId,
                payload.ScheduleDate,
                payload.ScheduleTitle);
            return Results.Ok(result);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/document/index", () =>
    {
        try
        {
            return Results.Text(connection.ReadOfficialDocumentIndex(), "application/json; charset=utf-8");
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapPut("/api/document/index", async (HttpRequest request) =>
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            connection.WriteOfficialDocumentIndex(await reader.ReadToEndAsync());
            return Results.Ok(new { saved = true });
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/lan-devices", async () =>
    {
        try
        {
            var devices = await LanScanner.DiscoverAsync();
            return Results.Ok(devices);
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
    OpenAppPage();
    var killHint = OperatingSystem.IsWindows()
        ? "문제가 계속되면 작업 관리자에서 BroadcastingSchedule.exe를 종료하세요."
        : "문제가 계속되면 활성 상태 모니터에서 BroadcastingSchedule 프로세스를 종료하세요.";
    ShowInfo($"포트 17821이 이미 사용 중입니다.\n기존 Broadcasting Schedule 창을 브라우저에서 엽니다.\n{killHint}");
}
catch (Exception exception)
{
    ShowError($"Broadcasting Schedule를 시작하지 못했습니다.\n\n{exception.Message}");
}

static void OpenAppPage()
{
    try
    {
        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { AppUrl },
                UseShellExecute = false,
            });
            return;
        }

        Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true });
    }
    catch
    {
        // ignore browser launch failures
    }
}

/// <summary>브리지(17820)가 있으면 /schedule 로 열어 단독 SMB 마운트를 하지 않는다.</summary>
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

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { BridgeScheduleUrl },
                UseShellExecute = false,
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo(BridgeScheduleUrl) { UseShellExecute = true });
        }
        return true;
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

static void ShowInfo(string message) => ShowNativeDialog(message, isError: false);

static void ShowError(string message) => ShowNativeDialog(message, isError: true);

static void ShowNativeDialog(string message, bool isError)
{
    if (OperatingSystem.IsWindows())
    {
        NativeMethods.MessageBox(IntPtr.Zero, message, "Broadcasting Schedule", isError ? 0x00000010u : 0x00000040u);
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        try
        {
            var escaped = message
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList =
                {
                    "-e",
                    $"display dialog \"{escaped}\" with title \"Broadcasting Schedule\" buttons {{\"OK\"}} default button 1 with icon {(isError ? "stop" : "note")}",
                },
                UseShellExecute = false,
            })?.WaitForExit(15000);
            return;
        }
        catch
        {
            // fall through to console
        }
    }

    Console.Error.WriteLine(message);
}

IResult EmbeddedFile(string resourceName, string contentType)
{
    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
    return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
}

record ConnectRequest(string Host, string Share, string Username, string Password);
record DocumentLinkRequest(string FileName, string ScheduleId, string ScheduleDate, string ScheduleTitle);

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
                    // 새로고침 시 곧이어 ping이 오면 취소됨
                    await Task.Delay(2800, token);
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
        // 브라우저가 한 번도 연결되지 않았으면 대기 (시작 직후)
        if (last is null) return;
        // 백그라운드 탭에서 interval이 느려질 수 있어 여유를 둠
        if (DateTime.UtcNow - last.Value > TimeSpan.FromSeconds(60))
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

sealed class SmbConnection
{
    private string? root;
    private string? macMountPoint;
    /// <summary>앱이 mount_smbfs 로 만든 마운트면 true. /Volumes Finder 마운트면 false.</summary>
    private bool macMountOwned;
    public bool IsConnected => root is not null;
    public string? Root => root;

    public void Connect(string host, string share, string username, string password)
    {
        var (normalizedHost, normalizedShare) = NormalizeHostAndShare(host, share);
        if (string.IsNullOrWhiteSpace(normalizedHost))
            throw new ArgumentException("NAS IP 또는 호스트 이름을 확인하세요.");
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("사용자 계정을 입력하세요.");
        if (string.IsNullOrWhiteSpace(normalizedShare))
            throw new ArgumentException("공유 폴더명(Share 이름)을 입력하세요. 예: schedule");

        if (OperatingSystem.IsWindows())
            ConnectWindows(normalizedHost, normalizedShare, username, password);
        else if (OperatingSystem.IsMacOS())
            ConnectMac(normalizedHost, normalizedShare, username, password);
        else
            throw new PlatformNotSupportedException("현재 OS에서는 SMB 연결을 지원하지 않습니다. Windows 또는 macOS를 사용하세요.");
    }

    private void ConnectWindows(string normalizedHost, string normalizedShare, string username, string password)
    {
        var parts = normalizedShare.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var rootShareName = parts[0];
        // 공유명에 공백이 있어도 UNC를 직접 조합 (Path.Combine은 UNC+공백에서 깨질 수 있음)
        var rootNetworkPath = $@"\\{normalizedHost}\{rootShareName}";
        var fullNetworkPath = parts.Length > 1
            ? $@"{rootNetworkPath}\{string.Join('\\', parts.Skip(1))}"
            : rootNetworkPath;

        // WorkLog 등과 같은 NAS를 동시에 쓸 수 있게, 기존 세션을 먼저 끊지 않음
        DisconnectLocalOnly();

        if (CanUseUncPath(fullNetworkPath))
        {
            root = fullNetworkPath;
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

            // 다른 자격 증명으로 이미 연결된 경우: 강제 끊기 전에 경로 접근만 확인
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

        // 이미 동일 서버에 연결돼 있고 경로만 쓸 수 있으면 성공 처리
        if (usedUser is null && CanUseUncPath(fullNetworkPath))
        {
            usedUser = username;
            lastError = 0;
        }

        // 다른 앱(WorkLog·FileChecker·탐색기)이 쓰는 SMB 세션을 끊지 않음.
        // 자격 증명 충돌(1219 등)이면 강제 해제 대신 안내만 한다.
        if (lastError != 0 || usedUser is null)
            throw new InvalidOperationException(DescribeSmbError(lastError, rootNetworkPath, fullNetworkPath));

        root = fullNetworkPath;
        try
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(
                    $"SMB 인증은 됐지만 경로에 접근할 수 없습니다.\n확인 경로: {root}\n공유명 아래 하위 폴더가 있다면 '공유명\\하위폴더' 형식으로 입력하세요.");
            _ = Directory.GetFiles(root);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Disconnect();
            throw new InvalidOperationException(
                $"SMB 연결 후 경로 접근 중 오류입니다.\n경로: {root}\n{ex.Message}", ex);
        }
    }

    private void ConnectMac(string normalizedHost, string normalizedShare, string username, string password)
    {
        Disconnect();

        var parts = normalizedShare.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rootShareName = parts[0];
        var subPath = parts.Length > 1 ? string.Join(Path.DirectorySeparatorChar, parts.Skip(1)) : null;

        // Finder 등으로 이미 /Volumes/<share> 에 올라와 있으면 재사용
        var volumesPath = Path.Combine("/Volumes", rootShareName);
        if (Directory.Exists(volumesPath))
        {
            macMountPoint = volumesPath;
            macMountOwned = false;
            root = string.IsNullOrEmpty(subPath) ? volumesPath : Path.Combine(volumesPath, subPath);
            try
            {
                if (!Directory.Exists(root))
                    throw new DirectoryNotFoundException(
                        $"SMB 경로에 접근할 수 없습니다.\n확인 경로: {root}\n공유명 아래 하위 폴더가 있다면 '공유명/하위폴더' 형식으로 입력하세요.");
                _ = Directory.GetFiles(root);
                return;
            }
            catch (Exception ex) when (ex is not InvalidOperationException and not DirectoryNotFoundException)
            {
                macMountPoint = null;
                root = null;
                throw new InvalidOperationException(
                    $"이미 마운트된 /Volumes 경로 접근 중 오류입니다.\n경로: {root}\n{ex.Message}", ex);
            }
        }

        // 호스트+공유별 고정 마운트 경로 (Guid 매번 생성 → File exists 재발 방지)
        var safeShare = string.Concat(rootShareName.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        var mountPoint = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "BroadcastingSchedule",
            "mnt",
            $"{normalizedHost}_{safeShare}");

        PrepareEmptyMountPoint(mountPoint);

        Exception? lastError = null;
        foreach (var candidate in BuildMacUsernameCandidates(username, normalizedHost))
        {
            try
            {
                MountSmbfs(normalizedHost, rootShareName, candidate, password, mountPoint);
                lastError = null;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryUnmount(mountPoint, force: true);
                PrepareEmptyMountPoint(mountPoint);
            }
        }

        if (lastError is not null)
        {
            // 실패 후에도 Finder 마운트가 생기면 폴백
            if (Directory.Exists(volumesPath))
            {
                macMountPoint = volumesPath;
                macMountOwned = false;
                root = string.IsNullOrEmpty(subPath) ? volumesPath : Path.Combine(volumesPath, subPath);
                if (Directory.Exists(root))
                {
                    _ = Directory.GetFiles(root);
                    return;
                }
            }

            TryDeleteDirectory(mountPoint);
            throw new InvalidOperationException(
                $"SMB 연결에 실패했습니다.\n대상: //{normalizedHost}/{rootShareName}\n{lastError.Message}\n" +
                "· NAS IP·공유명·계정·비밀번호를 확인하세요.\n" +
                "· Finder에서 해당 공유를 먼저 연결해 두면 /Volumes 경로를 재사용할 수 있습니다.\n" +
                "· macOS 시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크에서 이 앱을 허용했는지 확인하세요.");
        }

        macMountPoint = mountPoint;
        macMountOwned = true;
        root = string.IsNullOrEmpty(subPath) ? mountPoint : Path.Combine(mountPoint, subPath);
        try
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(
                    $"SMB 인증은 됐지만 경로에 접근할 수 없습니다.\n확인 경로: {root}\n공유명 아래 하위 폴더가 있다면 '공유명/하위폴더' 형식으로 입력하세요.");
            _ = Directory.GetFiles(root);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Disconnect();
            throw new InvalidOperationException(
                $"SMB 연결 후 경로 접근 중 오류입니다.\n경로: {root}\n{ex.Message}", ex);
        }
    }

    private static void PrepareEmptyMountPoint(string mountPoint)
    {
        TryUnmount(mountPoint, force: true);
        try
        {
            if (Directory.Exists(mountPoint))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(mountPoint))
                {
                    try
                    {
                        if (Directory.Exists(entry)) Directory.Delete(entry, true);
                        else File.Delete(entry);
                    }
                    catch { /* ignore */ }
                }
                try { Directory.Delete(mountPoint, false); }
                catch
                {
                    TryUnmount(mountPoint, force: true);
                    try { Directory.Delete(mountPoint, true); } catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }

        Directory.CreateDirectory(Path.GetDirectoryName(mountPoint)!);
        Directory.CreateDirectory(mountPoint);
    }

    private static void MountSmbfs(string host, string shareName, string username, string password, string mountPoint)
    {
        var sources = new[]
        {
            BuildMacMountSource(host, shareName, username, password, smbScheme: false),
            BuildMacMountSource(host, shareName, username, password, smbScheme: true),
        };

        Exception? lastError = null;
        foreach (var source in sources.Distinct(StringComparer.Ordinal))
        {
            foreach (var nobrowse in new[] { true, false })
            {
                try
                {
                    PrepareEmptyMountPoint(mountPoint);
                    RunMountSmbfs(source, mountPoint, nobrowse);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    TryUnmount(mountPoint, force: true);
                }
            }
        }

        throw lastError ?? new InvalidOperationException("mount_smbfs 실패");
    }

    private static void RunMountSmbfs(string source, string mountPoint, bool nobrowse = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/sbin/mount_smbfs",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (nobrowse)
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("nobrowse");
        }
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(mountPoint);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("mount_smbfs를 실행할 수 없습니다.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("SMB 마운트 시간이 초과되었습니다.");
        }

        if (process.ExitCode != 0)
        {
            var detail = string.Join('\n', new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"mount_smbfs 실패 (코드 {process.ExitCode})"
                : detail);
        }
    }

    private static string BuildMacMountSource(
        string host,
        string shareName,
        string username,
        string password,
        bool smbScheme = false)
    {
        // mount_smbfs URL: //[[[domain;]user[:password]@]server[/share]
        // 공백 포함 공유명(예: Temp DATA)은 반드시 percent-encoding 필요.
        var user = username.Trim();
        string auth;
        if (user.Contains('\\'))
        {
            var parts = user.Split('\\', 2);
            auth = $"{EscapeMountComponent(parts[0])};{EscapeMountComponent(parts[1])}:{EscapeMountComponent(password)}";
        }
        else if (user.Contains(';'))
        {
            var sep = user.IndexOf(';');
            auth = $"{EscapeMountComponent(user[..sep])};{EscapeMountComponent(user[(sep + 1)..])}:{EscapeMountComponent(password)}";
        }
        else
        {
            auth = $"{EscapeMountComponent(user)}:{EscapeMountComponent(password)}";
        }

        var encodedShare = string.Join('/',
            shareName.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(EscapeMountComponent));

        var path = $"//{auth}@{host}/{encodedShare}";
        return smbScheme ? $"smb:{path}" : path;
    }

    private static string EscapeMountComponent(string value) =>
        Uri.EscapeDataString(value ?? string.Empty);

    private static IEnumerable<string> BuildMacUsernameCandidates(string username, string host)
    {
        var user = username.Trim();
        var set = new List<string>();
        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !set.Contains(value, StringComparer.OrdinalIgnoreCase))
                set.Add(value);
        }

        Add(user);
        if (!user.Contains('\\') && !user.Contains(';') && !user.Contains('@'))
        {
            Add($"{host};{user}");
            Add($".;{user}");
        }
        return set;
    }

    private static void TryUnmount(string mountPoint, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(mountPoint)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/sbin/umount",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (force) psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(mountPoint);
            using var process = Process.Start(psi);
            process?.WaitForExit(10000);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);
        }
        catch
        {
            // ignore busy mount leftovers
        }
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

        // 호스트 칸에 \\IP\share 또는 IP\share 를 붙여 넣은 경우 분리
        var fromHost = TryParseUnc(rawHost);
        if (fromHost is not null)
        {
            rawHost = fromHost.Value.host;
            if (string.IsNullOrWhiteSpace(rawShare))
                rawShare = fromHost.Value.shareAndPath;
            else if (!rawShare.Contains('\\') && !rawShare.Contains('/'))
                rawShare = $"{fromHost.Value.shareAndPath}";
            else
                rawShare = $"{fromHost.Value.shareAndPath}\\{rawShare.Trim('\\', '/')}";
        }

        // 공유 칸에 \\IP\share\path 형태가 들어온 경우
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

        // 포트가 붙은 경우 제거 (192.168.0.10:445)
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
        // "Temp DATA\_data" 같은 공유명\하위경로 는 UNC가 아님
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
            67 => "네트워크 이름(공유 폴더)을 찾을 수 없습니다.\n· '공유 폴더명'에는 탐색기 주소 \\IP\\ 다음에 보이는 첫 공유 이름을 넣으세요.\n· 하위 폴더면 공유명\\하위폴더 형식입니다.\n· Synology/QNAP의 표시 이름과 공유 이름이 다를 수 있습니다.",
            86 => "지정한 네트워크 암호가 올바르지 않습니다.",
            1326 => "사용자 이름 또는 비밀번호가 올바르지 않습니다.\nNAS 로컬 계정이면 '사용자명'만, 도메인이면 DOMAIN\\user 형식을 시도하세요.",
            1219 => "같은 NAS에 다른 계정으로 이미 연결되어 있습니다.\n형제 앱(WorkLog·FileChecker)과 탐색기가 쓰는 세션을 보호하기 위해 자동으로 끊지 않습니다.\n· 모든 앱·탐색기에서 같은 NAS 계정을 쓰거나\n· 해당 NAS 탐색기 창을 닫고 cmd에서  net use \\\\서버 /delete /y  후 다시 연결하세요.",
            64 => "네트워크 이름이 삭제되었거나 서버에 닿지 않습니다.",
            1231 => "네트워크 위치가 원격 연결을 수락하지 않습니다. NAS SMB 서비스 활성화를 확인하세요.",
            _ => "공유명, IP, 계정 정보를 다시 확인하세요.",
        };
        return $"SMB 연결에 실패했습니다.\n대상: {rootSharePath}\n작업 경로: {fullPath}\n오류 코드: {code} (0x{code:X})\n{hint}";
    }

    public void Disconnect()
    {
        if (root is null && macMountPoint is null) return;

        if (OperatingSystem.IsMacOS())
        {
            var mount = macMountPoint;
            var owned = macMountOwned;
            root = null;
            macMountPoint = null;
            macMountOwned = false;
            if (owned && !string.IsNullOrWhiteSpace(mount))
            {
                TryUnmount(mount, force: true);
                TryDeleteDirectory(mount);
            }
            return;
        }

        // Windows: SMB 세션은 유지 (WorkLog 등 동일 NAS 동시 사용)
        DisconnectLocalOnly();
    }

    private void DisconnectLocalOnly()
    {
        root = null;
        // macMountPoint 는 ConnectMac 쪽에서만 다룸
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

    public string[] GetJsonFiles()
    {
        EnsureConnected();
        return Directory.EnumerateFiles(root!, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => !name.StartsWith('_'))
            .OrderBy(name => name)
            .ToArray();
    }

    public string ReadJson(string name)
    {
        return ReadJsonWithRevision(name).Json;
    }

    public (string Json, string Revision) ReadJsonWithRevision(string name)
    {
        var path = Resolve(name);
        var json = File.ReadAllText(path);
        return (json, GetFileRevision(path));
    }

    public void WriteJson(string name, string contents)
    {
        WriteJsonWithRevision(name, contents, expectedRevision: null);
    }

    public WriteJsonResult WriteJsonWithRevision(string name, string contents, string? expectedRevision)
    {
        var path = Resolve(name);
        if (!string.IsNullOrEmpty(expectedRevision) && File.Exists(path))
        {
            var current = GetFileRevision(path);
            if (!string.Equals(current, expectedRevision, StringComparison.Ordinal))
            {
                return new WriteJsonResult
                {
                    Saved = false,
                    Revision = current,
                    Content = File.ReadAllText(path),
                };
            }
        }

        using var document = JsonDocument.Parse(contents);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }));
        return new WriteJsonResult
        {
            Saved = true,
            Revision = GetFileRevision(path),
        };
    }

    private static string GetFileRevision(string path) =>
        File.GetLastWriteTimeUtc(path).Ticks.ToString();

    public sealed class WriteJsonResult
    {
        public bool Saved { get; init; }
        public string Revision { get; init; } = "";
        public string? Content { get; init; }
    }

    public object CreateBackup(string name, string contents)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        using var document = JsonDocument.Parse(contents);
        var pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });

        var backupDir = Path.Combine(root!, "_backup");
        Directory.CreateDirectory(backupDir);

        var baseName = Path.GetFileNameWithoutExtension(name);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipName = $"{baseName}_{stamp}.zip";
        var zipPath = Path.Combine(backupDir, zipName);

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry($"{baseName}.json", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(pretty);
        }

        var info = new FileInfo(zipPath);
        return new
        {
            backup = zipName,
            folder = "_backup",
            size = info.Length,
            created = info.LastWriteTime,
        };
    }

    public object ListBackups(string name)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        var backupDir = Path.Combine(root!, "_backup");
        if (!Directory.Exists(backupDir)) return Array.Empty<object>();

        var baseName = Path.GetFileNameWithoutExtension(name);
        return Directory.EnumerateFiles(backupDir, $"{baseName}_*.zip")
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new
                {
                    name = info.Name,
                    size = info.Length,
                    modified = info.LastWriteTime,
                };
            })
            .OrderByDescending(item => item.modified)
            .ToArray();
    }

    public string RestoreBackup(string name, string backup) =>
        RestoreBackupWithRevision(name, backup).Json;

    public (string Json, string Revision) RestoreBackupWithRevision(string name, string backup)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        if (string.IsNullOrWhiteSpace(backup) || Path.GetFileName(backup) != backup || !backup.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 백업 파일명입니다.");

        var zipPath = Path.Combine(root!, "_backup", backup);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("백업 파일을 찾을 수 없습니다.", backup);

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(Path.GetDirectoryName(item.FullName)?.Trim('\\', '/')));
        if (entry is null)
            throw new InvalidOperationException("백업 ZIP 안에 JSON 파일이 없습니다.");

        using var reader = new StreamReader(entry.Open());
        var contents = reader.ReadToEnd();
        var written = WriteJsonWithRevision(name, contents, expectedRevision: null);
        return (contents, written.Revision);
    }

    private const string OfficialDocumentsFolder = "_official_documents";
    private const string OfficialDocumentsIndexFile = "links.json";
    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    public object[] ListOfficialDocuments()
    {
        EnsureConnected();
        var dir = GetOfficialDocumentsDirectory();
        if (!Directory.Exists(dir)) return Array.Empty<object>();
        return Directory.EnumerateFiles(dir)
            .Where(path => AllowedDocumentExtensions.Contains(Path.GetExtension(path)))
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new
                {
                    name = info.Name,
                    size = info.Length,
                    modified = info.LastWriteTime,
                };
            })
            .OrderByDescending(item => item.modified)
            .Cast<object>()
            .ToArray();
    }

    public (byte[] Bytes, string ContentType) ReadOfficialDocument(string name)
    {
        EnsureConnected();
        var safeName = ValidateDocumentFileName(name);
        var path = Path.Combine(GetOfficialDocumentsDirectory(), safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("공문 파일을 찾을 수 없습니다.", safeName);
        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg",
        };
        return (File.ReadAllBytes(path), contentType);
    }

    public object UploadOfficialDocument(
        byte[] bytes,
        string originalFileName,
        string scheduleId,
        string scheduleDate,
        string scheduleTitle)
    {
        EnsureConnected();
        if (bytes.Length == 0) throw new ArgumentException("빈 파일은 업로드할 수 없습니다.");
        if (bytes.Length > 20 * 1024 * 1024) throw new ArgumentException("공문 이미지는 20MB 이하여야 합니다.");

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedDocumentExtensions.Contains(ext))
            throw new ArgumentException("jpg, jpeg, png, gif, webp, bmp 이미지만 업로드할 수 있습니다.");

        var dir = GetOfficialDocumentsDirectory();
        Directory.CreateDirectory(dir);
        var storedName = BuildDocumentStoredName(dir, originalFileName, scheduleDate, ext);
        var targetPath = Path.Combine(dir, storedName);
        File.WriteAllBytes(targetPath, bytes);

        var entry = UpsertDocumentIndexEntry(storedName, originalFileName, scheduleId, scheduleDate, scheduleTitle);
        return new
        {
            fileName = storedName,
            folder = OfficialDocumentsFolder,
            originalName = originalFileName,
            entry,
        };
    }

    public object LinkOfficialDocument(
        string fileName,
        string scheduleId,
        string scheduleDate,
        string scheduleTitle)
    {
        EnsureConnected();
        var safeName = ValidateDocumentFileName(fileName);
        var path = Path.Combine(GetOfficialDocumentsDirectory(), safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("공문 파일을 찾을 수 없습니다.", safeName);
        var entry = UpsertDocumentIndexEntry(safeName, safeName, scheduleId, scheduleDate, scheduleTitle);
        return new
        {
            fileName = safeName,
            folder = OfficialDocumentsFolder,
            entry,
        };
    }

    public string ReadOfficialDocumentIndex()
    {
        EnsureConnected();
        var path = GetOfficialDocumentsIndexPath();
        if (!File.Exists(path))
        {
            return """{"documents":[]}""";
        }
        return File.ReadAllText(path);
    }

    public void WriteOfficialDocumentIndex(string contents)
    {
        EnsureConnected();
        using var document = JsonDocument.Parse(contents);
        var dir = GetOfficialDocumentsDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            GetOfficialDocumentsIndexPath(),
            JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    }

    private object UpsertDocumentIndexEntry(
        string fileName,
        string originalName,
        string scheduleId,
        string scheduleDate,
        string scheduleTitle)
    {
        var path = GetOfficialDocumentsIndexPath();
        Directory.CreateDirectory(GetOfficialDocumentsDirectory());

        using var document = File.Exists(path)
            ? JsonDocument.Parse(File.ReadAllText(path))
            : JsonDocument.Parse("""{"documents":[]}""");

        var documents = new List<Dictionary<string, object?>>();
        if (document.RootElement.TryGetProperty("documents", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var map = new Dictionary<string, object?>();
                foreach (var prop in item.EnumerateObject())
                {
                    map[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var n) ? n : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText(),
                    };
                }
                documents.Add(map);
            }
        }

        documents.RemoveAll(item =>
            string.Equals(Convert.ToString(item.GetValueOrDefault("scheduleId")), scheduleId, StringComparison.OrdinalIgnoreCase));

        var entry = new Dictionary<string, object?>
        {
            ["scheduleId"] = scheduleId ?? "",
            ["scheduleDate"] = scheduleDate ?? "",
            ["scheduleTitle"] = scheduleTitle ?? "",
            ["fileName"] = fileName,
            ["originalName"] = originalName ?? fileName,
            ["folder"] = OfficialDocumentsFolder,
            ["linkedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["schedule"] = new Dictionary<string, object?>
            {
                ["id"] = scheduleId ?? "",
                ["scheduleDate"] = scheduleDate ?? "",
                ["scheduleTitle"] = scheduleTitle ?? "",
                ["officialDocument"] = fileName,
            },
        };
        documents.Add(entry);

        var payload = new Dictionary<string, object?> { ["documents"] = documents };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return entry;
    }

    private string GetOfficialDocumentsDirectory() =>
        Path.Combine(root!, OfficialDocumentsFolder);

    private string GetOfficialDocumentsIndexPath() =>
        Path.Combine(GetOfficialDocumentsDirectory(), OfficialDocumentsIndexFile);

    private static string BuildDocumentStoredName(
        string directory,
        string originalFileName,
        string scheduleDate,
        string extension)
    {
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        var rawBase = Path.GetFileNameWithoutExtension(originalFileName ?? "");
        var baseName = SanitizeFileToken(rawBase, "document");
        var ymd = ToOfficialDocYmd8(scheduleDate);

        // 연결 목록 규칙: YYYYMMDD(8자리)로 시작해야 함
        if (!Regex.IsMatch(baseName, @"^\d{8}(?!\d)"))
            baseName = $"{ymd}_{baseName}";

        if (baseName.Length > 80)
            baseName = baseName[..80].TrimEnd('_');

        return EnsureUniqueDocumentFileName(directory, baseName + ext);
    }

    private static string ToOfficialDocYmd8(string scheduleDate)
    {
        if (!string.IsNullOrWhiteSpace(scheduleDate))
        {
            var trimmed = scheduleDate.Trim();
            if (Regex.IsMatch(trimmed, @"^\d{8}$"))
                return trimmed;
            if (DateTime.TryParse(trimmed, out var parsed))
                return parsed.ToString("yyyyMMdd");
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (digits.Length >= 8)
                return digits[..8];
        }
        return DateTime.Now.ToString("yyyyMMdd");
    }

    private static string EnsureUniqueDocumentFileName(string directory, string fileName)
    {
        var candidate = fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 1; File.Exists(Path.Combine(directory, candidate)); n++)
        {
            candidate = n <= 99
                ? $"{stem}_{n:00}{ext}"
                : $"{stem}_{Guid.NewGuid().ToString("N")[..6]}{ext}";
            if (n > 99) break;
        }
        return candidate;
    }

    private static string SanitizeFileToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) || ch is '/' or '\\' or ' ' ? '_' : ch).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        cleaned = cleaned.Trim('_');
        if (cleaned.Length > 40) cleaned = cleaned[..40].Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string ValidateDocumentFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            throw new ArgumentException("허용되지 않은 공문 파일명입니다.");
        if (!AllowedDocumentExtensions.Contains(Path.GetExtension(name)))
            throw new ArgumentException("허용되지 않은 공문 확장자입니다.");
        return name;
    }

    private string Resolve(string name)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        return Path.Combine(root!, name);
    }

    private static void ValidateJsonFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 파일명입니다.");
    }

    private void EnsureConnected()
    {
        if (root is null) throw new InvalidOperationException("먼저 SMB 연결을 확인하세요.");
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

static class LanScanner
{
    private static readonly Regex ArpLineRegexWindows = new(
        @"^\s*(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F\-]{11,17})\s+(\w+)",
        RegexOptions.Compiled);
    private static readonly Regex ArpLineRegexMac = new(
        @"\((\d{1,3}(?:\.\d{1,3}){3})\)\s+at\s+([0-9a-fA-F:]{11,17})",
        RegexOptions.Compiled);

    private sealed class DeviceInfo
    {
        public string Ip { get; init; } = "";
        public string? Hostname { get; set; }
        public string? NetBiosName { get; set; }
        public string? VendorHint { get; set; }
        public string? Mac { get; set; }
        public bool SmbOpen { get; set; }
        public bool LikelyNas { get; set; }
        public bool IsSynology { get; set; }
        public string DisplayName =>
            FirstNonEmpty(NetBiosName, Hostname, VendorHint) ?? $"장치 {Ip}";
    }

    public static async Task<object[]> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var found = new ConcurrentDictionary<string, DeviceInfo>(StringComparer.Ordinal);

        foreach (var (ip, mac) in ReadArpTable())
        {
            found.AddOrUpdate(
                ip,
                _ => new DeviceInfo { Ip = ip, Mac = mac, VendorHint = GuessVendorHint(mac) },
                (_, existing) =>
                {
                    existing.Mac ??= mac;
                    existing.VendorHint ??= GuessVendorHint(mac);
                    return existing;
                });
        }

        var selfIps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (networkBase, self) in GetLocalSlash24Ranges())
        {
            selfIps.Add(self.ToString());
            await ProbeSlash24Async(networkBase, self, found, cancellationToken);
        }

        foreach (var selfIp in selfIps)
            found.TryRemove(selfIp, out _);

        using var resolveGate = new SemaphoreSlim(12);
        await Task.WhenAll(found.Values.Select(async device =>
        {
            await resolveGate.WaitAsync(cancellationToken);
            try
            {
                await EnrichDeviceNamesAsync(device, cancellationToken);
                device.IsSynology = IsSynologyDevice(device);
                device.LikelyNas = device.IsSynology || IsLikelyNas(device);
                if (device.IsSynology &&
                    (string.IsNullOrWhiteSpace(device.VendorHint) ||
                     device.VendorHint.Contains("Synology", StringComparison.OrdinalIgnoreCase)))
                {
                    device.VendorHint = "시놀로지 NAS";
                }
            }
            finally
            {
                resolveGate.Release();
            }
        }));

        return found.Values
            .OrderByDescending(item => item.IsSynology)
            .ThenByDescending(item => item.LikelyNas)
            .ThenByDescending(item => item.SmbOpen)
            .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.NetBiosName) || !string.IsNullOrWhiteSpace(item.Hostname))
            .ThenBy(item => ParseIp(item.Ip))
            .Select(item => (object)new
            {
                ip = item.Ip,
                hostname = item.Hostname,
                netbiosName = item.NetBiosName,
                displayName = item.DisplayName,
                vendorHint = item.VendorHint,
                mac = item.Mac,
                smbOpen = item.SmbOpen,
                likelyNas = item.LikelyNas,
                isSynology = item.IsSynology,
            })
            .ToArray();
    }

    private static IEnumerable<(string Ip, string Mac)> ReadArpTable()
    {
        string output;
        try
        {
            output = RunProcess("arp", "-a");
        }
        catch
        {
            yield break;
        }

        foreach (var line in output.Split('\n'))
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                var macMatch = ArpLineRegexMac.Match(line);
                if (!macMatch.Success) continue;
                var ip = macMatch.Groups[1].Value;
                var mac = macMatch.Groups[2].Value.Replace('-', ':').ToLowerInvariant();
                if (mac is "00:00:00:00:00:00" or "ff:ff:ff:ff:ff:ff" or "(incomplete)") continue;
                if (mac.Contains("incomplete", StringComparison.OrdinalIgnoreCase)) continue;
                yield return (ip, mac);
                continue;
            }

            var match = ArpLineRegexWindows.Match(line);
            if (!match.Success) continue;
            var winIp = match.Groups[1].Value;
            var winMac = match.Groups[2].Value.Replace('-', ':').ToLowerInvariant();
            var type = match.Groups[3].Value;
            if (type.Contains("invalid", StringComparison.OrdinalIgnoreCase)) continue;
            if (winMac is "00:00:00:00:00:00" or "ff:ff:ff:ff:ff:ff") continue;
            yield return (winIp, winMac);
        }
    }

    private static IEnumerable<(IPAddress NetworkBase, IPAddress Self)> GetLocalSlash24Ranges()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(address.Address)) continue;
                var bytes = address.Address.GetAddressBytes();
                if (bytes.Length != 4) continue;
                bytes[3] = 0;
                var networkBase = new IPAddress(bytes);
                var key = networkBase.ToString();
                if (!seen.Add(key)) continue;
                yield return (networkBase, address.Address);
            }
        }
    }

    private static async Task ProbeSlash24Async(
        IPAddress networkBase,
        IPAddress self,
        ConcurrentDictionary<string, DeviceInfo> found,
        CancellationToken cancellationToken)
    {
        var baseBytes = networkBase.GetAddressBytes();
        using var gate = new SemaphoreSlim(40);
        var tasks = new List<Task>();

        for (var host = 1; host <= 254; host += 1)
        {
            var bytes = (byte[])baseBytes.Clone();
            bytes[3] = (byte)host;
            var ip = new IPAddress(bytes);
            if (ip.Equals(self)) continue;
            var ipText = ip.ToString();

            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var open = await IsPortOpenAsync(ip, 445, 220, cancellationToken);
                    if (!open && !found.ContainsKey(ipText)) return;
                    found.AddOrUpdate(
                        ipText,
                        _ => new DeviceInfo { Ip = ipText, SmbOpen = open },
                        (_, existing) =>
                        {
                            if (open) existing.SmbOpen = true;
                            return existing;
                        });
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task EnrichDeviceNamesAsync(DeviceInfo device, CancellationToken cancellationToken)
    {
        device.VendorHint ??= GuessVendorHint(device.Mac);

        var dnsTask = TryResolveDnsNameAsync(device.Ip, cancellationToken);
        var netbiosTask = Task.Run(() => TryResolveNetBiosName(device.Ip), cancellationToken);
        var pingTask = Task.Run(() => TryResolvePingName(device.Ip), cancellationToken);
        await Task.WhenAll(dnsTask, netbiosTask, pingTask);

        device.Hostname = CleanHostLabel(await dnsTask) ?? CleanHostLabel(await pingTask);
        device.NetBiosName = CleanHostLabel(await netbiosTask);

        // DNS가 IP만 돌려주는 경우 ping/NetBIOS로 보강
        if (string.Equals(device.Hostname, device.Ip, StringComparison.OrdinalIgnoreCase))
            device.Hostname = null;
        if (device.Hostname is null)
            device.Hostname = CleanHostLabel(await pingTask);
    }

    private static async Task<bool> IsPortOpenAsync(
        IPAddress ip,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await client.ConnectAsync(ip, port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> TryResolveDnsNameAsync(string ip, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(900);
            var entry = await Dns.GetHostEntryAsync(ip, timeout.Token);
            var name = entry.HostName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (string.Equals(name, ip, StringComparison.OrdinalIgnoreCase)) return null;
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveNetBiosName(string ip)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var output = RunProcess("nbtstat", $"-A {ip}");
            if (string.IsNullOrWhiteSpace(output)) return null;

            string? fileServer = null;
            string? workstation = null;
            foreach (Match match in Regex.Matches(
                         output,
                         @"^\s*([^\s<][^\r\n]*?)\s+<([0-9A-Fa-f]{2})>\s+(\w+)",
                         RegexOptions.Multiline))
            {
                var name = match.Groups[1].Value.Trim();
                var type = match.Groups[2].Value.ToUpperInvariant();
                var kind = match.Groups[3].Value;
                if (kind.Contains("GROUP", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("MSBROWSE", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("__", StringComparison.Ordinal)) continue;
                if (type == "20") fileServer ??= name;
                else if (type == "00") workstation ??= name;
            }

            return CleanHostLabel(fileServer ?? workstation);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolvePingName(string ip)
    {
        try
        {
            var output = OperatingSystem.IsWindows()
                ? RunProcess("ping", $"-a -n 1 -w 400 {ip}")
                : RunProcess("ping", $"-c 1 -W 1000 {ip}");
            if (string.IsNullOrWhiteSpace(output)) return null;

            var match = Regex.Match(
                output,
                @"Pinging\s+([^\s\[\]]+)\s*\[",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(
                    output,
                    @"PING\s+([^\s\(\)]+)\s*\(",
                    RegexOptions.IgnoreCase);
            }
            if (!match.Success)
            {
                match = Regex.Match(
                    output,
                    @"([^\s\[\]]+)\s*\[(?:\d{1,3}\.){3}\d{1,3}\]",
                    RegexOptions.IgnoreCase);
            }

            if (!match.Success) return null;
            var name = match.Groups[1].Value.Trim();
            if (string.Equals(name, ip, StringComparison.OrdinalIgnoreCase)) return null;
            if (name.Contains("Ping", StringComparison.OrdinalIgnoreCase)) return null;
            return CleanHostLabel(name);
        }
        catch
        {
            return null;
        }
    }

    private static string? CleanHostLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = value.Trim().Trim('.');
        if (name.Contains('.'))
            name = name.Split('.')[0];
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (IPAddress.TryParse(name, out _)) return null;
        if (name.Length > 48) name = name[..48];
        return name;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private static string? GuessVendorHint(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var compact = mac.Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToUpperInvariant();
        if (compact.Length < 6) return null;
        var oui = compact[..6];

        // 흔한 NAS/공유기 OUI (완전하지 않아도 이름 없는 장치 구분에 도움)
        string[] synology =
        [
            "001132", "0011D8", "001BFC", "001D60", "0022A1", "0024E4", "002686",
            "0029F0", "00A098", "00A099", "641666", "0008A1", "0014B1", "001F33",
            "00211E", "0022D1", "0024BE", "0025B4", "002688", "00A0C5", "0019CB",
        ];
        string[] qnap =
        [
            "245EBE", "00E04C", "24A43C", "E0224C", "000A27", "001B11", "0026B9",
        ];
        string[] netgear = ["0024A5", "0025D4"];
        string[] asus = ["000C42", "001B21", "001E67", "0024E8"];
        string[] wd = ["0018E7", "001B63", "001E8C", "002185"];
        string[] vmware = ["000C29", "005056"];

        if (synology.Contains(oui)) return "시놀로지 NAS";
        if (qnap.Contains(oui)) return "QNAP 계열";
        if (netgear.Contains(oui)) return "Netgear 계열";
        if (asus.Contains(oui)) return "ASUS 계열";
        if (wd.Contains(oui)) return "Western Digital";
        if (vmware.Contains(oui)) return "가상 머신";
        return null;
    }

    private static bool IsSynologyDevice(DeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.VendorHint) &&
            (device.VendorHint.Contains("Synology", StringComparison.OrdinalIgnoreCase)
             || device.VendorHint.Contains("시놀로지", StringComparison.OrdinalIgnoreCase)))
            return true;

        var name = $"{device.NetBiosName} {device.Hostname}";
        return name.Contains("synology", StringComparison.OrdinalIgnoreCase)
            || name.Contains("diskstation", StringComparison.OrdinalIgnoreCase)
            || name.Contains("시놀로지", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyNas(DeviceInfo device)
    {
        if (device.IsSynology) return true;
        if (device.SmbOpen) return true;
        if (!string.IsNullOrWhiteSpace(device.VendorHint) &&
            (device.VendorHint.Contains("시놀로지", StringComparison.OrdinalIgnoreCase)
             || device.VendorHint.Contains("Synology", StringComparison.OrdinalIgnoreCase)
             || device.VendorHint.Contains("QNAP", StringComparison.OrdinalIgnoreCase)
             || device.VendorHint.Contains("Western Digital", StringComparison.OrdinalIgnoreCase)))
            return true;
        var name = $"{device.NetBiosName} {device.Hostname} {device.VendorHint}";
        return name.Contains("nas", StringComparison.OrdinalIgnoreCase)
            || name.Contains("synology", StringComparison.OrdinalIgnoreCase)
            || name.Contains("diskstation", StringComparison.OrdinalIgnoreCase)
            || name.Contains("qnap", StringComparison.OrdinalIgnoreCase)
            || name.Contains("truenas", StringComparison.OrdinalIgnoreCase)
            || name.Contains("storage", StringComparison.OrdinalIgnoreCase);
    }

    private static long ParseIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return long.MaxValue;
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return long.MaxValue;
        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
    }

    private static string RunProcess(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }
}
