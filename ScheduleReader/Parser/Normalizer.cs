using System.Globalization;
using System.Text.RegularExpressions;
using ScheduleReader.Excel;
using ScheduleReader.Models;

namespace ScheduleReader.Parser;

public static class PlaceMap
{
    private static readonly HashSet<string> ValidPlaces =
    [
        "None", "Main_Large", "Main_Medium", "Main_Small",
        "Edu_Large", "Edu_Medium", "Edu_Small",
        "Edu_Nongseon", "Edu_Siseon", "Edu_Go2", "Etc"
    ];

    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["본당 대강당"] = "Main_Large",
        ["본당 중강당"] = "Main_Medium",
        ["본당 소강당"] = "Main_Small",
        ["본당 중예배실"] = "Main_Medium",
        ["교육원 대강당"] = "Edu_Large",
        ["교육원 중강당"] = "Edu_Medium",
        ["교육원 소강당"] = "Edu_Small",
        ["교육원 농선부실"] = "Edu_Nongseon",
        ["교육원 시선부실"] = "Edu_Siseon",
        ["시선본당"] = "Edu_Siseon",
        ["교육원 고2실"] = "Edu_Go2",
        ["고2실"] = "Edu_Go2",
        ["기타"] = "Etc",
    };

    public static string Map(string? raw)
    {
        const string defaultPlace = "None";
        var text = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return defaultPlace;
        if (ValidPlaces.Contains(text)) return text;

        foreach (var key in Mappings.Keys.OrderByDescending(k => k.Length))
        {
            if (text.Contains(key, StringComparison.Ordinal))
            {
                var value = Mappings[key];
                return ValidPlaces.Contains(value) ? value : defaultPlace;
            }
        }

        return defaultPlace;
    }
}

