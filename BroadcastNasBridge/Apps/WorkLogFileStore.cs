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
