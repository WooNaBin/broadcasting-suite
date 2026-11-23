using ScheduleReader.Excel;
using ScheduleReader.Models;

namespace ScheduleReader.Services;

public sealed class WorkbookSession
{
    public required string Id { get; init; }
    public required string Filename { get; init; }
    public required string Path { get; init; }
    public required List<string> SheetNames { get; init; }
    public string ActiveSheet { get; set; } = "";
    public List<ScheduleItem> ExtractItems { get; set; } = [];
    public List<RawEvent> ExtractEvents { get; set; } = [];
    public bool ExtractOk { get; set; }
    public List<string> ExtractWarnings { get; set; } = [];
    public Dictionary<string, object?> ExtractMeta { get; set; } = new();
}

public sealed class SessionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, WorkbookSession> _sessions = new();
    private readonly string _tmpdir;

    public SessionStore()
    {
        _tmpdir = Path.Combine(AppPaths.TempSessionsDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpdir);
    }

    public WorkbookSession Create(string filename, byte[] content)
    {
        var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(filename) ? "schedule.xlsx" : filename);
        WorkbookReader.ValidateWorkbookBytes(safeName, content);

        var id = Guid.NewGuid().ToString("N");
        var path = System.IO.Path.Combine(_tmpdir, $"{id}_{safeName}");
        File.WriteAllBytes(path, content);

        List<string> names;
        try
        {
            names = WorkbookReader.ListSheetNames(path).ToList();
        }
        catch (Exception ex)
        {
            try { File.Delete(path); } catch { /* ignore */ }
            throw new ArgumentException($"엑셀을 열 수 없습니다: {ex.Message}", ex);
        }

        if (names.Count == 0)
        {
            try { File.Delete(path); } catch { /* ignore */ }
            throw new ArgumentException(
                "읽을 수 있는 워크시트가 없습니다. (차트 시트만 있거나 시트가 숨겨져 있을 수 있습니다.)");
        }

        var session = new WorkbookSession
        {
            Id = id,
            Filename = safeName,
            Path = path,
            SheetNames = names,
            ActiveSheet = names[0],
        };

        lock (_lock) _sessions[id] = session;
        return session;
    }

    public WorkbookSession? Get(string sessionId)
    {
        lock (_lock) return _sessions.GetValueOrDefault(sessionId);
    }
}
