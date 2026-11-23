using System.Globalization;
using System.Text.RegularExpressions;
using ScheduleReader.Models;
using ScheduleReader.Parser;

namespace ScheduleReader.Excel;

public static class HeaderTableExtractor
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["date"] = ["날짜", "일자", "일정일", "date", "스케줄일", "방송일", "시작일"],
        ["end_date"] = ["종료일", "끝날짜", "종료", "end", "종료일자", "마침일"],
        ["title"] = ["제목", "일정", "행사", "스케줄", "내용", "title", "일정제목", "프로그램"],
        ["speaker"] = ["강사", "설교", "강사명", "speaker", "설교자", "인도"],
        ["time"] = ["시간", "시작", "시작시간", "time", "시각"],
        ["place"] = ["장소", "place", "위치", "홀"],
        ["note"] = ["비고", "설명", "메모", "note", "부가", "준비"],
    };

    public static (int HeaderIndex, Dictionary<string, int> ColMap)? DetectHeaderMap(
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        (int Idx, Dictionary<string, int> Map, int Score)? best = null;
        var limit = Math.Min(40, rows.Count);
        for (var idx = 0; idx < limit; idx++)
        {
            var mapping = new Dictionary<string, int>();
            var row = rows[idx];
            for (var col = 0; col < row.Count; col++)
            {
                var field = MatchField(row[col]);
                if (field is not null && !mapping.ContainsKey(field))
                    mapping[field] = col;
            }

            var score = mapping.Count;
            if (mapping.ContainsKey("date")) score += 2;
            if (mapping.ContainsKey("title")) score += 2;
            if (score >= 3 && mapping.ContainsKey("date") && mapping.ContainsKey("title"))
            {
                if (best is null || score > best.Value.Score)
                    best = (idx, mapping, score);
            }
        }

        return best is null ? null : (best.Value.Idx, best.Value.Map);
    }

    public static List<RawEvent> ExtractRows(
        IReadOnlyList<IReadOnlyList<object?>> values,
        IReadOnlyList<IReadOnlyList<string>> texts,
        int headerIndex,
        Dictionary<string, int> colMap,
        int? yearHint,
        string sheetTitle,
        List<string> warnings)
    {
        var events = new List<RawEvent>();
        for (var r = headerIndex + 1; r < values.Count; r++)
        {
            object? Col(string field)
            {
                if (!colMap.TryGetValue(field, out var i)) return null;
                var row = values[r];
                return i < row.Count ? row[i] : null;
            }

            string ColText(string field)
            {
                if (!colMap.TryGetValue(field, out var i)) return "";
                var row = texts[r];
                return i < row.Count ? row[i] : "";
            }

            var title = ColText("title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var eventDate = ParseDate(Col("date"), yearHint);
            if (eventDate is null)
            {
                warnings.Add($"날짜 없음으로 건너뜀: {title}");
                continue;
            }

            DateOnly? endDate = null;
            if (colMap.ContainsKey("end_date"))
                endDate = ParseDate(Col("end_date"), yearHint);

            events.Add(new RawEvent
            {
                EventDate = eventDate,
                EndDate = endDate,
                Title = title,
                Speaker = colMap.ContainsKey("speaker") ? ColText("speaker") : "",
                Place = colMap.ContainsKey("place") ? ColText("place") : "",
                Notes = colMap.ContainsKey("note") ? ColText("note") : "",
                StartTime = colMap.ContainsKey("time") ? ParseTimeText(Col("time")) : null,
                SourceHint = $"{sheetTitle}!row",
            });
        }

        return events;
    }

    public static (int? Year, int? Month) GuessYearMonth(SheetMatrix sheet)
    {
        var hint = YearMonthParser.FromSheet(sheet);
        return (hint.Year, hint.Month);
    }

    public static DateOnly? ParseDate(object? value, int? defaultYear)
    {
        if (value is null) return null;
        if (value is DateOnly d) return d;
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        if (value is TimeOnly) return null;

        if (value is double dbl)
        {
            try { return DateOnly.FromDateTime(DateTime.FromOADate(dbl)); }
            catch { /* ignore */ }
        }

        if (value is long or int)
        {
            try
            {
                var n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return DateOnly.FromDateTime(DateTime.FromOADate(n));
            }
            catch { /* ignore */ }
        }

        var text = CellText.From(value);
        if (string.IsNullOrWhiteSpace(text)) return null;

        string[] formats =
        [
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yy-MM-dd", "yy/MM/dd"
        ];
        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(text.Length >= 10 ? text[..10] : text, fmt,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return DateOnly.FromDateTime(parsed);
        }

        var m = Regex.Match(text, @"^(\d{1,2})[./-](\d{1,2})(?:[./-](\d{2,4}))?$");
        if (m.Success)
        {
            var a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int year;
            if (m.Groups[3].Success)
            {
                year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (year < 100) year += 2000;
            }
            else
            {
                year = defaultYear ?? DateTime.Today.Year;
            }

            try { return new DateOnly(year, a, b); }
            catch
            {
                try { return new DateOnly(year, b, a); }
                catch { return null; }
            }
        }

        var m2 = Regex.Match(text, @"(\d{4})\s*년\s*(\d{1,2})\s*월\s*(\d{1,2})\s*일");
        if (m2.Success)
        {
            try
            {
                return new DateOnly(
                    int.Parse(m2.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(m2.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(m2.Groups[3].Value, CultureInfo.InvariantCulture));
            }
            catch { return null; }
        }

        var m3 = Regex.Match(text, @"(\d{1,2})\s*월\s*(\d{1,2})\s*일");
        if (m3.Success && defaultYear is not null)
        {
            try
            {
                return new DateOnly(
                    defaultYear.Value,
                    int.Parse(m3.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(m3.Groups[2].Value, CultureInfo.InvariantCulture));
            }
            catch { return null; }
        }

        return null;
    }

    public static string? ParseTimeText(object? value)
    {
        if (value is null) return null;
        if (value is DateTime dt) return $"{dt.Hour:D2}:{dt.Minute:D2}";
        if (value is TimeOnly t) return $"{t.Hour:D2}:{t.Minute:D2}";

        var text = CellText.From(value);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var n = Normalizer.ParseHhmm(text);
        if (n > 0) return $"{n / 100:D2}:{n % 100:D2}";

        var m = Regex.Match(text, @"(\d{1,2})\s*[:시]\s*(\d{0,2})");
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = string.IsNullOrEmpty(m.Groups[2].Value)
                ? 0
                : int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
                return $"{hour:D2}:{minute:D2}";
        }

        return null;
    }

    private static string NormalizeHeader(string text) =>
        Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", "");

    private static string? MatchField(string header)
    {
        var key = NormalizeHeader(header);
        if (string.IsNullOrEmpty(key)) return null;

        foreach (var (field, aliases) in Aliases)
        {
            foreach (var alias in aliases)
            {
                if (key == NormalizeHeader(alias)) return field;
            }
        }

        foreach (var (field, aliases) in Aliases)
        {
            foreach (var alias in aliases)
            {
                var a = NormalizeHeader(alias);
                if (a.Length < 2 || key.Length < 2) continue;
                if (key.Contains(a, StringComparison.Ordinal) || a.Contains(key, StringComparison.Ordinal))
                    return field;
            }
        }

        return null;
    }
}
