using System.Text.Json;
using ScheduleReader.Excel;
using Xunit;

namespace ScheduleReader.Tests;

public class BracketParserTests
{
    private const int Year = 2026;
    private const int Month = 9;

    public BracketParserTests()
    {
        BracketParser.SetContext(Year, Month);
    }

    [Fact]
    public void District_gathering_with_date_range_and_locations()
    {
        const string text = "교구집회(8.31-9.4) : 매곡6(이정익P),첨단2(이기천P),용봉3(김선욱P),두암(양현준P)";
        var entries = BracketParser.ParseEntries(text);

        Assert.Single(entries);
        Assert.Equal("교구집회", entries[0].Title);
        Assert.Equal(new DateOnly(2026, 8, 31), entries[0].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 4), entries[0].EndDate);
        Assert.Equal("매곡6, 첨단2, 용봉3, 두암", entries[0].AccessoryInfo);
        Assert.Equal("이정익PA, 이기천PA, 김선욱PA, 양현준PA", entries[0].Speaker);

        var items = BracketParser.ToScheduleItems(text);
        // 강사·부가정보 4쌍 → 제목(교구)별로 나눈 뒤 기간(5일) 펼침
        Assert.Equal(20, items.Count);
        Assert.All(items, i => Assert.Equal("2026-09-04", i.ReleaseDate));
        Assert.Equal("2026-08-31", items[0].ScheduleDate);
        Assert.Equal("2026-09-04", items[^1].ScheduleDate);
        Assert.Contains(items, i => i.ScheduleTitle == "교구집회(매곡6)" && i.SermonSpeaker == "이정익PA");
        Assert.Contains(items, i => i.ScheduleTitle == "교구집회(첨단2)" && i.SermonSpeaker == "이기천PA");
        Assert.Contains(items, i => i.ScheduleTitle == "교구집회(용봉3)" && i.SermonSpeaker == "김선욱PA");
        Assert.Contains(items, i => i.ScheduleTitle == "교구집회(두암)" && i.SermonSpeaker == "양현준PA");
        Assert.Equal("None", items[0].Place);
        Assert.Empty(items[0].Preparation);
        Assert.Equal(0, items[0].StartTime);
    }

    [Fact]
    public void Sunday_column_stack_splits_meetings()
    {
        const string text = """
            주일말씀
            (김연숙P:서울노원)
            유치부/중고등부 학부모교제
            구역형제모임
            """;
        var entries = BracketParser.ParseEntries(text, contextDay: 6);
        Assert.True(entries.Count >= 2, string.Join(" || ", entries.Select(e => e.Title)));
        Assert.Contains(entries, e => e.Title.Contains("주일말씀"));
        Assert.Contains(entries, e => e.Title.Contains("구역형제모임"));
        var sermon = entries.First(e => e.Title == "주일말씀");
        Assert.Equal("김연숙PA", sermon.Speaker);
        Assert.Equal("서울노원", sermon.AccessoryInfo);
        Assert.All(entries.Where(e => e.Title != "주일말씀"), e => Assert.Equal("", e.Speaker));
    }

    [Fact]
    public void Slash_without_spaces_stays_one_title()
    {
        var entries = BracketParser.ParseEntries("유치부/중고등부 학부모교제", contextDay: 30);
        Assert.Single(entries);
        Assert.Equal("유치부/중고등부 학부모교제", entries[0].Title);
    }

    [Fact]
    public void Slash_separates_three_events()
    {
        const string text =
            "청년회집회(7-12):정진관P  /  담양고서(7-11):이태훈P  /  직장선교부_서부유통&광일(7-11):박종욱P";
        var entries = BracketParser.ParseEntries(text);

        Assert.Equal(3, entries.Count);
        Assert.Equal("청년회집회", entries[0].Title);
        Assert.Equal(new DateOnly(2026, 9, 7), entries[0].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 12), entries[0].EndDate);
        Assert.Equal("정진관PA", entries[0].Speaker);

        Assert.Equal("담양고서", entries[1].Title);
        Assert.Equal(new DateOnly(2026, 9, 7), entries[1].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 11), entries[1].EndDate);
        Assert.Equal("이태훈PA", entries[1].Speaker);

        Assert.Equal("직장선교부_서부유통&광일", entries[2].Title);
        Assert.Equal("박종욱PA", entries[2].Speaker);
    }

    [Fact]
    public void Title_and_day_range_only()
    {
        var entries = BracketParser.ParseEntries("추계수양회(17-20)");
        Assert.Single(entries);
        Assert.Equal("추계수양회", entries[0].Title);
        Assert.Equal(new DateOnly(2026, 9, 17), entries[0].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 20), entries[0].EndDate);
        Assert.Equal("", entries[0].Speaker);
    }

    [Fact]
    public void Month_day_range_same_month_with_speaker()
    {
        var entries = BracketParser.ParseEntries("초등부집회(8.7-9) : 이주현P");
        Assert.Single(entries);
        Assert.Equal("초등부집회", entries[0].Title);
        Assert.Equal(new DateOnly(2026, 8, 7), entries[0].StartDate);
        Assert.Equal(new DateOnly(2026, 8, 9), entries[0].EndDate);
        Assert.Equal("이주현PA", entries[0].Speaker);
    }

    [Fact]
    public void Slash_second_event_uses_context_day()
    {
        const string text = "대전도집회(5.11-17) : 임승환P      /    초등부(김규석E, 성재경E, 송경근E)";
        var entries = BracketParser.ParseEntries(text, contextDay: 15);

        Assert.Equal(2, entries.Count);
        Assert.Equal("대전도집회", entries[0].Title);
        Assert.Equal(new DateOnly(2026, 5, 11), entries[0].StartDate);
        Assert.Equal(new DateOnly(2026, 5, 17), entries[0].EndDate);
        Assert.Equal("임승환PA", entries[0].Speaker);

        Assert.Equal("초등부", entries[1].Title);
        Assert.Equal(new DateOnly(2026, 9, 15), entries[1].StartDate);
        Assert.Equal("김규석Ev, 성재경Ev, 송경근Ev", entries[1].Speaker);
    }

    [Fact]
    public void Two_line_cell_is_two_events()
    {
        const string text = """
            담양고서집회(박경수P)
            교구집회:동림(강대석P).삼각1(김도형P),매곡5(이주현P),신용3(권오진P)
            """;
        var entries = BracketParser.ParseEntries(text, contextDay: 10);

        Assert.Equal(2, entries.Count);
        Assert.Equal("담양고서집회", entries[0].Title);
        Assert.Equal("박경수PA", entries[0].Speaker);
        Assert.Equal(new DateOnly(2026, 9, 10), entries[0].StartDate);

        Assert.Equal("교구집회", entries[1].Title);
        Assert.Equal("동림, 삼각1, 매곡5, 신용3", entries[1].AccessoryInfo);
        Assert.Equal("강대석PA, 김도형PA, 이주현PA, 권오진PA", entries[1].Speaker);
    }

    [Fact]
    public void Simple_calendar_title_and_name()
    {
        var entries = BracketParser.ParseEntries("개학예배\n홍길동", contextDay: 1);
        Assert.Single(entries);
        Assert.Equal("개학예배", entries[0].Title);
        Assert.Equal("홍길동", entries[0].Speaker);
        Assert.Equal(new DateOnly(2026, 9, 1), entries[0].StartDate);
    }

    [Fact]
    public void Simple_calendar_paren_without_suffix_is_accessory()
    {
        var entries = BracketParser.ParseEntries("주일말씀\n(김철수)", contextDay: 6);
        Assert.Single(entries);
        Assert.Equal("주일말씀", entries[0].Title);
        Assert.Equal("", entries[0].Speaker);
        Assert.Equal("김철수", entries[0].AccessoryInfo);
    }

    [Fact]
    public void Speaker_suffix_colon_place_splits()
    {
        var entries = BracketParser.ParseEntries("주일말씀\n(홍길동P:서울)", contextDay: 6);
        Assert.Single(entries);
        Assert.Equal("주일말씀", entries[0].Title);
        Assert.Equal("홍길동PA", entries[0].Speaker);
        Assert.Equal("서울", entries[0].AccessoryInfo);
    }

    [Fact]
    public void Leading_day_title_and_accessory_paren()
    {
        var entries = BracketParser.ParseEntries("13 하계기도부탁공유(1차)");
        Assert.Single(entries);
        Assert.Equal("하계기도부탁공유", entries[0].Title);
        Assert.Equal("1차", entries[0].AccessoryInfo);
        Assert.Equal("", entries[0].Speaker);
        Assert.Equal(new DateOnly(2026, 9, 13), entries[0].StartDate);
    }

    [Fact]
    public void Leading_day_title_only()
    {
        var entries = BracketParser.ParseEntries("6 현충일");
        Assert.Single(entries);
        Assert.Equal("현충일", entries[0].Title);
        Assert.Equal("", entries[0].Speaker);
        Assert.Equal("", entries[0].AccessoryInfo);
        Assert.Equal(new DateOnly(2026, 9, 6), entries[0].StartDate);
    }

    [Fact]
    public void Sunday_speaker_moves_to_sermon_from_last_event()
    {
        const string text = """
            주일말씀
            유치부/중고등부 학부모교제
            구역형제모임
            (김연숙P:서울노원)
            """;
        var entries = BracketParser.ParseEntries(text, contextDay: 6);
        Assert.Equal(3, entries.Count);
        var sermon = entries.First(e => e.Title == "주일말씀");
        Assert.Equal("김연숙PA", sermon.Speaker);
        Assert.Equal("서울노원", sermon.AccessoryInfo);
        Assert.All(entries.Where(e => e.Title != "주일말씀"), e =>
        {
            Assert.Equal("", e.Speaker);
            Assert.Equal("", e.AccessoryInfo);
        });
    }

    [Fact]
    public void Sunday_without_sermon_title_keeps_speaker_until_extract()
    {
        var entries = BracketParser.ParseEntries("구역형제모임\n(홍길동P)", contextDay: 6);
        Assert.Single(entries);
        Assert.Equal("홍길동PA", entries[0].Speaker);
    }

    [Fact]
    public void Weekday_keeps_speaker_on_non_sermon_title()
    {
        var entries = BracketParser.ParseEntries("구역형제모임\n(홍길동P)", contextDay: 7);
        Assert.Single(entries);
        Assert.Equal("홍길동PA", entries[0].Speaker);
        Assert.Equal(DayOfWeek.Monday, entries[0].StartDate!.Value.DayOfWeek);
    }

    [Fact]
    public void Json_matches_schedule_data_shape()
    {
        var json = BracketParser.ToJson("추계수양회(17-20)");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(4, doc.RootElement.GetArrayLength());

        var first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.Equal("2026-09-17", first.GetProperty("scheduleDate").GetString());
        Assert.Equal("2026-09-20", first.GetProperty("releaseDate").GetString());
        Assert.Equal(0, first.GetProperty("startTime").GetInt32());
        Assert.Equal("추계수양회", first.GetProperty("scheduleTitle").GetString());
        Assert.Equal("", first.GetProperty("accessoryInfo").GetString());
        Assert.Equal("None", first.GetProperty("place").GetString());
        Assert.Equal("", first.GetProperty("sermonSpeaker").GetString());
        Assert.Equal(JsonValueKind.Array, first.GetProperty("preparation").ValueKind);
        Assert.Equal("", first.GetProperty("preaparationNote").GetString());
        Assert.Equal("auto", first.GetProperty("color").GetString());
        Assert.Equal("auto", first.GetProperty("colorMode").GetString());
    }

    [Fact]
    public void December_to_january_uses_next_year()
    {
        BracketParser.SetContext(2026, 12);
        var entries = BracketParser.ParseEntries("송구영신(12.28-1.3)");
        Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 12, 28), entries[0].StartDate);
        Assert.Equal(new DateOnly(2027, 1, 3), entries[0].EndDate);
    }

    [Fact]
    public void SetContext_changes_day_only_month()
    {
        BracketParser.SetContext(2026, 10);
        var entries = BracketParser.ParseEntries("추계수양회(17-20)");
        Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 10, 17), entries[0].StartDate);
        Assert.Equal(new DateOnly(2026, 10, 20), entries[0].EndDate);
    }
}
