using System.Text.Json;
using System.Text.Json.Serialization;

namespace BroadcastNasBridge.Services;

public sealed class NasConfig
{
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool RememberPassword { get; set; } = true;

    public string TempShare { get; set; } = "Temp DATA";
    public string PermanentShare { get; set; } = "Permanent DATA";

    /// <summary>Temp 공유 기준 상대 경로 (스케줄 JSON 루트).</summary>
    public string ScheduleRelativePath { get; set; } = "_data";

    /// <summary>Temp 공유 기준 상대 경로 (작업일지 루트).</summary>
    public string WorkLogRelativePath { get; set; } = "_data/_work_log";

    /// <summary>Permanent 공유 기준 상대 경로 (미디어 스캔).</summary>
    public string MediaRelativePath { get; set; } = "H264_mp4 DATA";

    /// <summary>FC 동기화용 스케줄 JSON 파일명 (선택).</summary>
    public string? ScheduleJsonFile { get; set; }

    public string LocalPath { get; set; } = "";
}

public sealed class NasConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public NasConfigStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _path = Path.Combine(appDataDirectory, "nas.json");
    }

    public string ConfigPath => _path;

    public NasConfig Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return PrefillFromLegacy() ?? new NasConfig();

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<NasConfig>(json, JsonOptions) ?? new NasConfig();
            }
            catch
            {
                return new NasConfig();
            }
        }
    }

    public void Save(NasConfig config)
    {
        lock (_gate)
        {
            var toSave = Clone(config);
            if (!toSave.RememberPassword)
                toSave.Password = "";
            File.WriteAllText(_path, JsonSerializer.Serialize(toSave, JsonOptions));
        }
    }

    public NasConfig PrefillFromLegacy()
    {
        var cfg = new NasConfig();
        TryMergeFileChecker(cfg);
        return cfg;
    }

    private static NasConfig Clone(NasConfig c) => new()
    {
        Host = c.Host,
        Username = c.Username,
        Password = c.Password,
        RememberPassword = c.RememberPassword,
        TempShare = c.TempShare,
        PermanentShare = c.PermanentShare,
        ScheduleRelativePath = c.ScheduleRelativePath,
        WorkLogRelativePath = c.WorkLogRelativePath,
        MediaRelativePath = c.MediaRelativePath,
        ScheduleJsonFile = c.ScheduleJsonFile,
        LocalPath = c.LocalPath,
    };

    private static void TryMergeFileChecker(NasConfig cfg)
    {
        try
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "FileChecker", "data", "settings.json"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "data", "filechecker-settings.json"),
            };
            foreach (var candidate in candidates)
            {
                var full = System.IO.Path.GetFullPath(candidate);
                if (!File.Exists(full)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(full));
                var root = doc.RootElement;
                if (root.TryGetProperty("nasIp", out var ip)) cfg.Host = ip.GetString() ?? cfg.Host;
                if (root.TryGetProperty("nasUsername", out var user)) cfg.Username = user.GetString() ?? cfg.Username;
                if (root.TryGetProperty("nasPassword", out var pass)) cfg.Password = pass.GetString() ?? cfg.Password;
                if (root.TryGetProperty("nasSearchPath", out var search))
                {
                    var path = (search.GetString() ?? "").Replace('/', '\\').Trim('\\');
                    var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1) cfg.PermanentShare = parts[0];
                    if (parts.Length >= 2) cfg.MediaRelativePath = string.Join('/', parts.Skip(1));
                }
                if (root.TryGetProperty("localPath", out var local)) cfg.LocalPath = local.GetString() ?? "";
                if (root.TryGetProperty("scheduleJsonPath", out var sj))
                    cfg.ScheduleJsonFile = System.IO.Path.GetFileName(sj.GetString());
                break;
            }
        }
        catch
        {
            // ignore
        }
    }
}
