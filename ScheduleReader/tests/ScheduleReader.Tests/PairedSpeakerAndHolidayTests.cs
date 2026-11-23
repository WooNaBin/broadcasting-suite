using ScheduleReader.Excel;
using ScheduleReader.Models;
using ScheduleReader.Parser;
using Xunit;

namespace ScheduleReader.Tests;

public class PairedSpeakerAccessoryTests
{
    [Fact]
    public void Expand_splits_when_speaker_and_accessory_counts_match()
    {
        var pairs = BracketParser.ExpandPairedSpeakerAccessory(
            "교구모임",
            "김이름PA, 박이름PA",
            "서울, 부산");

        Assert.Equal(2, pairs.Count);
        Assert.Equal(("교구모임(서울)", "김이름PA", "서울"), pairs[0]);
        Assert.Equal(("교구모임(부산)", "박이름PA", "부산"), pairs[1]);
    }

    [Fact]
    public void Expand_keeps_single_when_counts_differ()
    {
        var pairs = BracketParser.ExpandPairedSpeakerAccessory(
            "교구모임",
            "김이름PA, 박이름PA",
            "서울");

        Assert.Single(pairs);
        Assert.Equal("교구모임", pairs[0].Title);
        Assert.Equal("김이름PA, 박이름PA", pairs[0].Speaker);
        Assert.Equal("서울", pairs[0].Accessory);
    }

    [Fact]
    public void ExpandAndNormalize_creates_separate_items_with_same_period()
    {
        var items = Normalizer.ExpandAndNormalize(
            [
                new RawEvent
                {
                    EventDate = new DateOnly(2026, 9, 10),
                    EndDate = new DateOnly(2026, 9, 12),
                    Title = "교구모임",
                    Speaker = "김이름P, 박이름P",
                    Notes = "서울, 부산",
                },
            ],
            expandRanges: true,
            forceNoTime: true,
            forceNoPlace: true);

        var collapsed = Normalizer.CollapseConsecutive(items);
        Assert.Equal(2, collapsed.Count);

        var seoul = Assert.Single(collapsed, i => i.ScheduleTitle == "교구모임(서울)");
        Assert.Equal("김이름PA", seoul.SermonSpeaker);
        Assert.Equal("서울", seoul.AccessoryInfo);
        Assert.Equal("2026-09-10", seoul.ScheduleDate);
        Assert.Equal("2026-09-12", seoul.ReleaseDate);

        var busan = Assert.Single(collapsed, i => i.ScheduleTitle == "교구모임(부산)");
        Assert.Equal("박이름PA", busan.SermonSpeaker);
        Assert.Equal("부산", busan.AccessoryInfo);
        Assert.Equal("2026-09-10", busan.ScheduleDate);
        Assert.Equal("2026-09-12", busan.ReleaseDate);
    }
}

public class KoreanHolidaysTests
{
    [Fact]
    public void Memorial_day_2026_is_holiday()
    {
        Assert.Equal("현충일", KoreanHolidays.GetHolidayName("2026-06-06"));
        Assert.True(KoreanHolidays.IsHolidaySchedule("현충일", "2026-06-06"));
        Assert.False(KoreanHolidays.IsHolidaySchedule("구역모임", "2026-06-06"));
        Assert.False(KoreanHolidays.IsHolidaySchedule("현충일", "2026-06-07"));
    }

    [Fact]
    public void ExcludeHolidaySchedules_removes_only_holiday_titles()
    {
        var items = new List<ScheduleItem>
        {
            new()
            {
                Id = "1",
                ScheduleDate = "2026-06-06",
                ReleaseDate = "2026-06-06",
                ScheduleTitle = "현충일",
            },
            new()
            {
                Id = "2",
                ScheduleDate = "2026-06-06",
                ReleaseDate = "2026-06-06",
                ScheduleTitle = "구역모임",
            },
        };

        var kept = KoreanHolidays.ExcludeHolidaySchedules(items);
        Assert.Single(kept);
        Assert.Equal("구역모임", kept[0].ScheduleTitle);
    }
}
