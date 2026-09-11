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
    bool YouTube,
    string? Place = null);

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
        // 브리지: 자체 schedules.json이 아니라 SDM 공유 JSON의 Recording만 작업 목록
        List<FilesStoredSchedule> schedules;
        if (_nas.IsConnected)
        {
            schedules = await LoadRecordingFromSdmAsync(ct);
            await _lock.WaitAsync(ct);
            try { await WriteAsync(_schedulesFile, schedules, ct); }
            finally { _lock.Release(); }
        }
        else
        {
            await _lock.WaitAsync(ct);
            try { schedules = await ReadAsync<List<FilesStoredSchedule>>(_schedulesFile, ct) ?? []; }
            finally { _lock.Release(); }
        }

        var settings = await EffectiveSettingsAsync(ct);
        var local = IndexFiles(settings.LocalPath, forceScan);
        var nas = IndexFiles(_nas.MediaRoot ?? "", forceScan);
        return schedules
            .OrderByDescending(s => s.Input.Date)
            .Select(s => ToDto(s, local, nas))
            .Cast<object>()
            .ToList();
    }

    public Task<object> AddScheduleAsync(FilesScheduleInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "브리지 FileChecker는 ScheduleDataManager 공유 스케줄만 사용합니다. SDM에서 preparation에 Recording을 넣어 주세요.");
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "작업 목록은 SDM 스케줄에서 파생됩니다. 일정 삭제는 ScheduleDataManager에서 하세요.");
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var schedules = _nas.IsConnected
            ? await LoadRecordingFromSdmAsync(ct)
            : await ReadAsync<List<FilesStoredSchedule>>(_schedulesFile, ct) ?? [];
        return JsonSerializer.SerializeToUtf8Bytes(schedules, _jsonOptions);
    }

    public Task<int> ImportAsync(Stream stream, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "브리지에서는 자체 schedules.json을 불러오지 않습니다. SDM 공유 JSON을 사용합니다.");
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
        var imported = await LoadRecordingFromSdmAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            await WriteAsync(_schedulesFile, imported, ct);
            return new { count = imported.Count, message = $"Recording 일정 {imported.Count}개 동기화 (SDM 공유 JSON)" };
        }
        finally { _lock.Release(); }
    }

    /// <summary>SDM 공유 스케줄 JSON에서 preparation∋Recording 만 작업 목록으로 변환.</summary>
    private async Task<List<FilesStoredSchedule>> LoadRecordingFromSdmAsync(CancellationToken ct)
    {
        _nas.EnsureConnected();
        var scheduleRoot = _nas.ScheduleRoot
            ?? throw new InvalidOperationException("스케줄 루트가 없습니다. NAS 설정을 확인하세요.");
        var path = ResolveSdmScheduleJsonPath(scheduleRoot)
            ?? throw new InvalidOperationException(
                "SDM 스케줄 JSON을 찾을 수 없습니다. Temp DATA\\_data 아래 *.json 을 확인하세요.");

        // 해석된 파일을 브리지 설정에 남겨 SDM·FC가 동일 파일을 쓰도록 함
        if (string.IsNullOrWhiteSpace(_nas.Config.ScheduleJsonFile))
            _nas.SetActiveScheduleJsonFile(Path.GetFileName(path));

        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        JsonElement arr;
        if (root.ValueKind == JsonValueKind.Array)
            arr = root;
        else if (root.TryGetProperty("schedules", out var nested) && nested.ValueKind == JsonValueKind.Array)
            arr = nested;
        else
            throw new InvalidOperationException($"스케줄 JSON은 배열이어야 합니다: {Path.GetFileName(path)}");

        var imported = new List<FilesStoredSchedule>();
        foreach (var item in arr.EnumerateArray())
        {
            if (!HasRecordingPreparation(item)) continue;
            if (!TryMapSchedule(item, out var input)) continue;
            imported.Add(new FilesStoredSchedule(StableId(item), input));
        }
        return imported;
    }

    private string? ResolveSdmScheduleJsonPath(string scheduleRoot)
    {
        // ScheduleDataManager pickPreferredJsonFile 과 동일 규칙
        var files = Directory.EnumerateFiles(scheduleRoot, "*.json")
            .Select(Path.GetFileName)
            .Where(name => name is not null && IsSdmScheduleJsonCandidate(name))
            .Cast<string>()
            .ToList();
        if (files.Count == 0) return null;

        var cfg = _nas.Config;
        var remembered = string.IsNullOrWhiteSpace(cfg.ScheduleJsonFile)
            ? ""
            : Path.GetFileName(cfg.ScheduleJsonFile.Trim());
        if (!string.IsNullOrWhiteSpace(remembered))
        {
            var hit = files.FirstOrDefault(f =>
                f.Equals(remembered, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return Path.Combine(scheduleRoot, hit);

            // 설정이 루트 외 절대경로면 파일 존재 시 그대로 사용
            if (Path.IsPathRooted(cfg.ScheduleJsonFile!) && File.Exists(cfg.ScheduleJsonFile))
                return cfg.ScheduleJsonFile;
        }

        string[] preferred =
        [
            "schedule-data.json",
            "scheduledata.json",
            "schedule.json",
        ];
        foreach (var name in preferred)
        {
            var hit = files.FirstOrDefault(f =>
                f.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return Path.Combine(scheduleRoot, hit);
        }

        var soft = files.FirstOrDefault(f =>
            f.Contains("schedule", StringComparison.OrdinalIgnoreCase));
        if (soft is not null) return Path.Combine(scheduleRoot, soft);

        var sorted = files
            .OrderBy(f => f, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), ignoreCase: false))
            .First();
        return Path.Combine(scheduleRoot, sorted);
    }

    private static bool IsSdmScheduleJsonCandidate(string name)
    {
        if (name.StartsWith('_')) return false;
        if (name.Contains("work_log", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains("backup", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("official-documents.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("links.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("_shared_notice.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("_schedule-presets.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("schedule-presets.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("_speaker-roster.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("speaker-roster.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("_schedule-colors.json", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
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

    private static bool HasRecordingPreparation(JsonElement item)
    {
        if (item.TryGetProperty("recording", out var recFlag))
        {
            if (recFlag.ValueKind == JsonValueKind.True) return true;
            if (recFlag.ValueKind == JsonValueKind.String &&
                recFlag.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        if (!item.TryGetProperty("preparation", out var prep))
            return false;

        if (prep.ValueKind == JsonValueKind.Object)
        {
            if (prep.TryGetProperty("recording", out var nested) && nested.ValueKind == JsonValueKind.True)
                return true;
            if (prep.TryGetProperty("Recording", out var nested2) && nested2.ValueKind == JsonValueKind.True)
                return true;
        }

        if (prep.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var value in prep.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String &&
                value.GetString()?.Equals("Recording", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }
        return false;
    }

    private static Guid StableId(JsonElement item)
    {
        if (item.TryGetProperty("id", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.String && Guid.TryParse(idEl.GetString(), out var g))
                return g;
            if (idEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(idEl.GetString()))
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(idEl.GetString()!);
                return new Guid(md5.ComputeHash(bytes));
            }
        }
        return Guid.NewGuid();
    }

    private static bool TryMapSchedule(JsonElement item, out FilesScheduleInput input)
    {
        input = default!;
        try
        {
            var dateStr = GetStringProp(item, "scheduleDate")
                ?? GetStringProp(item, "releaseDate")
                ?? GetStringProp(item, "date")
                ?? GetStringProp(item, "Date");
            if (string.IsNullOrWhiteSpace(dateStr) || !DateOnly.TryParse(dateStr, out var date))
                return false;
            var title = GetStringProp(item, "scheduleTitle")
                ?? GetStringProp(item, "title")
                ?? GetStringProp(item, "description")
                ?? "";
            if (string.IsNullOrWhiteSpace(title)) return false;
            var speaker = GetStringProp(item, "sermonSpeaker") ?? GetStringProp(item, "instructor");
            var memo = GetStringProp(item, "preaparationNote")
                ?? GetStringProp(item, "preparationNote")
                ?? GetStringProp(item, "accessoryInfo")
                ?? GetStringProp(item, "memo");
            string? startTime = null;
            var hasStart = false;
            if (item.TryGetProperty("startTime", out var st))
            {
                if (st.ValueKind == JsonValueKind.Number && st.TryGetInt32(out var num) && num is >= 0 and <= 2359)
                {
                    var hour = num / 100;
                    var minute = num % 100;
                    if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
                    {
                        startTime = $"{hour:D2}:{minute:D2}";
                        hasStart = true;
                    }
                }
                else if (st.ValueKind == JsonValueKind.String)
                {
                    startTime = st.GetString();
                    hasStart = !string.IsNullOrWhiteSpace(startTime);
                }
            }
            input = new FilesScheduleInput(
                date,
                title.Trim(),
                string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
                hasStart,
                startTime,
                !string.IsNullOrWhiteSpace(speaker),
                string.IsNullOrWhiteSpace(speaker) ? null : speaker.Trim(),
                HasPreparationFlag(item, "Praise"),
                HasPreparationFlag(item, "Streaming"),
                GetStringProp(item, "place"));
            return true;
        }
        catch { return false; }
    }

    private static bool HasPreparationFlag(JsonElement item, string flag)
    {
        if (!item.TryGetProperty("preparation", out var prep) || prep.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var value in prep.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String &&
                value.GetString()?.Equals(flag, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }
        return false;
    }

    private static string? GetStringProp(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

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
        var terms = identity.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        static string Normalize(string value) =>
            string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '_')).ToUpperInvariant();

        static bool IsHqName(string fileNameWithoutExt) =>
            System.Text.RegularExpressions.Regex.IsMatch(fileNameWithoutExt, @"_\d{1,4}M$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        string? Find(List<string> files, string kind)
        {
            bool Match(string f, bool wantHq)
            {
                var name = Path.GetFileName(f);
                var stem = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                var normalized = Normalize(name);
                if (!terms.All(t => normalized.Contains(Normalize(t), StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (kind == "audio")
                    return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
                if (!ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    return false;
                var hq = IsHqName(stem);
                return wantHq ? hq : !hq;
            }

            if (kind == "audio")
                return files.FirstOrDefault(f => Match(f, false));
            if (kind == "video")
                return files.FirstOrDefault(f => Match(f, false));
            if (kind == "hq")
            {
                var hq = files.FirstOrDefault(f => Match(f, true));
                if (hq is not null) return hq;
                var video = files.FirstOrDefault(f => Match(f, false));
                if (video is null) return null;
                var baseName = Path.GetFileNameWithoutExtension(video);
                return files.FirstOrDefault(f =>
                {
                    if (!Path.GetExtension(f).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                        return false;
                    var stem = Path.GetFileNameWithoutExtension(f);
                    if (!IsHqName(stem)) return false;
                    var stripped = System.Text.RegularExpressions.Regex.Replace(
                        stem, @"_\d{1,4}M$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    return string.Equals(stripped, baseName, StringComparison.OrdinalIgnoreCase);
                });
            }
            return null;
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
            place = item.Input.Place,
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
