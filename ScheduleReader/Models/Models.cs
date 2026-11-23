namespace ScheduleReader.Models;

/// <summary>엑셀에서 뽑은 중간 일정(정규화 전).</summary>
public sealed class RawEvent
{
    public DateOnly? EventDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Title { get; set; } = "";
    public string Speaker { get; set; } = "";
    public string? StartTime { get; set; }
    public string Place { get; set; } = "";
    public string Notes { get; set; } = "";
    public double Confidence { get; set; } = 1.0;
    public string SourceHint { get; set; } = "";
}

/// <summary>schedule-data-manager 웹 JSON 항목.</summary>
public sealed class ScheduleItem
{
    public string Id { get; set; } = "";
    public string ScheduleDate { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public int StartTime { get; set; }
    public string ScheduleTitle { get; set; } = "";
    public string AccessoryInfo { get; set; } = "";
    public string Place { get; set; } = "None";
    public string SermonSpeaker { get; set; } = "";
    public List<string> Preparation { get; set; } = [];
    /// <summary>기존 스키마 오타 유지(preaparation). BSS 스위트도 이 키를 읽습니다.</summary>
    public string PreaparationNote { get; set; } = "";
    /// <summary>정규 철자. 직렬화 시 <see cref="PreaparationNote"/> 와 같은 값.</summary>
    public string PreparationNote
    {
        get => PreaparationNote;
        set => PreaparationNote = value ?? "";
    }
    /// <summary>BSS /schedule · /worklog 기본값은 auto.</summary>
    public string Color { get; set; } = "auto";
    /// <summary>색상 모드(auto / none / custom).</summary>
    public string ColorMode { get; set; } = "auto";
}

/// <summary>편집된 일정 POST 내보내기 요청.</summary>
public sealed class ExportRequest
{
    public string SessionId { get; set; } = "";
    public List<ScheduleItem>? Items { get; set; }
}

/// <summary>엑셀 셀 값과 A1 주소.</summary>
public sealed class CellInfo
{
    public string Address { get; set; } = "";
    public int Row { get; set; }
    public int Column { get; set; }
    public string Text { get; set; } = "";
    public object? RawValue { get; set; }
}

public sealed class ShapeTextInfo
{
    public string Text { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int? FromCol { get; set; }
    public int? FromRow { get; set; }
    public int? ToCol { get; set; }
    public int? ToRow { get; set; }
    public string Sheet { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class ExtractResult
{
    public bool Ok { get; set; }
    public List<RawEvent> Events { get; set; } = [];
    public List<ScheduleItem> Items { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public Dictionary<string, object?> Meta { get; set; } = new();
    public List<ShapeTextInfo> Shapes { get; set; } = [];
    public List<CellInfo> Cells { get; set; } = [];
}
