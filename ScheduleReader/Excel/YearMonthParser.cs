using System.Globalization;
using System.Text.RegularExpressions;
using ScheduleReader.Models;

namespace ScheduleReader.Excel;

public readonly record struct YearMonthHint(int? Year, int? Month, string Source)
{
    public bool IsComplete => Year is >= 2000 and <= 2099 && Month is >= 1 and <= 12;
}

public static class YearMonthParser
{
    private static readonly Regex YearMonthRe = new(
        @"(20\d{2})\s*년\s*(\d{1,2})\s*월",
        RegexOptions.Compiled);

    public static YearMonthHint FromText(string? text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(null, null, source);
        var m = YearMonthRe.Match(text);
        if (!m.Success) return new(null, null, source);
        var year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return new(year, null, source);
        return new(year, month, source);
    }

    /// <summary>
    /// 연·월은 A1 제목("2026년 9월 광주교회 일정표")을 우선한다.
    /// A1에 없으면 시트 이름, 그다음 상단 몇 칸을 본다.
    /// </summary>
    public static YearMonthHint FromSheet(SheetMatrix sheet)
    {
        var a1 = sheet.Texts.Count > 0 && sheet.Texts[0].Count > 0
            ? sheet.Texts[0][0]
            : "";
        var fromA1 = FromText(a1, "A1");
        if (fromA1.IsComplete) return fromA1;

        var fromTitle = FromText(sheet.Title, "sheetName");
        if (fromTitle.IsComplete) return fromTitle;

        var bits = new List<string>();
        for (var r = 0; r < Math.Min(5, sheet.Texts.Count); r++)
        {
            for (var c = 0; c < Math.Min(8, sheet.Texts[r].Count); c++)
            {
                var t = sheet.Texts[r][c];
                if (!string.IsNullOrWhiteSpace(t)) bits.Add(t);
            }
        }

        var fromScan = FromText(string.Join(' ', bits), "headerScan");
        if (fromScan.IsComplete) return fromScan;

        return new(fromA1.Year ?? fromTitle.Year, fromA1.Month ?? fromTitle.Month, "");
    }

    public static YearMonthHint FromInventory(WorkbookInventory inventory) =>
        FromSheet(inventory.Matrix);
}
