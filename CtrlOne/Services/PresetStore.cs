using System.Text.Json;
using System.Text.Json.Serialization;

namespace CtrlOne.Services;

public sealed class SelectionPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Guid> DeviceIds { get; set; } = [];
}

/// <summary>
/// 그룹 제어 프리셋(이름 + 장비 ID 목록)을 presets.json에 저장한다.
/// </summary>
public sealed class PresetStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PresetStore(string? path = null)
    {
        _path = path ?? AppPaths.PresetsJsonPath;
    }

    public List<SelectionPreset> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return [];

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<SelectionPreset>>(json, _json) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public void Save(IEnumerable<SelectionPreset> presets)
    {
        lock (_sync)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(presets.ToList(), _json);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }
}
