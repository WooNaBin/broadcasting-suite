using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using ScheduleReader;
using ScheduleReader.Excel;
using ScheduleReader.Models;
using ScheduleReader.Services;

const string MutexName = "ScheduleReader.LocalHost";
const int PreferredPort = 17823;
const string Host = "127.0.0.1";

if (TryRunExtractCli(args))
    return;

using var mutex = new Mutex(true, MutexName, out var createdNew);
if (!createdNew)
{
    var url = $"http://{Host}:{PreferredPort}/";
    OpenBrowser(url);
    Console.WriteLine("ScheduleReader가 이미 실행 중입니다. 브라우저를 다시 엽니다.");
    return;
}

var port = PickPort(PreferredPort);
var listenUrl = $"http://{Host}:{port}";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppPaths.AppDirectory,
});
builder.WebHost.UseUrls(listenUrl);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton<SessionStore>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 128 * 1024 * 1024; // 128MB
    o.ValueLengthLimit = 128 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 128 * 1024 * 1024;
});

var app = builder.Build();
var webRoot = CreateWebRootFileProvider();
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webRoot });
app.UseStaticFiles(new StaticFileOptions { FileProvider = webRoot });

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
};

app.MapGet("/api/health", () => Results.Json(new
{
    ok = true,
    app = "ScheduleReader",
    mode = "excel",
}));

app.MapPost("/api/workbook", async (HttpRequest request, SessionStore sessions) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { detail = "multipart 폼이 필요합니다." });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { detail = "빈 파일입니다." });

    await using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    try
    {
        var session = sessions.Create(file.FileName, ms.ToArray());
        // Preview only — keep caps small so multi-sheet workbooks respond quickly
        var sheet = WorkbookReader.ReadSheet(session.Path, session.ActiveSheet, maxRows: 40, maxCols: 20);
        return Results.Json(new
        {
            sessionId = session.Id,
            filename = session.Filename,
            sheets = session.SheetNames,
            activeSheet = session.ActiveSheet,
            preview = WorkbookReader.Preview(sheet, maxRows: 30, maxCols: 20),
            extractReady = false,
        }, jsonOpts);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { detail = $"엑셀을 열 수 없습니다: {ex.Message}" });
    }
});

app.MapPost("/api/workbook/sheet", async (HttpRequest request, SessionStore sessions) =>
{
    var form = await request.ReadFormAsync();
    var sessionId = form["sessionId"].ToString();
    var sheetName = form["sheet"].ToString();
    var session = sessions.Get(sessionId);
    if (session is null)
        return Results.NotFound(new { detail = "불러온 엑셀 세션이 없습니다. 다시 불러오세요." });
    if (!session.SheetNames.Contains(sheetName))
        return Results.BadRequest(new { detail = "없는 시트입니다." });

    session.ActiveSheet = sheetName;
    session.ExtractOk = false;
    session.ExtractItems = [];
    session.ExtractEvents = [];
    session.ExtractWarnings = [];
    var sheet = WorkbookReader.ReadSheet(session.Path, sheetName, maxRows: 30, maxCols: 20);
    return Results.Json(new
    {
        sessionId = session.Id,
        activeSheet = sheetName,
        preview = WorkbookReader.Preview(sheet),
        extractReady = false,
    }, jsonOpts);
});

