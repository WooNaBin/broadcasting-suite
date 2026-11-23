using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ScheduleReader.Models;

namespace ScheduleReader.Excel;

public static class ShapeExtractor
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Pr = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static List<ShapeTextInfo> ExtractFromXlsx(string path, string? sheetName = null)
    {
        var shapes = new List<ShapeTextInfo>();
        using var zip = ZipFile.OpenRead(path);
        var workbookEntry = zip.GetEntry("xl/workbook.xml");
        var wbRelsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || wbRelsEntry is null) return shapes;

        var sheetEntries = SheetTargetToName(ReadAll(workbookEntry));
        var wbRels = RelsMap(ReadAll(wbRelsEntry));
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rid) in sheetEntries)
        {
            if (sheetName is not null && name != sheetName) continue;
            if (!wbRels.TryGetValue(rid, out var target)) continue;
            var sheetPath = ResolvePath("xl", target);
            var sheetDir = sheetPath.Contains('/') ? sheetPath[..sheetPath.LastIndexOf('/')] : "";
            var sheetFile = Path.GetFileName(sheetPath);
            var relsPath = $"{sheetDir}/_rels/{sheetFile}.rels";
            if (!names.Contains(relsPath)) continue;

            var relsEntry = zip.GetEntry(relsPath);
            if (relsEntry is null) continue;
            var rels = RelsMap(ReadAll(relsEntry));

            foreach (var relTarget in rels.Values)
            {
                if (!relTarget.Contains("drawing", StringComparison.OrdinalIgnoreCase)
                    || !relTarget.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var drawingPath = ResolvePath(sheetDir, relTarget);
                if (!names.Contains(drawingPath) && relTarget.Contains("drawings/", StringComparison.OrdinalIgnoreCase))
                    drawingPath = "xl/drawings/" + relTarget.Replace('\\', '/').Split('/').Last();
                if (!names.Contains(drawingPath)) continue;

                var drawingEntry = zip.GetEntry(drawingPath);
                if (drawingEntry is null) continue;
                shapes.AddRange(ParseDrawingXml(ReadAll(drawingEntry), name, drawingPath));
            }
        }

        return shapes;
    }

    public static (List<RawEvent> Events, List<string> Warnings) ToEvents(
        IReadOnlyList<ShapeTextInfo> shapes,
        SheetMatrix sheet,
        int? year,
        int? month,
        IReadOnlyDictionary<(int Row, int Col), DateOnly>? dayMap = null)
    {
        var warnings = new List<string>();
        var events = new List<RawEvent>();
        if (shapes.Count == 0) return (events, warnings);
        if (year is not null && month is not null)
            BracketParser.SetContext(year.Value, month.Value);

        foreach (var shape in shapes)
        {
            var text = (shape.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var (dayInText, rest) = BracketParser.StripLeadingDay(text);
            var body = string.IsNullOrWhiteSpace(rest) ? text : rest;

            var days = new List<DateOnly>();
            if (year is not null && month is not null)
                days = DatesUnderShape(sheet, shape, year.Value, month.Value, dayMap);
            if (dayInText is not null && year is not null && month is not null)
            {
                try
                {
                    days.Add(new DateOnly(year.Value, month.Value, dayInText.Value));
                    days = days.Distinct().OrderBy(x => x).ToList();
                }
                catch
                {
                    // 달력에 없는 일
                }
            }

            var parsed = year is not null && month is not null
                ? BracketParser.ParseEntries(body, dayInText)
                : [];
            if (parsed.Count == 0 && !string.IsNullOrWhiteSpace(body))
            {
                parsed =
                [
                    new BracketParser.ParsedEntry
                    {
                        Title = body.Replace('\n', ' ').Trim(),
                    }
                ];
            }

            if (days.Count == 0 && parsed.All(e => e.StartDate is null))
            {
                var hint = parsed.FirstOrDefault()?.Title ?? body;
                warnings.Add($"도형 텍스트는 읽었지만 날짜 칸을 못 찾음: {(hint.Length > 40 ? hint[..40] : hint)}");
                continue;
            }

            var fallbackStart = days.Count > 0 ? days[0] : (DateOnly?)null;
            var fallbackEnd = days.Count > 0 ? days[^1] : fallbackStart;

            foreach (var entry in parsed)
            {
                var title = entry.Title;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var start = entry.StartDate ?? fallbackStart;
                var end = entry.EndDate ?? fallbackEnd ?? start;
                if (start is null) continue;

                events.Add(new RawEvent
                {
                    EventDate = start,
                    EndDate = end != start ? end : null,
                    Title = title,
                    Speaker = entry.Speaker,
                    Notes = entry.AccessoryInfo,
                    SourceHint = $"shape:{(!string.IsNullOrEmpty(shape.Address) ? shape.Address : shape.Name)}:{(!string.IsNullOrEmpty(shape.Name) ? shape.Name : shape.Source)}",
                    Confidence = 0.95,
                });
            }
        }

        return (events, warnings);
    }

    private static List<DateOnly> DatesUnderShape(
        SheetMatrix sheet,
        ShapeTextInfo shape,
        int year,
        int month,
        IReadOnlyDictionary<(int Row, int Col), DateOnly>? dayMap)
    {
        if (shape.FromCol is null || shape.FromRow is null) return [];
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var c0 = shape.FromCol.Value;
        var r0 = shape.FromRow.Value;
        var c1 = shape.ToCol ?? c0;
        var r1 = shape.ToRow ?? r0;
        if (c1 < c0) (c0, c1) = (c1, c0);
        if (r1 < r0) (r0, r1) = (r1, r0);

        var days = new List<DateOnly>();
        for (var r = r0; r <= r1; r++)
        {
            if (r < 0 || r >= sheet.Texts.Count) continue;
            for (var c = c0; c <= c1; c++)
            {
                if (dayMap is not null && dayMap.TryGetValue((r, c), out var mapped))
                {
                    days.Add(mapped);
                    continue;
                }

                if (c < 0 || c >= sheet.Texts[r].Count) continue;
                var text = sheet.Texts[r][c];
                var (day, _, _) = CalendarGridExtractor.ParseCellContent(text);
                if (day is not null && day.Value >= 1 && day.Value <= daysInMonth)
                {
                    try { days.Add(new DateOnly(year, month, day.Value)); }
                    catch { /* skip */ }
                    continue;
                }

                var raw = sheet.Values[r][c];
                if (raw is long or int)
                {
                    var n = Convert.ToInt32(raw);
                    if (n >= 1 && n <= daysInMonth)
                    {
                        try { days.Add(new DateOnly(year, month, n)); }
                        catch { /* skip */ }
                    }
                }
            }
        }

        if (days.Count == 0 && dayMap is not null)
        {
            for (var c = c0; c <= c1; c++)
            {
                DateOnly? found = null;
                var foundRow = -1;
                foreach (var kv in dayMap)
                {
                    if (kv.Key.Col != c || kv.Key.Row > r0) continue;
                    if (kv.Key.Row >= foundRow)
                    {
                        foundRow = kv.Key.Row;
                        found = kv.Value;
                    }
                }

                if (found is not null)
                    days.Add(found.Value);
            }
        }

        return days.Distinct().OrderBy(x => x).ToList();
    }

    private static List<ShapeTextInfo> ParseDrawingXml(byte[] xmlBytes, string sheet, string source)
    {
        XDocument doc;
        try { doc = XDocument.Load(new MemoryStream(xmlBytes)); }
        catch { return []; }

        var outList = new List<ShapeTextInfo>();
        if (doc.Root is null) return outList;
        foreach (var anchor in doc.Root.Elements())
        {
            var tag = Local(anchor.Name);
            if (tag is not ("twoCellAnchor" or "oneCellAnchor" or "absoluteAnchor"))
                continue;
            outList.AddRange(ExtractFromAnchor(anchor, sheet, source));
        }

        return outList;
    }

    private static List<ShapeTextInfo> ExtractFromAnchor(XElement anchor, string sheet, string source)
    {
        var fromEl = Child(anchor, "from");
        var toEl = Child(anchor, "to");
        var (fromCol, fromRow) = AnchorCell(fromEl);
        var (toCol, toRow) = AnchorCell(toEl);
        toCol ??= fromCol;
        toRow ??= fromRow;

        var results = new List<ShapeTextInfo>();
        foreach (var child in anchor.Elements())
        {
            var local = Local(child.Name);
            if (local == "sp")
            {
                var tx = Child(child, "txBody");
                if (tx is null) continue;
                var text = TxBodyText(tx);
                if (string.IsNullOrWhiteSpace(text)) continue;
                results.Add(MakeShape(text, ShapeName(child), fromCol, fromRow, toCol, toRow, sheet, source));
            }
            else if (local == "grpSp")
            {
                foreach (var nested in child.Descendants().Where(e => Local(e.Name) == "sp"))
                {
                    var tx = Child(nested, "txBody");
                    if (tx is null) continue;
                    var text = TxBodyText(tx);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    results.Add(MakeShape(text, ShapeName(nested), fromCol, fromRow, toCol, toRow, sheet, source));
                }
            }
        }

        return results;
    }

    private static ShapeTextInfo MakeShape(
        string text,
        string name,
        int? fromCol,
        int? fromRow,
        int? toCol,
        int? toRow,
        string sheet,
        string source) =>
        new()
        {
            Text = text,
            Name = name,
            Address = fromCol is int fc && fromRow is int fr
                ? ExcelAddress.From(fr + 1, fc + 1)
                : "",
            FromCol = fromCol,
            FromRow = fromRow,
            ToCol = toCol,
            ToRow = toRow,
            Sheet = sheet,
            Source = source,
        };

    private static string TxBodyText(XElement txBody)
    {
        var paragraphs = new List<string>();
        foreach (var p in txBody.Elements().Where(e => Local(e.Name) == "p"))
        {
            var parts = p.Descendants()
                .Where(n => Local(n.Name) == "t")
                .Select(n => n.Value);
            paragraphs.Add(string.Concat(parts));
        }

        var text = string.Join('\n', paragraphs).Trim();
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        return text.Trim();
    }

    private static string ShapeName(XElement sp)
    {
        var nv = Child(sp, "nvSpPr");
        var cnv = nv is null ? null : Child(nv, "cNvPr");
        return cnv?.Attribute("name")?.Value?.Trim() ?? "";
    }

    private static (int? Col, int? Row) AnchorCell(XElement? marker)
    {
        if (marker is null) return (null, null);
        var colEl = Child(marker, "col");
        var rowEl = Child(marker, "row");
        int? col = colEl is not null && int.TryParse(colEl.Value, out var c) ? c : null;
        int? row = rowEl is not null && int.TryParse(rowEl.Value, out var r) ? r : null;
        return (col, row);
    }

    private static List<(string Name, string Rid)> SheetTargetToName(byte[] workbookXml)
    {
        var doc = XDocument.Load(new MemoryStream(workbookXml));
        var sheets = new List<(string, string)>();
        foreach (var el in doc.Descendants().Where(e => Local(e.Name) == "sheet"))
        {
            var name = el.Attribute("name")?.Value ?? "";
            var rid = el.Attribute(R + "id")?.Value ?? el.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(rid))
                sheets.Add((name, rid));
        }

        return sheets;
    }

    private static Dictionary<string, string> RelsMap(byte[] relsXml)
    {
        var doc = XDocument.Load(new MemoryStream(relsXml));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var el in doc.Descendants().Where(e => Local(e.Name) == "Relationship"))
        {
            var id = el.Attribute("Id")?.Value;
            var target = el.Attribute("Target")?.Value?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                map[id] = target;
        }

        return map;
    }

    private static string ResolvePath(string baseDir, string target)
    {
        target = target.Replace('\\', '/');
        if (target.StartsWith('/')) return target.TrimStart('/');
        var parts = string.IsNullOrEmpty(baseDir)
            ? new List<string>()
            : baseDir.Trim('/').Split('/').ToList();
        foreach (var piece in target.Split('/'))
        {
            if (piece is "" or ".") continue;
            if (piece == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(piece);
        }

        return string.Join('/', parts);
    }

    private static XElement? Child(XElement el, string localName) =>
        el.Elements().FirstOrDefault(c => Local(c.Name) == localName);

    private static string Local(XName name) => name.LocalName;

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
