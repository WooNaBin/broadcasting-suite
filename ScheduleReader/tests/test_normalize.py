from schedule_reader.models import RawEvent
from schedule_reader.parser.filters import apply_filters
from schedule_reader.parser.normalize import expand_and_normalize, parse_hhmm


def test_parse_hhmm():
    assert parse_hhmm("10:30") == 1030
    assert parse_hhmm("1930") == 1930
    assert parse_hhmm("9:05") == 900  # 10-minute grid
    assert parse_hhmm(None) == 0
    assert parse_hhmm("") == 0


def test_filter_excludes_birthday():
    events = [
        RawEvent(date="2026-09-01", title="주일말씀", speaker="A"),
        RawEvent(date="2026-09-02", title="김OO 생일", speaker=""),
    ]
    kept = apply_filters(
        events,
        {"exclude_keywords": ["생일"], "drop_empty_title": True, "include_keywords": []},
    )
    assert len(kept) == 1
    assert kept[0].title == "주일말씀"


def test_expand_range():
    events = [
        RawEvent(
            date="2026-09-01",
            end_date="2026-09-03",
            title="부흥회",
            speaker="B",
            start_time="19:30",
            place="본당 대예배실",
        )
    ]
    items = expand_and_normalize(events, expand_ranges=True)
    assert len(items) == 3
    assert items[0].startTime == 0
    assert items[0].place == "None"
    assert items[0].sermonSpeaker == "B"
    assert [i.scheduleDate for i in items] == [
        "2026-09-01",
        "2026-09-02",
        "2026-09-03",
    ]
