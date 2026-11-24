using System.Text.Json;

namespace CtrlOne.Services;

/// <summary>
/// 원터치 액션: 이름 + 명령 + 대상 장비 목록.
/// Stream Deck·대시보드 버튼은 이 액션만 실행한다.
/// </summary>
public sealed class OneTouchAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>play | record | stop | preview | previewOff | stream | streamOff | recordSpill | deviceStatus</summary>
    public string Command { get; set; } = "record";
    /// <summary>대상 장비. deviceStatus는 비워 두면 등록된 전체 장비를 로그한다.</summary>
    public List<Guid> DeviceIds { get; set; } = [];
}

/// <summary>
/// 원터치 액션을 actions.json에 저장한다.
/// </summary>
public sealed class ActionStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ActionStore(string? path = null)
    {
        _path = path ?? AppPaths.ActionsJsonPath;
    }

    public List<OneTouchAction> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return [];

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<OneTouchAction>>(json, _json) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public void Save(IEnumerable<OneTouchAction> actions)
    {
        lock (_sync)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(actions.ToList(), _json);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }
}