app.MapPost("/api/extract", async (HttpRequest request, SessionStore sessions) =>
{
    var form = await request.ReadFormAsync();
    var sessionId = form["sessionId"].ToString();
    var sheetName = form["sheet"].ToString();
    var session = sessions.Get(sessionId);
    if (session is null)
        return Results.NotFound(new { detail = "불러온 엑셀 세션이 없습니다. 다시 불러오세요." });

    var target = string.IsNullOrWhiteSpace(sheetName) ? session.ActiveSheet : sheetName;
    if (!session.SheetNames.Contains(target))
        return Results.BadRequest(new { detail = "없는 시트입니다." });

    var excludeHolidays = IsTruthy(form["excludeHolidays"].ToString());

    session.ActiveSheet = target;
    var result = ScheduleExtractor.Extract(session.Path, target);
    var items = result.Items;
    var holidayExcluded = 0;
    if (excludeHolidays)
    {
        var before = items.Count;
        items = ScheduleReader.Parser.KoreanHolidays.ExcludeHolidaySchedules(items);
        holidayExcluded = before - items.Count;
    }

    // UI·편집용: 이어진 같은 일정은 기간 한 건으로 합침 (내보내기 때 다시 일별로 펼침)
    var collapsed = ScheduleReader.Parser.Normalizer.CollapseConsecutive(items);
    session.ExtractOk = result.Ok;
    session.ExtractEvents = result.Events;
    session.ExtractItems = collapsed;
    session.ExtractWarnings = result.Warnings;
    session.ExtractMeta = result.Meta;
    session.ExtractMeta["rawItemCount"] = items.Count;
    session.ExtractMeta["collapsedItemCount"] = collapsed.Count;
    session.ExtractMeta["excludeHolidays"] = excludeHolidays;
    session.ExtractMeta["holidayExcludedCount"] = holidayExcluded;

    return Results.Json(new
    {
        ok = session.ExtractOk,
        sessionId = session.Id,
        sheet = target,
        eventCount = session.ExtractEvents.Count,
        itemCount = session.ExtractItems.Count,
        rawItemCount = items.Count,
        holidayExcludedCount = holidayExcluded,
        warnings = session.ExtractWarnings,
        meta = session.ExtractMeta,
        events = session.ExtractEvents.Take(200),
        items = session.ExtractItems.Take(200),
        shapes = result.Shapes.Take(200),
        cells = result.Cells.Take(500),
        extractReady = session.ExtractOk && session.ExtractItems.Count > 0,
    }, jsonOpts);
});

app.MapGet("/api/export", (string sessionId, SessionStore sessions) =>
{
    var session = sessions.Get(sessionId);
    if (session is null)
        return Results.NotFound(new { detail = "불러온 엑셀 세션이 없습니다." });
    if (!session.ExtractOk || session.ExtractItems.Count == 0)
        return Results.BadRequest(new { detail = "일정 추출이 아직 성공하지 않았습니다." });

    var expanded = ScheduleReader.Parser.Normalizer.ExpandDateRanges(session.ExtractItems);
    var body = SerializeScheduleData(expanded);

    return Results.File(
        Encoding.UTF8.GetBytes(body),
        "application/json; charset=utf-8",
        fileDownloadName: "schedule-data.json");
});

app.MapPost("/api/export", async (HttpRequest request, SessionStore sessions) =>
{
    ExportRequest? payload;
    try
    {
        payload = await JsonSerializer.DeserializeAsync<ExportRequest>(request.Body, jsonOpts);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { detail = $"일정 JSON을 읽을 수 없습니다: {ex.Message}" });
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
        return Results.BadRequest(new { detail = "sessionId가 필요합니다." });

    var session = sessions.Get(payload.SessionId);
    if (session is null)
        return Results.NotFound(new { detail = "불러온 엑셀 세션이 없습니다." });

    var edited = payload.Items ?? [];
    if (edited.Count == 0)
        return Results.BadRequest(new { detail = "내보낼 일정이 없습니다. 추출 결과를 확인해 주세요." });

    session.ExtractItems = edited;
    session.ExtractOk = true;
    var expanded = ScheduleReader.Parser.Normalizer.ExpandDateRanges(edited);
    if (expanded.Count == 0)
        return Results.BadRequest(new { detail = "유효한 날짜·제목이 있는 일정이 없습니다." });

    var body = SerializeScheduleData(expanded);
    return Results.File(
        Encoding.UTF8.GetBytes(body),
        "application/json; charset=utf-8",
        fileDownloadName: "schedule-data.json");
});

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"ScheduleReader UI → {listenUrl}/");
    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        OpenBrowser($"{listenUrl}/");
    });
});

app.Run();

