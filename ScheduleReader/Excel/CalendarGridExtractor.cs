using System.Text.RegularExpressions;
using ScheduleReader.Models;

namespace ScheduleReader.Excel;

public static class CalendarGridExtractor
{
    private static readonly string[] WeekdayMarkers = ["일", "월", "화", "수", "목", "금", "토"];
    private static readonly Regex MonthDayRe = new(
        @"^\s*(\d{1,2})[./](\d{1,2})(?:\s|$)",
        RegexOptions.Compiled);
    private static readonly Regex NoiseRe = new(
        @"맨위로|글씨체\s*통일|가운데정렬|^대집회D-\d+$",
        RegexOptions.Compiled);

    public sealed class WeekdayHeader
    {
        public int Row { get; init; }
        public int[] Columns { get; init; } = [];
    }

    public sealed class Result
    {
        public List<RawEvent> Events { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
        public Dictionary<string, object?> Meta { get; init; } = new();
        public Dictionary<(int Row, int Col), DateOnly> DayMap { get; init; } = new();
    }

    public static WeekdayHeader? FindWeekdayHeader(IReadOnlyList<IReadOnlyList<string>> matrix)
    {
        var limit = Math.Min(25, matrix.Count);
        for (var r = 0; r < limit; r++)
        {
            var row = matrix[r];
            var cols = new List<int>(7);
            var next = 0;
            for (var c = 0; c < row.Count && next < 7; c++)
            {
                var head = Regex.Replace(row[c] ?? "", @"\s+", "");
                if (string.IsNullOrEmpty(head)) continue;
                if (head.StartsWith(WeekdayMarkers[next], StringComparison.Ordinal))
                {
                    cols.Add(c);
                    next++;
                }
                else if (next > 0)
                {
                    break;
                }
            }

            if (cols.Count == 7)
                return new WeekdayHeader { Row = r, Columns = [.. cols] };
        }

        return null;
    }

    public static (int? Day, string Title, string Speaker) ParseCellContent(string text)
    {
        var (day, body) = BracketParser.StripLeadingDay(text);
        if (string.IsNullOrWhiteSpace(body)) return (day, "", "");

        var entries = BracketParser.ParseEntries(body, day);
        if (entries.Count == 0)
            return (day, body.Replace('\n', ' ').Trim(), "");

        var title = string.Join(" / ", entries.Select(e => e.Title).Where(t => t.Length > 0).Distinct());
        var speaker = string.Join(", ", entries.Select(e => e.Speaker).Where(s => s.Length > 0));
        return (day, title, speaker);
    }

    public static Result Extract(SheetMatrix sheet, int year, int month)
    {
        BracketParser.SetContext(year, month);
        var warnings = new List<string>();
        var meta = new Dictionary<string, object?> { ["strategy"] = "calendar_grid" };
        var header = FindWeekdayHeader(sheet.Texts);
        if (header is null)
        {
            warnings.Add("요일(일~토) 헤더 행을 찾지 못했습니다.");
            return new Result { Warnings = warnings, Meta = meta };
        }

        var dayMap = BuildDayMap(sheet, header.Row, header.Columns, year, month);
        var events = new List<RawEvent>();

        foreach (var c in header.Columns)
            CollectColumnEvents(sheet, header.Row, c, dayMap, year, month, events);

        foreach (var c in OverflowColumns(header.Columns, sheet.ColCount))
            CollectOverflowEvents(sheet, header.Row, c, dayMap, year, month, events);

        var uniq = new Dictionary<(DateOnly?, DateOnly?, string, string, string), RawEvent>();
        foreach (var e in events)
            uniq[(e.EventDate, e.EndDate, e.Title, e.Speaker, e.Notes)] = e;

        events = uniq.Values
            .OrderBy(e => e.EventDate?.ToString("yyyy-MM-dd") ?? "")
            .ThenBy(e => e.Title)
            .ToList();

        if (events.Count == 0)
            warnings.Add("달력 격자는 찾았지만 일정 텍스트를 읽지 못했습니다.");

        meta["headerRow"] = header.Row + 1;
        meta["startCol"] = header.Columns[0] + 1;
        meta["weekdayCols"] = header.Columns.Select(c => c + 1).ToArray();
        meta["year"] = year;
        meta["month"] = month;
        meta["eventCount"] = events.Count;
        meta["dayCellCount"] = dayMap.Count;
        return new Result
        {
            Events = events,
            Warnings = warnings,
            Meta = meta,
            DayMap = dayMap,
        };
    }

