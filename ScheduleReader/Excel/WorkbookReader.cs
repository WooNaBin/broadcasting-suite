using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleReader.Models;

namespace ScheduleReader.Excel;

public static class CellText
{
    public static string From(object? value)
    {
        if (value is null) return "";
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value is DateOnly d) return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value is TimeOnly t) return t.ToString("HH:mm", CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    }
}

/// <summary>Read worksheet cells into a dense string matrix (1-based Excel → 0-based lists).</summary>
public sealed class SheetMatrix
{
    public required string Title { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> Texts { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Values { get; init; }
    public IReadOnlyList<CellInfo> Cells { get; init; } = [];
    public int RowCount => Texts.Count;
    public int ColCount => Texts.Count == 0 ? 0 : Texts.Max(r => r.Count);

    public string GetText(int row, int column)
    {
        var r = row - 1;
        var c = column - 1;
        if (r < 0 || r >= Texts.Count) return "";
        var line = Texts[r];
        if (c < 0 || c >= line.Count) return "";
        return line[c] ?? "";
    }
}

/// <summary>한 시트의 셀(값·주소)과 도형 문자열.</summary>
public sealed class WorkbookInventory
{
    public required string SheetName { get; init; }
    public required SheetMatrix Matrix { get; init; }
    public required IReadOnlyList<CellInfo> Cells { get; init; }
    public required IReadOnlyList<ShapeTextInfo> Shapes { get; init; }

    public CellInfo? FindCell(string address) =>
        Cells.FirstOrDefault(c =>
            string.Equals(c.Address, address, StringComparison.OrdinalIgnoreCase));

    public string GetText(int row, int column) => Matrix.GetText(row, column);
}

public static class WorkbookReader
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void ValidateWorkbookBytes(string filename, ReadOnlySpan<byte> content)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xlsm"))
            throw new ArgumentException("엑셀 파일(.xlsx / .xlsm)만 지원합니다. (.xls 는 Excel에서 xlsx로 저장해 주세요.)");

        if (content.Length < 4)
            throw new ArgumentException("빈 파일이거나 손상된 파일입니다.");

        if (content[0] == 0xD0 && content[1] == 0xCF && content[2] == 0x11 && content[3] == 0xE0)
            throw new ArgumentException("구형 .xls(바이너리) 형식입니다. Excel에서 ‘다른 이름으로 저장’ → .xlsx 로 저장한 뒤 다시 불러오세요.");

        if (content[0] != (byte)'P' || content[1] != (byte)'K')
            throw new ArgumentException("엑셀(.xlsx) ZIP 형식이 아닙니다. 암호가 걸려 있거나 다른 프로그램 파일일 수 있습니다.");
    }

    /// <summary>List worksheet names via ZIP/XML only — does not load every sheet DOM.</summary>
    public static IReadOnlyList<string> ListSheetNames(string path)
    {
        try
        {
            return ListSheetNamesFromZip(path);
        }
        catch
        {
            // Fallback for unusual packages
            using var doc = Open(path);
            return ListWorksheetNamesViaSdk(doc).ToList();
        }
    }

