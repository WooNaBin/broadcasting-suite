using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using ScheduleReader.Excel;
using Xunit;

namespace ScheduleReader.Tests;

public class ExtractTests
{
    private static string SaveTemp(XLWorkbook wb, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sr_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void Header_table_extract()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("9월");
        ws.Cell(1, 1).Value = "2026년 9월";
        ws.Cell(2, 1).Value = "날짜";
        ws.Cell(2, 2).Value = "제목";
        ws.Cell(2, 3).Value = "강사";
        ws.Cell(2, 4).Value = "시간";
        ws.Cell(3, 1).Value = "2026-09-07";
        ws.Cell(3, 2).Value = "주일말씀";
        ws.Cell(3, 3).Value = "홍길동";
        ws.Cell(3, 4).Value = "10:30";
        ws.Cell(4, 1).Value = "9/10";
        ws.Cell(4, 2).Value = "수요예배";
        ws.Cell(4, 3).Value = "김철수";
        ws.Cell(4, 4).Value = "19:30";

        var path = SaveTemp(wb, "header.xlsx");
        var result = ScheduleExtractor.Extract(path, "9월");
        Assert.True(result.Ok);
        Assert.Equal("header_table", result.Meta["strategy"]?.ToString());
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("주일말씀", result.Items[0].ScheduleTitle);
        Assert.Equal("홍길동", result.Items[0].SermonSpeaker);
        Assert.Equal(1030, result.Items[0].StartTime);
    }

    [Fact]
    public void Calendar_grid_extract()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("2026년 9월");
        var headers = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var i = 0; i < 7; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(2, 3).Value = "1\n개학예배\n홍길동";
        ws.Cell(2, 4).Value = "2";
        ws.Cell(2, 5).Value = "3";
        ws.Cell(2, 6).Value = "4";
        ws.Cell(2, 7).Value = "5";
        ws.Cell(3, 1).Value = "6\n주일말씀\n(김철수)";
        ws.Cell(3, 5).Value = "10\n수요예배";