    public static Dictionary<(int Row, int Col), DateOnly> BuildDayMap(
        SheetMatrix sheet,
        int headerRow,
        int startCol,
        int year,
        int month) =>
        BuildDayMap(sheet, headerRow, Enumerable.Range(startCol, 7).ToArray(), year, month);

    public static Dictionary<(int Row, int Col), DateOnly> BuildDayMap(
        SheetMatrix sheet,
        int headerRow,
        int[] columns,
        int year,
        int month)
    {
        var markers = new List<(int Row, int Col, DateOnly Date)>();
        DateOnly? expected = null;
        for (var r = headerRow + 1; r < sheet.Texts.Count; r++)
        {
            foreach (var c in columns)
            {
                var date = ReadCellDate(sheet, r, c, year, month, expected);
                if (date is null) continue;
                if (expected is null || date == expected)
                {
                    markers.Add((r, c, date.Value));
                    expected = date.Value.AddDays(1);
                }
            }
        }

        return markers.ToDictionary(m => (m.Row, m.Col), m => m.Date);
    }

    private static void CollectColumnEvents(
        SheetMatrix sheet,
        int headerRow,
        int c,
        Dictionary<(int Row, int Col), DateOnly> dayMap,
        int year,
        int month,
        List<RawEvent> events)
    {
        DateOnly? current = null;
        var buf = new List<string>();
        string? bucketAddress = null;

        void Flush()
        {
            if (current is null || buf.Count == 0) return;
            var combined = string.Join('\n', buf);
            buf.Clear();
            AddParsed(sheet, combined, current.Value, bucketAddress, headerRow, c, events);
        }

        for (var r = headerRow + 1; r < sheet.Texts.Count; r++)
        {
            var text = GetText(sheet, r, c);
            if (dayMap.TryGetValue((r, c), out var mapped))
            {
                Flush();
                current = mapped;
                bucketAddress = ExcelAddress.From(r + 1, c + 1);
                var body = StripDatePrefix(text);
                if (!string.IsNullOrWhiteSpace(body) && !IsNoise(body))
                    buf.Add(body);
                continue;
            }

            if (current is null || string.IsNullOrWhiteSpace(text) || IsNoise(text)) continue;
            buf.Add(text.Trim());
        }

        Flush();
        BracketParser.SetContext(year, month);
    }

    private static void CollectOverflowEvents(
        SheetMatrix sheet,
        int headerRow,
        int c,
        Dictionary<(int Row, int Col), DateOnly> dayMap,
        int year,
        int month,
        List<RawEvent> events)
    {
        DateOnly? current = null;
        var buf = new List<string>();
        string? bucketAddress = null;

        void Flush()
        {
            if (current is null || buf.Count == 0) return;
            var combined = string.Join('\n', buf);
            buf.Clear();
            AddParsed(sheet, combined, current.Value, bucketAddress, headerRow, c, events);
        }

        for (var r = headerRow + 1; r < sheet.Texts.Count; r++)
        {
            var weekDate = NearbyDate(dayMap, r);
            if (weekDate is not null && weekDate != current)
            {
                Flush();
                current = weekDate;
                bucketAddress = ExcelAddress.From(r + 1, c + 1);
            }

            var text = GetText(sheet, r, c);
            if (current is null || string.IsNullOrWhiteSpace(text) || IsNoise(text)) continue;
            if (bucketAddress is null)
                bucketAddress = ExcelAddress.From(r + 1, c + 1);
            buf.Add(text.Trim());
        }

        Flush();
        BracketParser.SetContext(year, month);
    }