    public static SheetMatrix ReadSheet(string path, string sheetName, int maxRows = 500, int maxCols = 40)
    {
        using var doc = Open(path);
        var workbookPart = doc.WorkbookPart
            ?? throw new InvalidOperationException("워크북을 열 수 없습니다.");

        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>()
            .FirstOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.Ordinal));
        if (sheet?.Id?.Value is null)
            throw new InvalidOperationException($"시트 없음: {sheetName}");

        OpenXmlPart part;
        try
        {
            part = workbookPart.GetPartById(sheet.Id.Value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"시트「{sheetName}」관계를 열 수 없습니다: {ex.Message}", ex);
        }

        if (part is not WorksheetPart worksheetPart)
            throw new InvalidOperationException(
                $"「{sheetName}」은 일반 워크시트가 아닙니다(차트/매크로 시트 등). 다른 시트를 선택하세요.");

        var sharedStrings = LoadSharedStrings(workbookPart);
        var dateStyleFlags = BuildDateStyleFlags(workbookPart.WorkbookStylesPart?.Stylesheet);

        var sparse = new Dictionary<(int Row, int Col), (string Text, object? Raw)>();
        var maxRowSeen = 0;
        var maxColSeen = 0;

        // Stream rows — avoid loading entire worksheet DOM (multi-sheet calendars hang otherwise)
        using (var reader = OpenXmlReader.Create(worksheetPart))
        {
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
                    continue;

                var row = (Row)reader.LoadCurrentElement()!;
                var rowIndex = row.RowIndex?.Value is uint ri ? (int)ri : 0;
                if (rowIndex <= 0) continue;
                if (rowIndex > maxRows)
                {
                    // Rows are normally ascending; stop early for preview/extract caps
                    break;
                }

                var colCursor = 1;
                foreach (var cell in row.Elements<Cell>())
                {
                    var col = GetColumnIndex(cell.CellReference?.Value);
                    if (col <= 0) col = colCursor;
                    colCursor = col + 1;
                    if (col > maxCols) continue;

                    var parsed = ReadCell(cell, sharedStrings, dateStyleFlags);
                    if (string.IsNullOrWhiteSpace(parsed.Text) && parsed.Raw is null)
                        continue;

                    sparse[(rowIndex, col)] = parsed;
                    if (rowIndex > maxRowSeen) maxRowSeen = rowIndex;
                    if (col > maxColSeen) maxColSeen = col;
                }
            }
        }

        var texts = new List<List<string>>();
        var values = new List<List<object?>>();
        var lastCol = Math.Min(maxCols, Math.Max(maxColSeen, 1));

        for (var r = 1; r <= Math.Min(maxRows, Math.Max(maxRowSeen, 0)); r++)
        {
            var textRow = new List<string>(lastCol);
            var valueRow = new List<object?>(lastCol);
            for (var c = 1; c <= lastCol; c++)
            {
                if (sparse.TryGetValue((r, c), out var cell))
                {
                    textRow.Add(cell.Text);
                    valueRow.Add(cell.Raw);
                }
                else
                {
                    textRow.Add("");
                    valueRow.Add(null);
                }
            }

            texts.Add(textRow);
            values.Add(valueRow);
        }

        while (texts.Count > 0 && texts[^1].All(string.IsNullOrWhiteSpace))
        {
            texts.RemoveAt(texts.Count - 1);
            values.RemoveAt(values.Count - 1);
        }

        var cells = sparse
            .OrderBy(kv => kv.Key.Row)
            .ThenBy(kv => kv.Key.Col)
            .Select(kv => new CellInfo
            {
                Address = ExcelAddress.From(kv.Key.Row, kv.Key.Col),
                Row = kv.Key.Row,
                Column = kv.Key.Col,
                Text = kv.Value.Text,
                RawValue = kv.Value.Raw,
            })
            .ToList();

        return new SheetMatrix
        {
            Title = sheetName,
            Texts = texts,
            Values = values,
            Cells = cells,
        };
    }

    /// <summary>셀 값·주소와 도형 안 문자열을 한 번에 읽는다.</summary>
    public static WorkbookInventory ReadInventory(string path, string sheetName, int maxRows = 500, int maxCols = 40)
    {
        var matrix = ReadSheet(path, sheetName, maxRows, maxCols);
        var shapes = ShapeExtractor.ExtractFromXlsx(path, sheetName);
        return new WorkbookInventory
        {
            SheetName = matrix.Title,
            Matrix = matrix,
            Cells = matrix.Cells,
            Shapes = shapes,
        };
    }

    public static Dictionary<string, object?> Preview(SheetMatrix sheet, int maxRows = 30, int maxCols = 20)
    {
        var rows = new List<List<string>>();
        for (var i = 0; i < Math.Min(maxRows, sheet.Texts.Count); i++)
        {
            var src = sheet.Texts[i];
            var cells = new List<string>();
            for (var c = 0; c < Math.Min(maxCols, src.Count); c++)
                cells.Add(src[c]);
            if (cells.Any(x => !string.IsNullOrWhiteSpace(x)))
                rows.Add(cells);
        }

        return new Dictionary<string, object?>
        {
            ["sheet"] = sheet.Title,
            ["rowCountHint"] = sheet.RowCount,
            ["colCountHint"] = sheet.ColCount,
            ["rows"] = rows,
        };
    }

    private static List<string> ListSheetNamesFromZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var wbEntry = zip.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("xl/workbook.xml 없음");
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("workbook.xml.rels 없음");

        XDocument wbDoc;
        XDocument relsDoc;
        using (var s = wbEntry.Open()) wbDoc = XDocument.Load(s);
        using (var s = relsEntry.Open()) relsDoc = XDocument.Load(s);

        var rels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in relsDoc.Descendants(PkgRelNs + "Relationship"))
        {
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                rels[id] = target;
        }

        var names = new List<string>();
        foreach (var sheet in wbDoc.Descendants(MainNs + "sheet"))
        {
            var name = sheet.Attribute("name")?.Value;
            var rid = sheet.Attribute(RelNs + "id")?.Value ?? sheet.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(rid))
                continue;
            if (!rels.TryGetValue(rid, out var target))
                continue;

            // worksheets only (skip chartsheets, dialogsheets, …)
            var t = target.ToLowerInvariant();
            if (!t.Contains("worksheet"))
                continue;

            var state = sheet.Attribute("state")?.Value;
            if (string.Equals(state, "veryHidden", StringComparison.OrdinalIgnoreCase))
                continue;

            names.Add(name);
        }

        if (names.Count == 0)
            throw new InvalidOperationException("읽을 수 있는 워크시트가 없습니다.");

        return names;
    }

    private static SpreadsheetDocument Open(string path)
    {
        try
        {
            // Default open — ProcessAllParts on multi-sheet files with drawings can hang/OOM
            return SpreadsheetDocument.Open(path, isEditable: false);
        }
        catch (OpenXmlPackageException ex)
        {
            throw new InvalidOperationException(
                "엑셀 패키지를 열 수 없습니다. 암호 보호·손상 파일일 수 있습니다. " + ex.Message, ex);
        }
        catch (FileFormatException ex)
        {
            throw new InvalidOperationException(
                "엑셀 형식이 올바르지 않습니다. .xlsx/.xlsm 인지 확인해 주세요. " + ex.Message, ex);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                "압축(ZIP)이 깨졌거나 암호가 걸린 파일일 수 있습니다. " + ex.Message, ex);
        }
    }

    private static IEnumerable<string> ListWorksheetNamesViaSdk(SpreadsheetDocument doc)
    {
        var workbookPart = doc.WorkbookPart
            ?? throw new InvalidOperationException("워크북을 열 수 없습니다.");
        var names = new List<string>();
        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            var name = sheet.Name?.Value;
            if (string.IsNullOrWhiteSpace(name) || sheet.Id?.Value is null)
                continue;
            try
            {
                if (workbookPart.GetPartById(sheet.Id.Value) is WorksheetPart)
                    names.Add(name);
            }
            catch
            {
                // skip broken relationships
            }
        }

        return names;
    }

    private static string[] LoadSharedStrings(WorkbookPart workbookPart)
    {
        var sstPart = workbookPart.SharedStringTablePart;
        if (sstPart is null) return [];

        // Stream SST — full DOM load is slow on large multi-sheet workbooks
        var list = new List<string>();
        using var reader = OpenXmlReader.Create(sstPart);
        while (reader.Read())
        {
            if (reader.ElementType != typeof(SharedStringItem) || !reader.IsStartElement)
                continue;
            var item = (SharedStringItem)reader.LoadCurrentElement()!;
            if (item.Text?.Text is { } t)
                list.Add(t);
            else
                list.Add(string.Concat(item.Descendants<Text>().Select(x => x.Text)));
        }

        return list.ToArray();
    }

    private static bool[] BuildDateStyleFlags(Stylesheet? styles)
    {
        if (styles?.CellFormats is null) return [];
        var formats = styles.CellFormats.Elements<CellFormat>().ToList();
        var flags = new bool[formats.Count];
        Dictionary<uint, string>? custom = null;
        if (styles.NumberingFormats is not null)
        {
            custom = styles.NumberingFormats.Elements<NumberingFormat>()
                .Where(n => n.NumberFormatId?.Value is not null)
                .GroupBy(n => n.NumberFormatId!.Value)
                .ToDictionary(g => g.Key, g => g.First().FormatCode?.Value ?? "");
        }

        for (var i = 0; i < formats.Count; i++)
        {
            var numFmtId = formats[i].NumberFormatId?.Value ?? 0;
            if (numFmtId is >= 14 and <= 22 or >= 45 and <= 47)
            {
                flags[i] = true;
                continue;
            }

            if (custom is null || !custom.TryGetValue(numFmtId, out var code) || code.Length == 0)
                continue;

            flags[i] = code.Contains('y', StringComparison.OrdinalIgnoreCase)
                || code.Contains('d', StringComparison.OrdinalIgnoreCase)
                || (code.Contains('m', StringComparison.OrdinalIgnoreCase)
                    && !code.Contains('h', StringComparison.OrdinalIgnoreCase));
        }

        return flags;
    }

    private static (string Text, object? Raw) ReadCell(
        Cell cell,
        string[] sharedStrings,
        bool[] dateStyleFlags)
    {
        var dataType = cell.DataType?.Value;
        var rawText = cell.CellValue?.InnerText;

        if (dataType == CellValues.SharedString && int.TryParse(rawText, out var sstIndex))
        {
            if (sstIndex < 0 || sstIndex >= sharedStrings.Length) return ("", null);
            var text = sharedStrings[sstIndex].Trim();
            return (text, text);
        }

        if (dataType == CellValues.InlineString)
        {
            var text = cell.InlineString?.Text?.Text
                ?? string.Concat(cell.InlineString?.Descendants<Text>().Select(t => t.Text) ?? []);
            text = text.Trim();
            return (text, text);
        }

        if (dataType == CellValues.Boolean)
        {
            var b = rawText == "1" || string.Equals(rawText, "true", StringComparison.OrdinalIgnoreCase);
            return (b ? "TRUE" : "FALSE", b);
        }

        if (string.IsNullOrEmpty(rawText))
        {
            var inline = cell.InlineString;
            if (inline is not null)
            {
                var text = string.Concat(inline.Descendants<Text>().Select(t => t.Text)).Trim();
                return (text, text);
            }

            return ("", null);
        }

        if (double.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var styleIdx = cell.StyleIndex?.Value;
            var isDate = styleIdx is not null
                && styleIdx.Value < (uint)dateStyleFlags.Length
                && dateStyleFlags[(int)styleIdx.Value];

            if (isDate && number >= 1 && number < 1000000)
            {
                try
                {
                    var dt = DateTime.FromOADate(number);
                    if (number == Math.Truncate(number))
                        return (dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.FromDateTime(dt));
                    return (dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), dt);
                }
                catch
                {
                    // fall through
                }
            }

            if (number == Math.Truncate(number) && Math.Abs(number) < 1e15)
                return (Convert.ToInt64(number).ToString(CultureInfo.InvariantCulture), Convert.ToInt64(number));
            return (number.ToString(CultureInfo.InvariantCulture), number);
        }

        return (rawText.Trim(), rawText);
    }

    private static int GetColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        var col = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return col;
    }
}