        var path = SaveTemp(wb, "calendar.xlsx");
        var result = ScheduleExtractor.Extract(path, "2026년 9월");
        Assert.True(result.Ok);
        Assert.Equal("calendar_grid", result.Meta["strategy"]?.ToString());
        var titles = result.Items.ToDictionary(i => i.ScheduleTitle, i => i);
        Assert.True(titles.ContainsKey("개학예배"));
        Assert.Equal("2026-09-01", titles["개학예배"].ScheduleDate);
        Assert.Equal("홍길동", titles["개학예배"].SermonSpeaker);
        Assert.True(titles.ContainsKey("주일말씀"));
        Assert.Equal("2026-09-06", titles["주일말씀"].ScheduleDate);
        Assert.Equal("", titles["주일말씀"].SermonSpeaker);
        Assert.Equal("김철수", titles["주일말씀"].AccessoryInfo);
    }

    [Fact]
    public void Multi_sheet_workbook_lists_and_reads()
    {
        using var wb = new XLWorkbook();
        for (var m = 1; m <= 12; m++)
        {
            var ws = wb.AddWorksheet($"2026년 {m}월");
            ws.Cell(1, 1).Value = $"2026년 {m}월";
            ws.Cell(2, 1).Value = "일";
            ws.Cell(2, 2).Value = "월";
            ws.Cell(2, 3).Value = "화";
            ws.Cell(2, 4).Value = "수";
            ws.Cell(2, 5).Value = "목";
            ws.Cell(2, 6).Value = "금";
            ws.Cell(2, 7).Value = "토";
            ws.Cell(3, 1).Value = "1\n예배";
        }

        var path = SaveTemp(wb, "multi.xlsx");
        var names = WorkbookReader.ListSheetNames(path);
        Assert.Equal(12, names.Count);
        var preview = WorkbookReader.ReadSheet(path, names[0], maxRows: 40, maxCols: 20);
        Assert.True(preview.RowCount > 0);
        var result = ScheduleExtractor.Extract(path, names[5]);
        Assert.True(result.Ok);
    }

    [Fact]
    public void Inventory_exposes_cell_value_and_address()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("9월");
        ws.Cell(1, 1).Value = "         2026년 9월 광주교회 일정표";
        ws.Cell(2, 3).Value = "개학예배";

        var path = SaveTemp(wb, "inventory.xlsx");
        var inventory = WorkbookReader.ReadInventory(path, "9월");
        var a1 = inventory.FindCell("A1");
        Assert.NotNull(a1);
        Assert.Equal(1, a1!.Row);
        Assert.Equal(1, a1.Column);
        Assert.Contains("2026년 9월", a1.Text);
        Assert.Equal("A1", a1.Address);

        var c2 = inventory.FindCell("C2");
        Assert.NotNull(c2);
        Assert.Equal("개학예배", c2!.Text);
        Assert.Equal(2, c2.Row);
        Assert.Equal(3, c2.Column);
    }

    [Fact]
    public void Year_month_comes_from_A1_title()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("일정");
        ws.Cell(1, 1).Value = "         2026년 9월 광주교회 일정표";
        var headers = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var i = 0; i < 7; i++) ws.Cell(2, i + 1).Value = headers[i];
        ws.Cell(3, 3).Value = "1";
        ws.Cell(4, 3).Value = "개학예배\n홍길동";
        ws.Cell(3, 4).Value = "2";
        ws.Cell(3, 5).Value = "3";
        ws.Cell(3, 6).Value = "4";
        ws.Cell(3, 7).Value = "5";
        ws.Cell(5, 1).Value = "6\n주일말씀\n(김철수)";

        var path = SaveTemp(wb, "a1-calendar.xlsx");
        var result = ScheduleExtractor.Extract(path, "일정");
        Assert.True(result.Ok);
        Assert.Equal("A1", result.Meta["yearSource"]?.ToString());
        Assert.Equal(2026, Convert.ToInt32(result.Meta["yearHint"]));
        Assert.Equal(9, Convert.ToInt32(result.Meta["monthHint"]));
        Assert.Equal("calendar_grid", result.Meta["strategy"]?.ToString());

        var titles = result.Items.ToDictionary(i => i.ScheduleTitle, i => i);
        Assert.Equal("2026-09-01", titles["개학예배"].ScheduleDate);
        Assert.Equal("홍길동", titles["개학예배"].SermonSpeaker);
        Assert.Equal("2026-09-06", titles["주일말씀"].ScheduleDate);
        Assert.Equal("", titles["주일말씀"].SermonSpeaker);
        Assert.Equal("김철수", titles["주일말씀"].AccessoryInfo);
    }

    [Fact]
    public void Shape_over_day_cell_becomes_schedule()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("2026년 9월");
        var headers = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var i = 0; i < 7; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(2, 3).Value = "1";
        ws.Cell(2, 4).Value = "2";

        var path = SaveTemp(wb, "shape-cal.xlsx");
        var sheet = WorkbookReader.ReadSheet(path, "2026년 9월");
        BracketParser.SetContext(2026, 9);
        var dayMap = CalendarGridExtractor.BuildDayMap(sheet, 0, 0, 2026, 9);
        var shapes = new List<ScheduleReader.Models.ShapeTextInfo>
        {
            new()
            {
                Text = "개학예배\n홍길동",
                Name = "Rectangle 1",
                Address = "C2",
                FromCol = 2,
                FromRow = 1,
                ToCol = 2,
                ToRow = 1,
                Sheet = "2026년 9월",
            },
        };

        var (events, warnings) = ShapeExtractor.ToEvents(shapes, sheet, 2026, 9, dayMap);
        Assert.Empty(warnings);
        Assert.Single(events);
        Assert.Equal("개학예배", events[0].Title);
        Assert.Equal("홍길동", events[0].Speaker);
        Assert.Equal(new DateOnly(2026, 9, 1), events[0].EventDate);
    }

    [Fact]
    public void YearMonthParser_reads_padded_A1_title()
    {
        var hint = YearMonthParser.FromText("         2026년 9월 광주교회 일정표", "A1");
        Assert.True(hint.IsComplete);
        Assert.Equal(2026, hint.Year);
        Assert.Equal(9, hint.Month);
        Assert.Equal("A1", hint.Source);
    }

    [Fact]
    public void Calendar_gap_column_and_md_dates()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("9월");
        ws.Cell(1, 1).Value = "2026년 9월 광주교회 일정표";
        ws.Cell(2, 1).Value = "일";
        ws.Cell(2, 2).Value = "월";
        ws.Cell(2, 4).Value = "화";
        ws.Cell(2, 5).Value = "수";
        ws.Cell(2, 6).Value = "목";
        ws.Cell(2, 7).Value = "금";
        ws.Cell(2, 8).Value = "토";
        ws.Cell(3, 1).Value = "8/30";
        ws.Cell(3, 2).Value = "8/31";
        ws.Cell(3, 4).Value = "1";
        ws.Cell(3, 5).Value = "2";
        ws.Cell(3, 6).Value = "3";
        ws.Cell(3, 7).Value = "4";
        ws.Cell(3, 8).Value = "5";
        ws.Cell(4, 1).Value = "주일말씀";
        ws.Cell(5, 1).Value = "(김연숙P:서울노원)";
        ws.Cell(4, 4).Value = "은장회모임";
        ws.Cell(19, 1).Value = "6";
        ws.Cell(20, 1).Value = "주일말씀";
        ws.Cell(20, 3).Value = "문경(7-13):박경수P";

        var path = SaveTemp(wb, "gap-calendar.xlsx");
        var result = ScheduleExtractor.Extract(path, "9월");
        Assert.True(result.Ok, string.Join(" | ", result.Warnings));
        Assert.Equal("calendar_grid", result.Meta["strategy"]?.ToString());

        var sun = result.Events.First(e => e.Title == "주일말씀" && e.EventDate == new DateOnly(2026, 8, 30));
        Assert.Contains("김연숙PA", sun.Speaker);
        Assert.Equal("서울노원", sun.Notes);
        Assert.Contains(result.Events, e => e.Title == "은장회모임" && e.EventDate == new DateOnly(2026, 9, 1));
        Assert.Contains(result.Events, e => e.Title == "문경" && e.EventDate == new DateOnly(2026, 9, 7));
        Assert.Contains(result.Events, e => e.Title == "주일말씀" && e.EventDate == new DateOnly(2026, 9, 6));
    }

    [Fact]
    public void Realistic_guide_strings_and_shape()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("9월");
        ws.Cell(1, 1).Value = "         2026년 9월 광주교회 일정표";
        var headers = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var i = 0; i < 7; i++)
            ws.Cell(2, i + 1).Value = headers[i];

        // 2026-09-01 = 화요일
        Put(ws, 3, 3, "1\n개학예배\n홍길동");
        Put(ws, 3, 4, "2");
        Put(ws, 3, 5, "3");
        Put(ws, 3, 6, "4\n교구집회(8.31-9.4) : 매곡6(이정익P),첨단2(이기천P),용봉3(김선욱P),두암(양현준P)");
        Put(ws, 3, 7, "5\n초등부집회(8.7-9) : 이주현P");

        Put(ws, 4, 1, "6\n주일말씀\n(김철수)");
        Put(ws, 4, 2, "7\n청년회집회(7-12):정진관P  /  담양고서(7-11):이태훈P  /  직장선교부_서부유통&광일(7-11):박종욱P");
        Put(ws, 4, 3, "8");
        Put(ws, 4, 4, "9");
        Put(ws, 4, 5, """
            10
            담양고서집회(박경수P)
            교구집회:동림(강대석P).삼각1(김도형P),매곡5(이주현P),신용3(권오진P)
            """);
        Put(ws, 4, 6, "11");
        Put(ws, 4, 7, "12");

        Put(ws, 5, 1, "13");
        Put(ws, 5, 2, "14");
        Put(ws, 5, 3, "15\n대전도집회(5.11-17) : 임승환P      /    초등부(김규석E, 성재경E, 송경근E)");
        Put(ws, 5, 4, "16");
        Put(ws, 5, 5, "17\n추계수양회(17-20)");
        Put(ws, 5, 6, "18");
        Put(ws, 5, 7, "19");

        for (var d = 20; d <= 30; d++)
        {
            var offset = d - 20;
            var col = (offset % 7) + 1;
            var row = 6 + offset / 7;
            Put(ws, row, col, d.ToString());
        }

        var path = SaveTemp(wb, "realistic-2026-09.xlsx");
        AttachTextShape(path, fromCol0: 3, fromRow0: 2, name: "Rectangle 1", text: "특새\n이정익P");

        var result = ScheduleExtractor.Extract(path, "9월");
        DumpSample(path, result);

        Assert.True(result.Ok, string.Join(" | ", result.Warnings));
        Assert.Equal("calendar_grid", result.Meta["strategy"]?.ToString());
        Assert.Equal("A1", result.Meta["yearSource"]?.ToString());
        Assert.Equal(2026, Convert.ToInt32(result.Meta["yearHint"]));
        Assert.Equal(9, Convert.ToInt32(result.Meta["monthHint"]));
        Assert.True(result.Shapes.Count >= 1);
        Assert.Contains(result.Shapes, s => s.Text.Contains("특새"));

        var byTitle = result.Events.ToLookup(e => e.Title);
        Assert.Equal(new DateOnly(2026, 9, 1), byTitle["개학예배"].Single().EventDate);
        Assert.Equal("홍길동", byTitle["개학예배"].Single().Speaker);

        Assert.Equal(new DateOnly(2026, 9, 2), byTitle["특새"].Single().EventDate);
        Assert.Equal("이정익PA", byTitle["특새"].Single().Speaker);

        Assert.Equal(new DateOnly(2026, 9, 6), byTitle["주일말씀"].Single().EventDate);
        Assert.Equal("", byTitle["주일말씀"].Single().Speaker);
        Assert.Equal("김철수", byTitle["주일말씀"].Single().Notes);

        var youth = byTitle["청년회집회"].Single();
        Assert.Equal(new DateOnly(2026, 9, 7), youth.EventDate);
        Assert.Equal(new DateOnly(2026, 9, 12), youth.EndDate);
        Assert.Equal("정진관PA", youth.Speaker);

        var camp = byTitle["추계수양회"].Single();
        Assert.Equal(new DateOnly(2026, 9, 17), camp.EventDate);
        Assert.Equal(new DateOnly(2026, 9, 20), camp.EndDate);

        var district = byTitle["교구집회"].First(e => e.EventDate == new DateOnly(2026, 8, 31));
        Assert.Equal(new DateOnly(2026, 9, 4), district.EndDate);
        Assert.Contains("이정익PA", district.Speaker);
        Assert.Contains("매곡6", district.Notes);

        var elem = byTitle["초등부집회"].Single();
        Assert.Equal(new DateOnly(2026, 8, 7), elem.EventDate);
        Assert.Equal(new DateOnly(2026, 8, 9), elem.EndDate);
        Assert.Equal("이주현PA", elem.Speaker);

        var twoLineDistrict = byTitle["교구집회"].First(e => e.EventDate == new DateOnly(2026, 9, 10));
        Assert.Contains("동림", twoLineDistrict.Notes);
        Assert.Contains("강대석PA", twoLineDistrict.Speaker);
    }

    [Fact]
    public void Sunday_extract_speaker_only_on_sermon_title()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("9월");
        ws.Cell(1, 1).Value = "2026년 9월 광주교회 일정표";
        var headers = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var i = 0; i < 7; i++)
            ws.Cell(2, i + 1).Value = headers[i];
        Put(ws, 3, 3, "1");
        Put(ws, 3, 4, "2");
        Put(ws, 3, 5, "3");
        Put(ws, 3, 6, "4");
        Put(ws, 3, 7, "5");
        Put(ws, 4, 1, """
            6
            주일말씀
            구역형제모임
            (홍길동P:서울)
            """);
        Put(ws, 4, 2, "7");

        var path = SaveTemp(wb, "sunday-sermon.xlsx");
        var result = ScheduleExtractor.Extract(path, "9월");
        Assert.True(result.Ok, string.Join(" | ", result.Warnings));

        var sermon = result.Events.Single(e => e.Title == "주일말씀");
        Assert.Equal(new DateOnly(2026, 9, 6), sermon.EventDate);
        Assert.Equal("홍길동PA", sermon.Speaker);
        Assert.Equal("서울", sermon.Notes);

        var other = result.Events.Single(e => e.Title == "구역형제모임");
        Assert.Equal(new DateOnly(2026, 9, 6), other.EventDate);
        Assert.Equal("", other.Speaker);

        Assert.Equal("홍길동PA", result.Items.Single(i => i.ScheduleTitle == "주일말씀").SermonSpeaker);
        Assert.Equal("", result.Items.Single(i => i.ScheduleTitle == "구역형제모임").SermonSpeaker);
    }

    [Fact]
    public void Sunday_extract_clears_speaker_without_sermon_title()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("9월");
        ws.Cell(1, 1).Value = "2026년 9월 광주교회 일정표";
        var headers = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var i = 0; i < 7; i++)
            ws.Cell(2, i + 1).Value = headers[i];
        Put(ws, 3, 3, "1");
        Put(ws, 3, 4, "2");
        Put(ws, 3, 5, "3");
        Put(ws, 3, 6, "4");
        Put(ws, 3, 7, "5");
        Put(ws, 4, 1, "6\n구역형제모임\n(홍길동P)");
        Put(ws, 4, 2, "7");

        var path = SaveTemp(wb, "sunday-no-sermon.xlsx");
        var result = ScheduleExtractor.Extract(path, "9월");
        Assert.True(result.Ok, string.Join(" | ", result.Warnings));
        var ev = result.Events.Single(e => e.Title == "구역형제모임");
        Assert.Equal("", ev.Speaker);
        Assert.Equal("", result.Items.Single(i => i.ScheduleTitle == "구역형제모임").SermonSpeaker);
    }

    private static void Put(IXLWorksheet ws, int row, int col, string text)
    {
        ws.Cell(row, col).Value = text;
        ws.Cell(row, col).Style.Alignment.WrapText = true;
    }

    private static void DumpSample(string xlsxPath, ScheduleReader.Models.ExtractResult result)
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var outDir = Path.Combine(repo, "output");
        Directory.CreateDirectory(outDir);
        File.Copy(xlsxPath, Path.Combine(outDir, "sample-2026-09.xlsx"), overwrite: true);

        var json = JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            warnings = result.Warnings,
            meta = result.Meta,
            eventCount = result.Events.Count,
            itemCount = result.Items.Count,
            shapeCount = result.Shapes.Count,
            events = result.Events.Select(e => new
            {
                date = e.EventDate?.ToString("yyyy-MM-dd"),
                endDate = e.EndDate?.ToString("yyyy-MM-dd"),
                title = e.Title,
                speaker = e.Speaker,
                notes = e.Notes,
                source = e.SourceHint,
            }),
            items = result.Items.Select(i => new
            {
                i.ScheduleDate,
                i.ReleaseDate,
                i.ScheduleTitle,
                i.SermonSpeaker,
                i.AccessoryInfo,
                i.StartTime,
                i.Place,
            }),
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(Path.Combine(outDir, "sample-extract.json"), json + "\n", Encoding.UTF8);
    }

    private static void AttachTextShape(string xlsxPath, int fromCol0, int fromRow0, string name, string text)
    {
        var escapedName = System.Security.SecurityElement.Escape(name) ?? name;
        var paragraphs = string.Join("",
            text.Replace("\r\n", "\n").Split('\n').Select(line =>
                $"<a:p><a:r><a:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(line)}</a:t></a:r></a:p>"));
        var drawingXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<xdr:twoCellAnchor>" +
            $"<xdr:from><xdr:col>{fromCol0}</xdr:col><xdr:colOff>0</xdr:colOff>" +
            $"<xdr:row>{fromRow0}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
            $"<xdr:to><xdr:col>{fromCol0}</xdr:col><xdr:colOff>500000</xdr:colOff>" +
            $"<xdr:row>{fromRow0}</xdr:row><xdr:rowOff>500000</xdr:rowOff></xdr:to>" +
            "<xdr:sp><xdr:nvSpPr>" +
            $"<xdr:cNvPr id=\"2\" name=\"{escapedName}\"/><xdr:cNvSpPr/>" +
            "</xdr:nvSpPr><xdr:spPr>" +
            "<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1000000\" cy=\"500000\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>" +
            $"<xdr:txBody><a:bodyPr/><a:lstStyle/>{paragraphs}</xdr:txBody>" +
            "</xdr:sp><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>";

        using var zip = ZipFile.Open(xlsxPath, ZipArchiveMode.Update);
        var sheetEntry = zip.Entries.First(e =>
        {
            var n = e.FullName.Replace('\\', '/');
            return n.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("/_rels/", StringComparison.Ordinal);
        });
        var sheetPath = sheetEntry.FullName.Replace('\\', '/');
        var sheetFile = Path.GetFileName(sheetPath);
        var relsPath = $"xl/worksheets/_rels/{sheetFile}.rels";

        zip.GetEntry("xl/drawings/drawing1.xml")?.Delete();
        var drawing = zip.CreateEntry("xl/drawings/drawing1.xml");
        using (var s = drawing.Open())
            s.Write(Encoding.UTF8.GetBytes(drawingXml));

        var relId = UpsertDrawingRel(zip, relsPath);
        PatchWorksheetDrawing(sheetEntry, relId);
        EnsureDrawingContentType(zip);
    }

    private static string UpsertDrawingRel(ZipArchive zip, string relsPath)
    {
        const string drawingType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument doc;
        var existing = zip.GetEntry(relsPath);
        if (existing is null)
        {
            doc = new XDocument(new XElement(ns + "Relationships"));
        }
        else
        {
            using var s = existing.Open();
            doc = XDocument.Load(s);
            existing.Delete();
        }

        doc.Root!.Elements(ns + "Relationship")
            .Where(e => (string?)e.Attribute("Target") == "../drawings/drawing1.xml")
            .Remove();

        var used = doc.Root.Elements(ns + "Relationship")
            .Select(e => (string?)e.Attribute("Id") ?? "")
            .ToHashSet(StringComparer.Ordinal);
        var id = 1;
        while (used.Contains($"rId{id}")) id++;
        var relId = $"rId{id}";
        doc.Root.Add(new XElement(ns + "Relationship",
            new XAttribute("Id", relId),
            new XAttribute("Type", drawingType),
            new XAttribute("Target", "../drawings/drawing1.xml")));

        var entry = zip.CreateEntry(relsPath);
        using (var s = entry.Open())
            doc.Save(s);
        return relId;
    }

    private static void PatchWorksheetDrawing(ZipArchiveEntry sheetEntry, string relId)
    {
        XDocument doc;
        using (var s = sheetEntry.Open())
            doc = XDocument.Load(s);

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        doc.Root?.Elements(main + "drawing").Remove();
        doc.Root?.Add(new XElement(main + "drawing", new XAttribute(r + "id", relId)));

        var fullName = sheetEntry.FullName;
        var archive = sheetEntry.Archive;
        sheetEntry.Delete();
        var next = archive.CreateEntry(fullName);
        using var outStream = next.Open();
        doc.Save(outStream);
    }

    private static void EnsureDrawingContentType(ZipArchive zip)
    {
        var ct = zip.GetEntry("[Content_Types].xml")
            ?? throw new InvalidOperationException("[Content_Types].xml 없음");
        XDocument doc;
        using (var s = ct.Open())
            doc = XDocument.Load(s);

        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        const string part = "/xl/drawings/drawing1.xml";
        const string type = "application/vnd.openxmlformats-officedocument.drawing+xml";
        if (doc.Root!.Elements(ns + "Override").All(e => (string?)e.Attribute("PartName") != part))
            doc.Root.Add(new XElement(ns + "Override",
                new XAttribute("PartName", part),
                new XAttribute("ContentType", type)));

        ct.Delete();
        var next = zip.CreateEntry("[Content_Types].xml");
        using var outStream = next.Open();
        doc.Save(outStream);
    }
}
