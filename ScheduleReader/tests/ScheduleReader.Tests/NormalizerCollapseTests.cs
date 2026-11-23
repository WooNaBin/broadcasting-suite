using ScheduleReader.Models;
using ScheduleReader.Parser;
using Xunit;

namespace ScheduleReader.Tests;

public class NormalizerCollapseTests
{
    private static ScheduleItem Day(string date, string title, string speaker = "", int time = 0) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ScheduleDate = date,
            ReleaseDate = date,
            ScheduleTitle = title,
            SermonSpeaker = speaker,
            StartTime = time,
            Place = "None",
            Color = "auto",
            ColorMode = "auto",
        };

    [Fact]
    public void Collapse_merges_consecutive_same_title()
    {
        var items = new[]
        {
            Day("2026-09-12", "수련회", "홍길동"),
            Day("2026-09-13", "수련회", "홍길동"),
            Day("2026-09-14", "수련회", "홍길동"),
            Day("2026-09-15", "수요말씀", "김철수"),
        };

        var collapsed = Normalizer.CollapseConsecutive(items);
        Assert.Equal(2, collapsed.Count);
        Assert.Equal("2026-09-12", collapsed[0].ScheduleDate);
        Assert.Equal("2026-09-14", collapsed[0].ReleaseDate);
        Assert.Equal("수련회", collapsed[0].ScheduleTitle);
        Assert.Equal("2026-09-15", collapsed[1].ScheduleDate);
        Assert.Equal("2026-09-15", collapsed[1].ReleaseDate);
    }

    [Fact]
    public void Collapse_absorbs_same_day_duplicate_into_longer_range()
    {
        // 9.12~9.12 와 9.12~9.13 펼침 → 같은 날 중복 + 다음날 → 한 기간
        var items = new[]
        {
            Day("2026-09-12", "수련회", "홍길동"),
            Day("2026-09-12", "수련회", "홍길동"),
            Day("2026-09-13", "수련회", "홍길동"),
        };

        var collapsed = Normalizer.CollapseConsecutive(items);
        Assert.Single(collapsed);
        Assert.Equal("2026-09-12", collapsed[0].ScheduleDate);
        Assert.Equal("2026-09-13", collapsed[0].ReleaseDate);
        Assert.Equal("홍길동", collapsed[0].SermonSpeaker);
    }

    [Fact]
    public void Collapse_merges_parallel_titles_independently()
    {
        // 같은 날 다른 제목이 섞여도 각각 연속 합침
        var items = new[]
        {
            Day("2026-09-12", "수련회", "홍길동"),
            Day("2026-09-12", "말씀", "김철수"),
            Day("2026-09-13", "수련회", "홍길동"),
            Day("2026-09-13", "말씀", "김철수"),
        };

        var collapsed = Normalizer.CollapseConsecutive(items);
        Assert.Equal(2, collapsed.Count);
        var retreat = Assert.Single(collapsed, i => i.ScheduleTitle == "수련회");
        var worship = Assert.Single(collapsed, i => i.ScheduleTitle == "말씀");
        Assert.Equal("2026-09-12", retreat.ScheduleDate);
        Assert.Equal("2026-09-13", retreat.ReleaseDate);
        Assert.Equal("2026-09-12", worship.ScheduleDate);
        Assert.Equal("2026-09-13", worship.ReleaseDate);
    }

    [Fact]
    public void Collapse_does_not_merge_gap_or_different_speakers()
    {
        var items = new[]
        {
            Day("2026-09-12", "수련회", "A"),
            Day("2026-09-14", "수련회", "A"),
            Day("2026-09-15", "수련회", "B"),
        };

        var collapsed = Normalizer.CollapseConsecutive(items);
        Assert.Equal(3, collapsed.Count);
        Assert.Equal("2026-09-12", collapsed[0].ScheduleDate);
        Assert.Equal("2026-09-12", collapsed[0].ReleaseDate);
        Assert.Equal("2026-09-14", collapsed[1].ScheduleDate);
        Assert.Equal("2026-09-14", collapsed[1].ReleaseDate);
        Assert.Equal("A", collapsed[1].SermonSpeaker);
        Assert.Equal("2026-09-15", collapsed[2].ScheduleDate);
        Assert.Equal("B", collapsed[2].SermonSpeaker);
    }

    [Fact]
    public void Collapse_merges_when_one_speaker_empty()
    {
        // 일요일 강사 비움 등으로 한쪽만 비어 있어도 제목이 같으면 이어 붙임
        var items = new[]
        {
            Day("2026-09-12", "수련회", "홍길동"),
            Day("2026-09-13", "수련회", ""),
        };

        var collapsed = Normalizer.CollapseConsecutive(items);
        Assert.Single(collapsed);
        Assert.Equal("2026-09-12", collapsed[0].ScheduleDate);
        Assert.Equal("2026-09-13", collapsed[0].ReleaseDate);
        Assert.Equal("홍길동", collapsed[0].SermonSpeaker);
    }

    [Fact]
    public void Expand_then_collapse_roundtrip_day_count()
    {
        // 일요일이 끼면 비주일말씀은 강사가 비워져도 제목으로 이어 붙임 → 월~수 사용은 안정용
        var ranges = new[]
        {
            new ScheduleItem
            {
                ScheduleDate = "2026-09-14",
                ReleaseDate = "2026-09-16",
                ScheduleTitle = "수련회",
                SermonSpeaker = "홍길동",
                Place = "Main_Large",
                StartTime = 1000,
                Color = "auto",
                ColorMode = "auto",
            },
        };

        var expanded = Normalizer.ExpandDateRanges(ranges);
        Assert.Equal(3, expanded.Count);
        Assert.All(expanded, i => Assert.Equal("2026-09-16", i.ReleaseDate));

        var collapsed = Normalizer.CollapseConsecutive(expanded);
        Assert.Single(collapsed);
        Assert.Equal("2026-09-14", collapsed[0].ScheduleDate);
        Assert.Equal("2026-09-16", collapsed[0].ReleaseDate);
        Assert.Equal("홍길동", collapsed[0].SermonSpeaker);
    }
}
