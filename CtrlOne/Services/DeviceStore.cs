using System.Text.Json;
using System.Text.Json.Serialization;

namespace CtrlOne.Services;

/// <summary>
/// devices.json 영속화. 정렬 인덱스(Order)를 포함해 저장/로드한다.
/// </summary>
public sealed class DeviceStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DeviceStore(string? path = null)
    {
        _path = path ?? AppPaths.DevicesJsonPath;
    }

    public string FilePath => _path;

    public List<DeviceRecord> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return [];

            try
            {
                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<DeviceRecord>>(json, _json) ?? [];
                return list.OrderBy(d => d.Order).ToList();
            }
            catch
            {
                return [];
            }
        }
    }

    public void Save(IEnumerable<DeviceRecord> devices)
    {
        lock (_sync)
        {
            var ordered = devices
                .OrderBy(d => d.Order)
                .Select((d, index) =>
                {
                    d.Order = index;
                    return d;
                })
                .ToList();

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(ordered, _json);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

public sealed class DeviceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Order { get; set; }
    public bool IsSelected { get; set; } = true;
    public string DeviceType { get; set; } = "HyperDeck";
}