static bool TryRunExtractCli(string[] args)
{
    var extractIdx = Array.FindIndex(args, a => a is "--extract" or "-e");
    if (extractIdx < 0)
        return false;

    if (extractIdx + 1 >= args.Length)
    {
        Console.Error.WriteLine(
            "사용법: ScheduleReader --extract <엑셀.xlsx> [--sheet 시트명] [--out schedule-data.json]");
        Environment.ExitCode = 1;
        return true;
    }

    var xlsx = args[extractIdx + 1].Trim().Trim('"');
    if (!File.Exists(xlsx))
    {
        Console.Error.WriteLine($"엑셀 파일이 없습니다: {xlsx}");
        Environment.ExitCode = 1;
        return true;
    }

    var sheetOpt = ArgValue(args, "--sheet", "-s");
    var outOpt = ArgValue(args, "--out", "-o");
    var outPath = string.IsNullOrWhiteSpace(outOpt)
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(xlsx)) ?? ".", "schedule-data.json")
        : outOpt.Trim().Trim('"');

    string? tempCopy = null;
    try
    {
        var readable = TryOpenCopy(xlsx, out tempCopy);
        var sheets = WorkbookReader.ListSheetNames(readable);
        var targets = string.IsNullOrWhiteSpace(sheetOpt)
            ? sheets
            : sheets.Where(n => string.Equals(n, sheetOpt, StringComparison.Ordinal)).ToList();
        if (targets.Count == 0)
        {
            Console.Error.WriteLine(
                $"시트「{sheetOpt}」가 없습니다. 있는 시트: {string.Join(", ", sheets)}");
            Environment.ExitCode = 1;
            return true;
        }

        var items = new List<ScheduleReader.Models.ScheduleItem>();
        var warnings = new List<string>();
        foreach (var sheet in targets)
        {
            var result = ScheduleExtractor.Extract(readable, sheet);
            warnings.AddRange(result.Warnings.Select(w => $"[{sheet}] {w}"));
            items.AddRange(result.Items);
            Console.WriteLine($"{sheet}: 일정 {result.Events.Count}건 → JSON {result.Items.Count}줄");
        }

        items = items
            .OrderBy(i => i.ScheduleDate)
            .ThenBy(i => i.StartTime)
            .ThenBy(i => i.ScheduleTitle)
            .ToList();

        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, SerializeScheduleData(items), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"저장: {Path.GetFullPath(outPath)} ({items.Count}줄)");
        foreach (var w in warnings)
            Console.WriteLine("경고: " + w);
        Environment.ExitCode = items.Count == 0 ? 2 : 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"추출 실패: {ex.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        if (tempCopy is not null)
        {
            try { File.Delete(tempCopy); } catch { /* temp */ }
        }
    }

    return true;
}

static string TryOpenCopy(string path, out string? tempCopy)
{
    tempCopy = Path.Combine(
        Path.GetTempPath(),
        "ScheduleReader",
        Guid.NewGuid().ToString("n") + Path.GetExtension(path));
    Directory.CreateDirectory(Path.GetDirectoryName(tempCopy)!);
    using var src = new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var dst = new FileStream(tempCopy, FileMode.Create, FileAccess.Write, FileShare.None);
    src.CopyTo(dst);
    return tempCopy;
}

static string? ArgValue(string[] args, params string[] names)
{
    var i = Array.FindIndex(args, a => names.Contains(a, StringComparer.OrdinalIgnoreCase));
    if (i < 0 || i + 1 >= args.Length) return null;
    var next = args[i + 1];
    if (next.StartsWith('-')) return null;
    return next;
}

static bool IsTruthy(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;
    return value.Trim() is "1" or "true" or "True" or "TRUE" or "on" or "yes" or "Y" or "y";
}

static string SerializeScheduleData(IReadOnlyList<ScheduleReader.Models.ScheduleItem> items) =>
    JsonSerializer.Serialize(items, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }) + "\n";

static int PickPort(int preferred)
{
    if (CanBind(preferred)) return preferred;
    if (IsScheduleReader(preferred)) return preferred;
    for (var p = preferred + 1; p < preferred + 20; p++)
    {
        if (CanBind(p)) return p;
    }

    throw new InvalidOperationException(
        $"{preferred}~{preferred + 19} 포트가 모두 사용 중입니다. 기존 서버를 종료하세요.");
}

static bool CanBind(int port)
{
    try
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch
    {
        return false;
    }
}

static bool IsScheduleReader(int port)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        var html = client.GetStringAsync($"http://{Host}:{port}/").GetAwaiter().GetResult();
        return html.Contains("ScheduleReader", StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static IFileProvider CreateWebRootFileProvider()
{
    var physical = Path.Combine(AppPaths.AppDirectory, "wwwroot");
    if (Directory.Exists(physical))
        return new PhysicalFileProvider(physical);

    var project = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    if (Directory.Exists(project))
        return new PhysicalFileProvider(project);

    return new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
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
                UseShellExecute = false,
            });
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { url },
                UseShellExecute = false,
            });
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"브라우저 실행 실패: {ex.Message}");
        Console.WriteLine($"수동으로 열어 주세요: {url}");
    }
}
