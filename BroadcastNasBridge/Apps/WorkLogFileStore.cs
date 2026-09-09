using System.Text;
using System.Text.Json;
using BroadcastNasBridge.Services;

namespace BroadcastNasBridge.Apps;

public sealed class WorkLogFileStore
{
    private string? _root;
    private string? _scheduleRoot;
    private readonly object _fileGate = new();

    public bool IsConnected => _root is not null;
    public string? Root => _root;
    public string? ScheduleRoot => _scheduleRoot;

    public void Bind(string workLogRoot, string? scheduleRoot)
    {
        if (!Directory.Exists(workLogRoot))
            throw new DirectoryNotFoundException(workLogRoot);
        _root = workLogRoot;
        _scheduleRoot = scheduleRoot;
    }

    public void Clear()
    {
        _root = null;
        _scheduleRoot = null;
    }

    public (string Content, string Revision) ReadTextWithRevision(string relativePath)
    {
        var path = ResolvePath(relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException(relativePath);
        lock (_fileGate)
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

        lock (_fileGate)
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
        var path = ResolvePath($"audit/{month}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var trimmed = lineOrJson.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("감사 로그 내용이 비어 있습니다.");
        using (JsonDocument.Parse(trimmed)) { }
        lock (_fileGate)
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
        lock (_fileGate)
            return File.ReadAllText(path, Encoding.UTF8);
    }

    private const int EditLockTtlSeconds = 45;

    public EditLockState? GetEditLock(string date)
    {
        EnsureConnected();
        ValidateDate(date);
        lock (_fileGate)
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
        Directory.CreateDirectory(Path.Combine(_root!, "locks"));

        lock (_fileGate)
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
        lock (_fileGate)
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
        lock (_fileGate)
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
        if (string.IsNullOrEmpty(_scheduleRoot) || !Directory.Exists(_scheduleRoot))
            return [];

        return Directory.EnumerateFiles(_scheduleRoot, "*.json", SearchOption.TopDirectoryOnly)
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
        if (string.IsNullOrEmpty(_scheduleRoot))
            throw new InvalidOperationException("스케줄 폴더를 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name ||
            !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 스케줄 파일명입니다.");

        var path = Path.Combine(_scheduleRoot, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"스케줄 파일이 없습니다: {name}");
        lock (_fileGate)
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

        var full = Path.GetFullPath(Path.Combine(_root!, normalized));
        var rootFull = Path.GetFullPath(_root!);
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
        if (_root is null) throw new InvalidOperationException("먼저 NAS 연결을 확인하세요.");
    }

    public sealed class WriteResult
    {
        public bool Saved { get; init; }
        public string Revision { get; init; } = "";
        public string? Content { get; init; }
    }
}

public sealed record EditLockRequest(string? Date, string? SessionId, string? ActorId, string? ActorName);

public sealed record EditLockState(
    string Date,
    string SessionId,
    string ActorId,
    string ActorName,
    string AcquiredAt,
    string HeartbeatAt);