    private static void AddParsed(
        SheetMatrix sheet,
        string text,
        DateOnly contextDate,
        string? address,
        int headerRow,
        int col,
        List<RawEvent> events)
    {
        BracketParser.SetContext(contextDate.Year, contextDate.Month);
        var hint = $"{sheet.Title}!{address ?? ExcelAddress.From(headerRow + 2, col + 1)}";
        foreach (var ev in BracketParser.ToRawEvents(text, contextDate.Day, hint))
        {
            if (string.IsNullOrWhiteSpace(ev.Title) || ev.EventDate is null) continue;
            events.Add(ev);
        }
    }

    private static DateOnly? NearbyDate(Dictionary<(int Row, int Col), DateOnly> dayMap, int row)
    {
        var weekRow = -1;
        foreach (var key in dayMap.Keys)
        {
            if (key.Row <= row && key.Row > weekRow)
                weekRow = key.Row;
        }

        if (weekRow < 0) return null;
        DateOnly? start = null;
        foreach (var kv in dayMap)
        {
            if (kv.Key.Row != weekRow) continue;
            if (start is null || kv.Value < start)
                start = kv.Value;
        }

        return start;
    }

    private static IEnumerable<int> OverflowColumns(int[] weekdayCols, int colCount)
    {
        if (weekdayCols.Length == 0) yield break;
        var min = weekdayCols.Min();
        var max = weekdayCols.Max();
        var set = weekdayCols.ToHashSet();
        for (var c = min; c <= max && c < colCount; c++)
        {
            if (!set.Contains(c))
                yield return c;
        }
    }

    private static DateOnly? ReadCellDate(
        SheetMatrix sheet,
        int r,
        int c,
        int year,
        int month,
        DateOnly? expected)
    {
        var text = GetText(sheet, r, c);
        var md = MonthDayRe.Match(text);
        if (md.Success
            && int.TryParse(md.Groups[1].Value, out var m)
            && int.TryParse(md.Groups[2].Value, out var d))
        {
            return ResolveMonthDay(year, month, m, d);
        }

        var (day, _) = BracketParser.StripLeadingDay(text);
        if (day is null && r < sheet.Values.Count && c < sheet.Values[r].Count)
        {
            var raw = sheet.Values[r][c];
            if (raw is long or int)
            {
                var n = Convert.ToInt32(raw);
                if (n is >= 1 and <= 31) day = n;
            }
        }

        if (day is null) return null;
        if (expected is DateOnly exp && day == exp.Day)
            return exp;
        if (day < 1 || day > 31) return null;
        try
        {
            return new DateOnly(year, month, day.Value);
        }
        catch
        {
            return null;
        }
    }

    private static DateOnly? ResolveMonthDay(int year, int month, int m, int d)
    {
        if (m is < 1 or > 12 || d < 1) return null;
        var y = year;
        if (m - month > 6) y--;
        else if (month - m > 6) y++;
        try
        {
            if (d > DateTime.DaysInMonth(y, m)) return null;
            return new DateOnly(y, m, d);
        }
        catch
        {
            return null;
        }
    }

    private static string StripDatePrefix(string text)
    {
        var md = MonthDayRe.Match(text);
        if (md.Success)
            return text[md.Length..].Trim();
        var (_, body) = BracketParser.StripLeadingDay(text);
        return body;
    }

    private static bool IsNoise(string text)
    {
        var t = text.Trim();
        return t.Length == 0 || NoiseRe.IsMatch(t);
    }

    private static string GetText(SheetMatrix sheet, int r, int c)
    {
        if (r < 0 || r >= sheet.Texts.Count) return "";
        var row = sheet.Texts[r];
        if (c < 0 || c >= row.Count) return "";
        return row[c] ?? "";
    }
}
