using System.Text;

namespace BroadcastNasBridge.Services;

public sealed class NasService : IDisposable
{
    private readonly NasConfigStore _configStore;
    private readonly object _gate = new();
    private readonly SmbConnection _tempShare = new();
    private readonly SmbConnection _permanentShare = new();
    private readonly SmbConnection _schedule = new();
    private NasConfig _config = new();
    private string? _workLogRoot;
    private string? _mediaRoot;
    private string? _lastError;

    public NasService(NasConfigStore configStore)
    {
        _configStore = configStore;
        _config = configStore.Load();
    }

    public NasConfig Config
    {
        get { lock (_gate) return Clone(_config); }
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
                return _tempShare.IsConnected && _schedule.IsConnected;
        }
    }

    public string? TempRoot
    {
        get { lock (_gate) return _tempShare.Root; }
    }

    public string? ScheduleRoot
    {
        get { lock (_gate) return _schedule.Root; }
    }

    public string? WorkLogRoot
    {
        get { lock (_gate) return _workLogRoot; }
    }

    public string? MediaRoot
    {
        get { lock (_gate) return _mediaRoot; }
    }

    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    public SmbConnection ScheduleStore
    {
        get { lock (_gate) return _schedule; }
    }

    public object Status()
    {
        lock (_gate)
        {
            return new
            {
                app = "BroadcastNasBridge",
                port = 17820,
                connected = _tempShare.IsConnected && _schedule.IsConnected,
                host = _config.Host,
                tempRoot = _tempShare.Root,
                scheduleRoot = _schedule.Root,
                workLogRoot = _workLogRoot,
                mediaRoot = _mediaRoot,
                permanentConnected = _permanentShare.IsConnected,
                lastError = _lastError,
                configPath = _configStore.ConfigPath,
                scheduleJsonFile = _config.ScheduleJsonFile,
            };
        }
    }

    public void SetActiveScheduleJsonFile(string? fileName)
    {
        lock (_gate)
        {
            var name = string.IsNullOrWhiteSpace(fileName)
                ? null
                : Path.GetFileName(fileName.Trim());
            if (string.Equals(_config.ScheduleJsonFile, name, StringComparison.OrdinalIgnoreCase))
                return;
            _config.ScheduleJsonFile = name;
            _configStore.Save(_config);
        }
    }

    public NasConfig GetSetupDefaults()
    {
        var cfg = _configStore.Load();
        if (string.IsNullOrWhiteSpace(cfg.Host) && string.IsNullOrWhiteSpace(cfg.Username))
            cfg = _configStore.PrefillFromLegacy();
        if (string.IsNullOrWhiteSpace(cfg.TempShare)) cfg.TempShare = "Temp DATA";
        if (string.IsNullOrWhiteSpace(cfg.PermanentShare)) cfg.PermanentShare = "Permanent DATA";
        if (string.IsNullOrWhiteSpace(cfg.ScheduleRelativePath)) cfg.ScheduleRelativePath = "_data";
        if (string.IsNullOrWhiteSpace(cfg.WorkLogRelativePath)) cfg.WorkLogRelativePath = "_data/_work_log";
        if (string.IsNullOrWhiteSpace(cfg.MediaRelativePath)) cfg.MediaRelativePath = "H264_mp4 DATA";
        return cfg;
    }

    public void SaveConfig(NasConfig config)
    {
        lock (_gate)
        {
            _config = Clone(config);
            _configStore.Save(_config);
        }
    }

    public void Connect(NasConfig? config = null)
    {
        lock (_gate)
        {
            try
            {
                if (config is not null)
                {
                    _config = Clone(config);
                    _configStore.Save(_config);
                }

                var cfg = _config;
                if (string.IsNullOrWhiteSpace(cfg.Host))
                    throw new ArgumentException("NAS IP를 입력하세요.");
                if (string.IsNullOrWhiteSpace(cfg.Username))
                    throw new ArgumentException("계정을 입력하세요.");

                _tempShare.Connect(cfg.Host, cfg.TempShare, cfg.Username, cfg.Password);
                var schedulePath = CombineUnder(_tempShare.Root!, cfg.ScheduleRelativePath);
                EnsureDirectory(schedulePath);
                _schedule.UseLocalRoot(schedulePath);

                var workLogPath = CombineUnder(_tempShare.Root!, cfg.WorkLogRelativePath);
                EnsureDirectory(workLogPath);
                EnsureWorkLogLayout(workLogPath);
                _workLogRoot = workLogPath;

                try
                {
                    _permanentShare.Connect(cfg.Host, cfg.PermanentShare, cfg.Username, cfg.Password);
                    var mediaPath = CombineUnder(_permanentShare.Root!, cfg.MediaRelativePath);
                    if (!Directory.Exists(mediaPath))
                        throw new DirectoryNotFoundException($"미디어 경로 없음: {mediaPath}");
                    _mediaRoot = mediaPath;
                }
                catch (Exception ex)
                {
                    // Temp만 있어도 스케줄/일지는 사용 가능. Permanent 실패는 경고로 남김.
                    _mediaRoot = null;
                    _lastError = $"Permanent 공유 연결 실패(스케줄·일지는 사용 가능): {ex.Message}";
                    StartupConsole.WriteLine(_lastError);
                }

                if (_mediaRoot is not null)
                    _lastError = null;

                StartupConsole.WriteLine($"NAS 연결됨: {cfg.Host}");
                StartupConsole.WriteLine($"  스케줄: {_schedule.Root}");
                StartupConsole.WriteLine($"  일지: {_workLogRoot}");
                if (_mediaRoot is not null)
                    StartupConsole.WriteLine($"  미디어: {_mediaRoot}");
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                // Temp/스케줄만 열린 반연결 상태면 일지 루트가 비어 /worklog 가 로그인에 고착됨
                try { DisconnectUnlocked(); } catch { /* ignore */ }
                throw;
            }
        }
    }

    public void Disconnect()
    {
        lock (_gate) DisconnectUnlocked();
    }

    private void DisconnectUnlocked()
    {
        _schedule.Disconnect();
        _workLogRoot = null;
        _mediaRoot = null;
        _tempShare.Disconnect();
        _permanentShare.Disconnect();
        _lastError = null;
    }

    public void EnsureConnected()
    {
        lock (_gate)
        {
            if (_tempShare.IsConnected && _schedule.IsConnected)
            {
                EnsureWorkLogRootUnlocked();
                return;
            }
            Connect();
        }
    }

    /// <summary>Temp 공유는 살아 있는데 일지 루트만 비어 있는 경우 복구.</summary>
    public void EnsureWorkLogRoot()
    {
        lock (_gate) EnsureWorkLogRootUnlocked();
    }

    private void EnsureWorkLogRootUnlocked()
    {
        if (!string.IsNullOrWhiteSpace(_workLogRoot) && Directory.Exists(_workLogRoot))
            return;
        if (!_tempShare.IsConnected || string.IsNullOrWhiteSpace(_tempShare.Root))
            return;

        var rel = string.IsNullOrWhiteSpace(_config.WorkLogRelativePath)
            ? "_data/_work_log"
            : _config.WorkLogRelativePath;
        var workLogPath = CombineUnder(_tempShare.Root!, rel);
        EnsureDirectory(workLogPath);
        EnsureWorkLogLayout(workLogPath);
        _workLogRoot = workLogPath;
        StartupConsole.WriteLine($"일지 루트 복구: {_workLogRoot}");
    }

    public void Dispose() => Disconnect();

    private static string CombineUnder(string root, string relative)
    {
        var parts = (relative ?? "")
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = root;
        foreach (var part in parts)
            path = Path.Combine(path, part);
        return path;
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static void EnsureWorkLogLayout(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "work-logs"));
        Directory.CreateDirectory(Path.Combine(root, "audit"));
        Directory.CreateDirectory(Path.Combine(root, "locks"));
        SeedIfMissing(Path.Combine(root, "profiles.json"), """
            {
              "version": 1,
              "profiles": [
                { "id": "ko_yunseok", "displayName": "고윤석" },
                { "id": "kim_jonghee", "displayName": "김종희" },
                { "id": "lim_wonbin", "displayName": "임원빈" }
              ]
            }
            """);
        SeedIfMissing(Path.Combine(root, "defaults.json"), """
            {
              "version": 2,
              "specialNotesMax": 10,
              "titleRules": [],
              "templateButtons": {},
              "customTemplates": []
            }
            """);
        SeedIfMissing(Path.Combine(root, "special-notes.json"), """
            {
              "version": 1,
              "notes": []
            }
            """);
    }

    private static void SeedIfMissing(string path, string contents)
    {
        if (File.Exists(path)) return;
        using var document = System.Text.Json.JsonDocument.Parse(contents);
        File.WriteAllText(
            path,
            System.Text.Json.JsonSerializer.Serialize(document.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
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
}
