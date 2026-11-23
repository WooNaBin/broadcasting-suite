using System.Globalization;
using ScheduleReader.Models;

namespace ScheduleReader.Parser;

/// <summary>
/// 관공서 공휴일(고정·설/추석 연휴·대체) 판별.
/// ScheduleDataManager 달력 표시와 같은 기준을 쓴다.
/// </summary>
public static class KoreanHolidays
{
    // 음력 명절 당일(설·추석·부처님오신날). 연휴·대체는 아래에서 계산.
    private static readonly Dictionary<int, (string Seollal, string Chuseok, string Buddha)> LunarCore = new()
    {
        [2024] = ("2024-02-10", "2024-09-17", "2024-05-15"),
        [2025] = ("2025-01-29", "2025-10-06", "2025-05-05"),
        [2026] = ("2026-02-17", "2026-09-25", "2026-05-24"),
        [2027] = ("2027-02-07", "2027-09-15", "2027-05-13"),
        [2028] = ("2028-01-27", "2028-10-03", "2028-05-02"),
        [2029] = ("2029-02-13", "2029-09-22", "2029-05-20"),
        [2030] = ("2030-02-03", "2030-09-12", "2030-05-09"),
        [2031] = ("2031-01-23", "2031-10-01", "2031-05-28"),
        [2032] = ("2032-02-11", "2032-09-19", "2032-05-16"),
        [2033] = ("2033-01-31", "2033-09-08", "2033-05-06"),
        [2034] = ("2034-02-19", "2034-09-27", "2034-05-25"),
        [2035] = ("2035-02-08", "2035-09-16", "2035-05-15"),
    };

    private static readonly HashSet<string> WeekendSensitive = new(StringComparer.Ordinal)
    {
        "삼일절", "어린이날", "부처님오신날", "광복절", "개천절", "한글날", "성탄절", "근로자의 날",
    };

    private static readonly Dictionary<int, Dictionary<string, string>> Cache = new();

    public static string? GetHolidayName(string? isoDate)
    {
        if (!TryParseIso(isoDate, out var date)) return null;
        var map = BuildYear(date.Year);
        return map.TryGetValue(ToIso(date), out var name) ? name : null;
    }

    /// <summary>
    /// 공휴일 날짜이면서 제목이 그 공휴일(또는 대체) 명칭과 같으면 제외.
    /// </summary>
    public static List<ScheduleItem> ExcludeHolidaySchedules(IEnumerable<ScheduleItem> items)
    {
        return items.Where(i => !IsHolidaySchedule(i.ScheduleTitle, i.ScheduleDate)).ToList();
    }

    public static bool IsHolidaySchedule(string? title, string? scheduleDateIso)
    {
        var holidayName = GetHolidayName(scheduleDateIso);
        if (string.IsNullOrEmpty(holidayName)) return false;
        var t = Norm(title);
        if (t.Length == 0) return false;

        foreach (var part in holidayName.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var h = Norm(part);
            if (h.Length == 0) continue;
            if (t == h) return true;
            // 제목 "어린이날" · 공휴일명 "어린이날 대체"
            if (h.StartsWith(t + " ", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static Dictionary<string, string> BuildYear(int year)
    {
        if (Cache.TryGetValue(year, out var cached)) return cached;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var fixedDays = new List<(int Month, int Day, string Name)>
        {
            (1, 1, "신정"),
            (3, 1, "삼일절"),
            (5, 5, "어린이날"),
            (6, 6, "현충일"),
            (8, 15, "광복절"),
            (10, 3, "개천절"),
            (10, 9, "한글날"),
            (12, 25, "성탄절"),
        };
        if (year >= 2026)
            fixedDays.Insert(2, (5, 1, "근로자의 날"));

        foreach (var (month, day, name) in fixedDays)
            Add(map, $"{year}-{month:D2}-{day:D2}", name);

        if (LunarCore.TryGetValue(year, out var lunar))
        {
            Add(map, ShiftIso(lunar.Seollal, -1), "설날 전날");
            Add(map, lunar.Seollal, "설날");
            Add(map, ShiftIso(lunar.Seollal, 1), "설날 다음날");
            Add(map, ShiftIso(lunar.Chuseok, -1), "추석 전날");
            Add(map, lunar.Chuseok, "추석");
            Add(map, ShiftIso(lunar.Chuseok, 1), "추석 다음날");
            Add(map, lunar.Buddha, "부처님오신날");

            AddFestivalSubstitute(map, lunar.Seollal!);
            AddFestivalSubstitute(map, lunar.Chuseok!);
        }

        foreach (var (iso, name) in map.ToList())
        {
            if (!WeekendSensitive.Contains(name)) continue;
            if (!TryParseIso(iso, out var d)) continue;
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) continue;
            var alt = NextOpenDay(map, iso);
            if (!string.IsNullOrEmpty(alt))
                Add(map, alt, $"{name} 대체");
        }

        Cache[year] = map;
        return map;
    }

    private static void AddFestivalSubstitute(Dictionary<string, string> map, string mainIso)
    {
        var after = ShiftIso(mainIso, 1);
        if (after is null) return;
        var span = new[] { ShiftIso(mainIso, -1), mainIso, after };
        var overlapsSunday = span.Any(iso =>
            iso is not null && TryParseIso(iso, out var d) && d.DayOfWeek == DayOfWeek.Sunday);
        if (!overlapsSunday) return;
        var alt = NextOpenDay(map, after);
        if (!string.IsNullOrEmpty(alt))
            Add(map, alt, "대체공휴일");
    }

    private static string? NextOpenDay(Dictionary<string, string> map, string fromIso)
    {
        if (!TryParseIso(fromIso, out var cur)) return null;
        for (var i = 0; i < 14; i++)
        {
            cur = cur.AddDays(1);
            var iso = ToIso(cur);
            if (cur.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (map.ContainsKey(iso)) continue;
            return iso;
        }

        return null;
    }

    private static void Add(Dictionary<string, string> map, string? iso, string name)
    {
        if (string.IsNullOrWhiteSpace(iso) || string.IsNullOrWhiteSpace(name)) return;
        if (!map.TryGetValue(iso, out var prev))
        {
            map[iso] = name;
            return;
        }

        if (prev == name || prev.Contains(name, StringComparison.Ordinal)) return;
        map[iso] = $"{prev}·{name}";
    }

    private static string? ShiftIso(string? iso, int days)
    {
        if (!TryParseIso(iso, out var d)) return null;
        return ToIso(d.AddDays(days));
    }

    private static string Norm(string? value) =>
        string.Join(" ", (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string ToIso(DateOnly d) =>
        d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseIso(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            (value ?? "").Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
}
