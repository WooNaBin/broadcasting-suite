using ScheduleReader.Models;
using ScheduleReader.Parser;

namespace ScheduleReader.Excel;

public static class ScheduleExtractor
{
    public static ExtractResult Extract(string xlsxPath, string sheetName)
    {
        var warnings = new List<string>();
        var inventory = WorkbookReader.ReadInventory(xlsxPath, sheetName);
        var sheet = inventory.Matrix;
        var yearMonth = YearMonthParser.FromInventory(inventory);
        if (yearMonth.IsComplete)
            BracketParser.SetContext(yearMonth.Year!.Value, yearMonth.Month!.Value);

        var yearHint = yearMonth.Year;
        var monthHint = yearMonth.Month;
        var detected = HeaderTableExtractor.DetectHeaderMap(sheet.Texts);
        var weekdayHeader = CalendarGridExtractor.FindWeekdayHeader(sheet.Texts);

        var useCalendar = false;
        if (weekdayHeader is not null && yearHint is not null && monthHint is not null)
        {
            if (detected is null)
                useCalendar = true;
            else if (detected.Value.HeaderIndex == weekdayHeader.Row)
                useCalendar = true;
        }

        var events = new List<RawEvent>();
        var meta = new Dictionary<string, object?>
        {
            ["sheet"] = sheet.Title,
            ["yearHint"] = yearHint,
            ["monthHint"] = monthHint,
            ["yearSource"] = yearMonth.Source,
            ["a1"] = inventory.GetText(1, 1),
            ["cellCount"] = inventory.Cells.Count,
        };

        Dictionary<(int Row, int Col), DateOnly>? dayMap = null;
        if (useCalendar)
        {
            var cal = CalendarGridExtractor.Extract(sheet, yearHint!.Value, monthHint!.Value);
            foreach (var kv in cal.Meta) meta[kv.Key] = kv.Value;
            warnings.AddRange(cal.Warnings);
            events = cal.Events;
            dayMap = cal.DayMap;
        }
        else if (detected is not null)
        {
            var (headerIdx, colMap) = detected.Value;
            meta["strategy"] = "header_table";
            meta["headerRow"] = headerIdx + 1;
            meta["columns"] = colMap;
            events = HeaderTableExtractor.ExtractRows(
                sheet.Values, sheet.Texts, headerIdx, colMap, yearHint, sheet.Title, warnings);
        }
        else
        {
            if (weekdayHeader is not null && (yearHint is null || monthHint is null))
            {
                warnings.Add(
                    "달력(일~토)으로 보이지만 연·월을 찾지 못했습니다. " +
                    "A1에 ‘2026년 9월 … 일정표’처럼 적어 주세요.");
            }
            else
            {
                warnings.Add(
                    "날짜·제목 헤더 표도, 일~토 달력 격자도 찾지 못했습니다. " +
                    "표 상단에 ‘날짜/제목/강사’ 열이 있거나, 요일 헤더가 있는 달력형인지 확인해 주세요.");
            }
        }

        var shapes = inventory.Shapes;
        var (shapeEvents, shapeWarnings) = ShapeExtractor.ToEvents(
            shapes, sheet, yearHint, monthHint, dayMap);
        warnings.AddRange(shapeWarnings);
        if (shapeEvents.Count > 0)
        {
            var shapeKeys = shapeEvents
                .Where(e => e.EventDate is not null && !string.IsNullOrWhiteSpace(e.Title))
                .Select(e => (e.EventDate, e.Title))
                .ToHashSet();
            events = events
                .Where(e => !shapeKeys.Contains((e.EventDate, e.Title)))
                .Concat(shapeEvents)
                .ToList();
        }

        var uniq = new Dictionary<(DateOnly?, DateOnly?, string, string, string), RawEvent>();
        foreach (var e in events)
            uniq[(e.EventDate, e.EndDate, e.Title, e.Speaker, e.Notes)] = e;
        events = uniq.Values
            .OrderBy(e => e.EventDate?.ToString("yyyy-MM-dd") ?? "")
            .ThenBy(e => e.Title)
            .ToList();

        BracketParser.ApplySundaySermonSpeaker(events);

        meta["shapeCount"] = shapes.Count;
        meta["shapeEventCount"] = shapeEvents.Count;

        var ok = events.Count > 0;
        if (!ok && detected is not null && !useCalendar)
            warnings.Add("헤더는 찾았지만 추출된 일정이 없습니다.");

        var items = Normalizer.ExpandAndNormalize(
            events,
            expandRanges: true,
            forceNoTime: false,
            forceNoPlace: false,
            defaultColor: "auto");

        meta["eventCount"] = events.Count;
        meta["itemCount"] = items.Count;

        return new ExtractResult
        {
            Ok = ok,
            Events = events,
            Items = items,
            Warnings = warnings,
            Meta = meta,
            Shapes = shapes.ToList(),
            Cells = inventory.Cells.ToList(),
        };
    }
}
