using System.Diagnostics;
using System.Text.Json;
using BroadcastNasBridge.Services;

namespace BroadcastNasBridge.Apps;

public sealed record FilesSettings(
    string LocalPath,
    string NasIp,
    string NasUsername,
    string NasPassword,
    string NasSearchPath,
    string? ScheduleJsonPath = null);

public sealed record FilesScheduleInput(
    DateOnly Date,
    string Description,
    string? Memo,
    bool HasStartTime,
    string? StartTime,
    bool HasInstructor,
    string? Instructor,
    bool SpecialSong,
    bool YouTube);

public sealed record FilesStoredSchedule(Guid Id, FilesScheduleInput Input);

public sealed class FilesAppStore
{
    private readonly string _schedulesFile;
    private readonly string _settingsFile;
    private readonly NasService _nas;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FilesAppStore(IWebHostEnvironment environment, NasService nas)
    {
        _nas = nas;
        var dataDirectory = Path.Combine(environment.ContentRootPath, "data", "files");
        Directory.CreateDirectory(dataDirectory);
        _schedulesFile = Path.Combine(dataDirectory, "schedules.json");
        _settingsFile = Path.Combine(dataDirectory, "settings.json");
    }

    public async Task<IReadOnlyList<object>> GetSchedulesAsync(bool forceScan, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var schedules = await ReadAsync<List<FilesStoredSchedule>>(_schedulesFile, ct) ?? [];
            var settings = await EffectiveSettingsAsync(ct);
            var dates = schedules.Select(s => s.Input.Date).ToHashSet();
            var local = IndexFiles(settings.LocalPath, forceScan);
            var nas = IndexFiles(_nas.MediaRoot ?? "", forceScan);
            return schedules
                .OrderByDescending(s => s.Input.Date)
                .Select(s => ToDto(s, local, nas))
                .Cast<object>()
                .ToList();
        }
        finally { _lock.Release(); }
    }

    public async Task<object> AddScheduleAsync(FilesScheduleInput input, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var schedules = await ReadAsync<List<FilesStoredSchedule>>(_schedulesFile, ct) ?? [];
            var item = new FilesStoredSchedule(Guid.NewGuid(), input);
            schedules.Add(item);
            await WriteAsync(_schedulesFile, schedules, ct);
            var settings = await EffectiveSettingsAsync(ct);
            return ToDto(item, IndexFiles(settings.LocalPath, false), IndexFiles(_nas.MediaRoot ?? "", false));
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var schedules = await ReadAsync<List<FilesStoredSchedule>>(_schedulesFile, ct) ?? [];
            var removed = schedules.RemoveAll(s => s.Id == id);
            if (removed == 0) return false;
            await WriteAsync(_schedulesFile, schedules, ct);
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var schedules = await ReadAsync<List<FilesStoredSchedule>>(_schedulesFile, ct) ?? [];
            return JsonSerializer.SerializeToUtf8Bytes(schedules, _jsonOptions);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> ImportAsync(Stream stream, CancellationToken ct)
    {
        var schedules = await JsonSerializer.DeserializeAsync<List<FilesStoredSchedule>>(stream, _jsonOptions, ct)
            ?? throw new JsonException("스케줄 데이터가 없습니다.");
        await _lock.WaitAsync(ct);
        try
        {
            await WriteAsync(_schedulesFile, schedules, ct);
            return schedules.Count;
        }
        finally { _lock.Release(); }
    }

    public async Task<FilesSettings> GetSettingsAsync(CancellationToken ct) =>
        await EffectiveSettingsAsync(ct);

    public async Task<FilesSettings> SaveSettingsAsync(FilesSettings settings, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await WriteAsync(_settingsFile, settings, ct);
            return settings;
        }
        finally { _lock.Release(); }
    }

    public Task<object> TestNasAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            _nas.EnsureConnected();
            var path = _nas.MediaRoot ?? "";
            var ok = !string.IsNullOrEmpty(path) && Directory.Exists(path);
            return Task.FromResult<object>(new
            {
                success = ok,
                message = ok
                    ? "브리지 NAS 미디어 경로에 접근할 수 있습니다."
                    : "미디어 경로가 없습니다. 설정 마법사에서 Permanent 공유를 확인하세요.",
                path,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new { success = false, message = ex.Message, path = "" });
        }
    }

    public async Task<object> SyncFromScheduleAsync(CancellationToken ct)
    {
        _nas.EnsureConnected();
        var scheduleRoot = _nas.ScheduleRoot
            ?? throw new InvalidOperationException("스케줄 루트가 없습니다.");
        var cfg = _nas.Config;
        var fileName = string.IsNullOrWhiteSpace(cfg.ScheduleJsonFile)
            ? Directory.EnumerateFiles(scheduleRoot, "*.json")
                .Select(Path.GetFileName)
                .Where(n => n is not null && !n.StartsWith('_'))
                .Cast<string>()
                .OrderByDescending(n => n)
                .FirstOrDefault()
            : cfg.ScheduleJsonFile;
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("가져올 스케줄 JSON이 없습니다.");

        var path = Path.Combine(scheduleRoot, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(fileName);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var imported = new List<FilesStoredSchedule>();
        if (doc.RootElement.TryGetProperty("schedules", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (!TryMapSchedule(item, out var input)) continue;
                imported.Add(new FilesStoredSchedule(Guid.NewGuid(), input));
            }
        }

        await _lock.WaitAsync(ct);
        try
        {
            await WriteAsync(_schedulesFile, imported, ct);
            return new { count = imported.Count, message = $"Recording 일정 {imported.Count}개 가져옴 ({fileName})" };
        }
        finally { _lock.Release(); }
    }

    private async Task<FilesSettings> EffectiveSettingsAsync(CancellationToken ct)
    {
        var local = await ReadAsync<FilesSettings>(_settingsFile, ct);
        var cfg = _nas.Config;
        return new FilesSettings(
            LocalPath: local?.LocalPath ?? cfg.LocalPath ?? "",
            NasIp: cfg.Host,
            NasUsername: cfg.Username,
            NasPassword: cfg.RememberPassword ? cfg.Password : "",
            NasSearchPath: $"{cfg.PermanentShare}\\{cfg.MediaRelativePath.Replace('/', '\\')}",
            ScheduleJsonPath: cfg.ScheduleJsonFile);
    }

    private static bool TryMapSchedule(JsonElement item, out FilesScheduleInput input)
    {
        input = default!;
        try
        {
            var dateStr = item.TryGetProperty("date", out var d) ? d.GetString()
                : item.TryGetProperty("Date", out var d2) ? d2.GetString() : null;
            if (string.IsNullOrWhiteSpace(dateStr) || !DateOnly.TryParse(dateStr, out var date))
                return false;
            var title = item.TryGetProperty("title", out var t) ? t.GetString()
                : item.TryGetProperty("description", out var desc) ? desc.GetString() : "";
            if (string.IsNullOrWhiteSpace(title)) return false;
            input = new FilesScheduleInput(date, title!, null, false, null, false, null, false, false);
            return true;
        }
        catch { return false; }
    }

    private static List<string> IndexFiles(string root, bool _)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return [];
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(20000).ToList();
        }
        catch { return []; }
    }

    private static object ToDto(FilesStoredSchedule item, List<string> localFiles, List<string> nasFiles)
    {
        var identity = string.Join(' ', item.Input.Date.ToString("yyyyMMdd"), item.Input.Description, item.Input.Instructor);
        string? Find(List<string> files, string kind)
        {
            return files.FirstOrDefault(f =>
            {
                var name = Path.GetFileName(f);
                if (!name.Contains(item.Input.Date.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!name.Contains(item.Input.Description.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains(item.Input.Description, StringComparison.OrdinalIgnoreCase))
                {
                    // 느슨: 날짜만 있으면 후보
                }
                return kind switch
                {
                    "audio" => name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase),
                    "video" => name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase),
                    "hq" => name.Contains("HQ", StringComparison.OrdinalIgnoreCase) || name.Contains("고화질", StringComparison.OrdinalIgnoreCase),
                    _ => false,
                };
            });
        }

        var localAudio = Find(localFiles, "audio");
        var localVideo = Find(localFiles, "video");
        var localHq = Find(localFiles, "hq");
        var nasAudio = Find(nasFiles, "audio");
        var nasVideo = Find(nasFiles, "video");
        var nasHq = Find(nasFiles, "hq");
        var baseName = $"{item.Input.Date:yyyyMMdd}_{item.Input.Description}";

        return new
        {
            id = item.Id,
            date = item.Input.Date,
            description = item.Input.Description,
            memo = item.Input.Memo,
            hasStartTime = item.Input.HasStartTime,
            startTime = item.Input.StartTime,
            hasInstructor = item.Input.HasInstructor,
            instructor = item.Input.Instructor,
            specialSong = item.Input.SpecialSong,
            youTube = item.Input.YouTube,
            localAudioFound = localAudio is not null,
            localVideoFound = localVideo is not null,
            localHighQualityFound = localHq is not null,
            nasAudioFound = nasAudio is not null,
            nasVideoFound = nasVideo is not null,
            nasHighQualityFound = nasHq is not null,
            localAudioPath = localAudio,
            localVideoPath = localVideo,
            localHighQualityPath = localHq,
            nasAudioPath = nasAudio,
            nasVideoPath = nasVideo,
            nasHighQualityPath = nasHq,
            baseFileName = baseName,
        };
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct);
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, ct);
    }

    public static void OpenLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            throw new FileNotFoundException(path);
        if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo { FileName = "open", ArgumentList = { "-R", path }, UseShellExecute = false });
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true,
            });
    }
}