public static class Normalizer
{
    public static int ParseHhmm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var s = value.Trim();
        var m = Regex.Match(s, @"^(\d{1,2}):(\d{2})$");
        int hour, minute;
        if (m.Success)
        {
            hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var digits = Regex.Replace(s, @"\D", "");
            if (digits.Length is not (3 or 4)) return 0;
            digits = digits.PadLeft(4, '0');
            hour = int.Parse(digits[..2], CultureInfo.InvariantCulture);
            minute = int.Parse(digits[2..], CultureInfo.InvariantCulture);
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59) return 0;
        minute = minute / 10 * 10;
        return hour * 100 + minute;
    }

    public static List<ScheduleItem> ExpandAndNormalize(
        IEnumerable<RawEvent> events,
        bool expandRanges = true,
        bool forceNoTime = true,
        bool forceNoPlace = true,
        string defaultColor = "auto")
    {
        var items = new List<ScheduleItem>();
        foreach (var ev in events)
        {
            var title = (ev.Title ?? "").Trim();
            if (string.IsNullOrEmpty(title) || ev.EventDate is null) continue;

            var start = ev.EventDate.Value;
            var end = ev.EndDate ?? ev.EventDate.Value;
            if (end < start) (start, end) = (end, start);

            var dates = expandRanges ? DateRange(start, end) : [start];
            var startTime = forceNoTime ? 0 : ParseHhmm(ev.StartTime);
            var place = forceNoPlace ? "None" : PlaceMap.Map(ev.Place);
            // 강사·부가정보 개수가 맞으면 제목(부가)로 여러 일정으로 나눔
            var variants = BracketParser.ExpandPairedSpeakerAccessory(title, ev.Speaker, ev.Notes);
            var release = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // SDM 자동 색상 규칙에 맡기도록 color·colorMode 기본값은 auto
            var colorToken = string.IsNullOrWhiteSpace(defaultColor) ? "auto" : defaultColor.Trim();
            var colorLower = colorToken.ToLowerInvariant();
            var colorMode = colorLower is "auto" or "none" ? colorLower : "custom";
            if (colorMode is "auto" or "none")
                colorToken = colorMode;

            foreach (var (itemTitle, speaker, notes) in variants)
            {
                foreach (var d in dates)
                {
                    var iso = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    // 일요일은 주일말씀에만 강사를 남긴다.
                    var itemSpeaker = speaker;
                    if (d.DayOfWeek == DayOfWeek.Sunday && !BracketParser.IsSundaySermonTitle(itemTitle))
                        itemSpeaker = "";
                    items.Add(new ScheduleItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        ScheduleDate = iso,
                        ReleaseDate = release,
                        StartTime = startTime,
                        ScheduleTitle = itemTitle,
                        AccessoryInfo = notes,
                        Place = place,
                        SermonSpeaker = itemSpeaker,
                        Preparation = [],
                        PreaparationNote = "",
                        Color = colorToken,
                        ColorMode = colorMode,
                    });
                }
            }
        }

        return items
            .OrderBy(i => i.ScheduleDate)
            .ThenBy(i => i.StartTime)
            .ThenBy(i => i.ScheduleTitle)
            .ToList();
    }

    /// <summary>
    /// 같은 제목(+호환 강사)이 날짜로 이어지거나 겹치면 한 건으로 합친다.
    /// 입력은 일별 행(Expand 결과)을 가정하고 scheduleDate만 본다.
    /// 같은 날 중복(예: 9.12~9.12 와 9.12~9.13 펼침)도 하나로 흡수한다.
    /// 결과는 scheduleDate=시작, releaseDate=끝.
    /// </summary>
    public static List<ScheduleItem> CollapseConsecutive(IEnumerable<ScheduleItem> items)
    {
        // SDM 일별 저장처럼 releaseDate가 기간 끝일 수 있어, 합칠 때는 당일(scheduleDate)만 사용
        var prepared = items
            .Where(i => !string.IsNullOrWhiteSpace(i.ScheduleTitle) && TryParseIso(i.ScheduleDate, out _))
            .Select(i =>
            {
                TryParseIso(i.ScheduleDate, out var day);
                return CloneItem(i, day, day);
            })
            .ToList();

        // 제목별로 모은 뒤, 날짜순으로 호환 강사 구간에 붙인다.
        // (전체를 날짜순만 보면 같은 날 다른 제목이 last를 가로채 연속 합침이 깨짐)
        var groups = new List<ScheduleItem>();
        foreach (var titleGroup in prepared.GroupBy(i => (i.ScheduleTitle ?? "").Trim(), StringComparer.Ordinal))
        {
            var ordered = titleGroup
                .OrderBy(i => i.ScheduleDate)
                .ThenBy(i => i.StartTime)
                .ThenBy(i => i.SermonSpeaker, StringComparer.Ordinal)
                .ToList();

            var runs = new List<ScheduleItem>();
            foreach (var item in ordered)
            {
                TryParseIso(item.ScheduleDate, out var day);
                ScheduleItem? match = null;
                foreach (var run in runs)
                {
                    if (!SpeakersCompatible(run.SermonSpeaker, item.SermonSpeaker))
                        continue;
                    if (!TryParseIso(run.ScheduleDate, out var runStart)
                        || !TryParseIso(run.ReleaseDate, out var runEnd))
                        continue;
                    // 같은 날·기간 안·바로 다음날이면 흡수
                    if (day >= runStart && day <= runEnd.AddDays(1))
                    {
                        match = run;
                        break;
                    }
                }

                if (match is null)
                {
                    runs.Add(item);
                    continue;
                }

                TryParseIso(match.ReleaseDate, out var matchEnd);
                if (day > matchEnd)
                    match.ReleaseDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                // 일요일 강사 비움 등으로 한쪽만 비어 있으면 있는 쪽을 남김
                if (string.IsNullOrWhiteSpace(match.SermonSpeaker)
                    && !string.IsNullOrWhiteSpace(item.SermonSpeaker))
                    match.SermonSpeaker = item.SermonSpeaker;
            }

            groups.AddRange(runs);
        }

        return groups
            .OrderBy(i => i.ScheduleDate)
            .ThenBy(i => i.StartTime)
            .ThenBy(i => i.ScheduleTitle, StringComparer.Ordinal)
            .ThenBy(i => i.SermonSpeaker, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 기간(scheduleDate~releaseDate)을 날짜별 1건으로 펼친다. SDM 내보내기용.
    /// </summary>
    public static List<ScheduleItem> ExpandDateRanges(IEnumerable<ScheduleItem> items)
    {
        var expanded = new List<ScheduleItem>();
        foreach (var item in items)
        {
            var title = (item.ScheduleTitle ?? "").Trim();
            if (string.IsNullOrEmpty(title) || !TryParseIso(item.ScheduleDate, out var start))
                continue;

            var end = TryParseIso(item.ReleaseDate, out var release) ? release : start;
            if (end < start) (start, end) = (end, start);
            var releaseIso = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var place = PlaceMap.Map(item.Place);
            var colorToken = string.IsNullOrWhiteSpace(item.Color) ? "auto" : item.Color.Trim();
            var colorMode = string.IsNullOrWhiteSpace(item.ColorMode)
                ? (colorToken is "auto" or "none" ? colorToken : "custom")
                : item.ColorMode.Trim();
            if (colorMode is "auto" or "none")
                colorToken = colorMode;

            foreach (var d in DateRange(start, end))
            {
                var iso = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var speaker = item.SermonSpeaker ?? "";
                if (d.DayOfWeek == DayOfWeek.Sunday && !BracketParser.IsSundaySermonTitle(title))
                    speaker = "";
                expanded.Add(new ScheduleItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ScheduleDate = iso,
                    ReleaseDate = releaseIso,
                    StartTime = item.StartTime,
                    ScheduleTitle = title,
                    AccessoryInfo = item.AccessoryInfo ?? "",
                    Place = place,
                    SermonSpeaker = speaker,
                    Preparation = item.Preparation?.ToList() ?? [],
                    PreaparationNote = item.PreaparationNote ?? "",
                    Color = colorToken,
                    ColorMode = colorMode,
                });
            }
        }

        return expanded
            .OrderBy(i => i.ScheduleDate)
            .ThenBy(i => i.StartTime)
            .ThenBy(i => i.ScheduleTitle, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 강사 호환: 둘 다 있으면 같아야 함. 한쪽만 비어 있으면(일요일 비우기 등) 이어 붙일 수 있음.
    /// </summary>
    private static bool SpeakersCompatible(string? a, string? b)
    {
        var sa = (a ?? "").Trim();
        var sb = (b ?? "").Trim();
        if (sa.Length == 0 || sb.Length == 0) return true;
        return string.Equals(sa, sb, StringComparison.Ordinal);
    }

    private static ScheduleItem CloneItem(ScheduleItem src, DateOnly start, DateOnly end) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(src.Id) ? Guid.NewGuid().ToString() : src.Id,
            ScheduleDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReleaseDate = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StartTime = src.StartTime,
            ScheduleTitle = (src.ScheduleTitle ?? "").Trim(),
            AccessoryInfo = src.AccessoryInfo ?? "",
            Place = string.IsNullOrWhiteSpace(src.Place) ? "None" : src.Place,
            SermonSpeaker = src.SermonSpeaker ?? "",
            Preparation = src.Preparation?.ToList() ?? [],
            PreaparationNote = src.PreaparationNote ?? "",
            Color = string.IsNullOrWhiteSpace(src.Color) ? "auto" : src.Color,
            ColorMode = string.IsNullOrWhiteSpace(src.ColorMode) ? "auto" : src.ColorMode,
        };

    private static bool TryParseIso(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            (value ?? "").Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static List<DateOnly> DateRange(DateOnly start, DateOnly end)
    {
        var days = new List<DateOnly>();
        for (var cur = start; cur <= end; cur = cur.AddDays(1))
            days.Add(cur);
        return days;
    }
}
