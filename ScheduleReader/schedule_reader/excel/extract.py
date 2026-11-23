from __future__ import annotations

import re
from datetime import date, datetime, time, timedelta
from pathlib import Path
from typing import Any

from openpyxl.worksheet.worksheet import Worksheet

from schedule_reader.excel.preview import cell_text
from schedule_reader.models import RawEvent
from schedule_reader.parser.normalize import expand_and_normalize, parse_hhmm

HEADER_ALIASES: dict[str, tuple[str, ...]] = {
    "date": ("날짜", "일자", "일정일", "date", "스케줄일", "방송일", "시작일"),
    "end_date": ("종료일", "끝날짜", "종료", "end", "종료일자", "마침일"),
    "title": ("제목", "일정", "행사", "스케줄", "내용", "title", "일정제목", "프로그램"),
    "speaker": ("강사", "설교", "강사명", "speaker", "설교자", "인도"),
    "time": ("시간", "시작", "시작시간", "time", "시각"),
    "place": ("장소", "place", "위치", "홀"),
    "note": ("비고", "설명", "메모", "note", "부가", "준비"),
}


def _normalize_header(text: str) -> str:
    return re.sub(r"\s+", "", (text or "").strip().lower())


def _match_field(header: str) -> str | None:
    key = _normalize_header(header)
    if not key:
        return None
    # Exact match first
    for field, aliases in HEADER_ALIASES.items():
        for alias in aliases:
            if key == _normalize_header(alias):
                return field
    # Partial match only for multi-char tokens (avoid 일→일자, 목→제목)
    for field, aliases in HEADER_ALIASES.items():
        for alias in aliases:
            a = _normalize_header(alias)
            if len(a) < 2 or len(key) < 2:
                continue
            if a in key or key in a:
                return field
    return None


def _parse_date(value: Any, *, default_year: int | None = None) -> date | None:
    if value is None or value == "":
        return None
    if isinstance(value, datetime):
        return value.date()
    if isinstance(value, date):
        return value
    if isinstance(value, time):
        return None
    text = cell_text(value)
    if not text:
        return None
    # Excel serial sometimes arrives as int/float when data_only
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        try:
            # openpyxl already converts usually; fallback serial from 1899-12-30
            base = datetime(1899, 12, 30) + timedelta(days=float(value))
            return base.date()
        except Exception:
            pass
    for fmt in ("%Y-%m-%d", "%Y/%m/%d", "%Y.%m.%d", "%y-%m-%d", "%y/%m/%d"):
        try:
            return datetime.strptime(text[:10], fmt).date()
        except ValueError:
            pass
    m = re.match(r"^(\d{1,2})[./-](\d{1,2})(?:[./-](\d{2,4}))?$", text)
    if m:
        a, b, c = m.group(1), m.group(2), m.group(3)
        month, day = int(a), int(b)
        if c:
            year = int(c)
            if year < 100:
                year += 2000
        elif default_year:
            year = default_year
        else:
            year = date.today().year
        try:
            return date(year, month, day)
        except ValueError:
            try:
                return date(year, day, month)
            except ValueError:
                return None
    m2 = re.search(r"(\d{4})\s*년\s*(\d{1,2})\s*월\s*(\d{1,2})\s*일", text)
    if m2:
        try:
            return date(int(m2.group(1)), int(m2.group(2)), int(m2.group(3)))
        except ValueError:
            return None
    m3 = re.search(r"(\d{1,2})\s*월\s*(\d{1,2})\s*일", text)
    if m3 and default_year:
        try:
            return date(default_year, int(m3.group(1)), int(m3.group(2)))
        except ValueError:
            return None
    return None


def _parse_time_text(value: Any) -> str | None:
    if value is None or value == "":
        return None
    if isinstance(value, datetime):
        return f"{value.hour:02d}:{value.minute:02d}"
    if isinstance(value, time):
        return f"{value.hour:02d}:{value.minute:02d}"
    text = cell_text(value)
    if not text:
        return None
    if parse_hhmm(text):
        n = parse_hhmm(text)
        return f"{n // 100:02d}:{n % 100:02d}"
    m = re.search(r"(\d{1,2})\s*[:시]\s*(\d{0,2})", text)
    if m:
        hour = int(m.group(1))
        minute = int(m.group(2) or "0")
        if 0 <= hour <= 23 and 0 <= minute <= 59:
            return f"{hour:02d}:{minute:02d}"
    return None


def detect_header_map(rows: list[list[Any]]) -> tuple[int, dict[str, int]] | None:
    """Return (header_row_index, field->col_index) if a usable header is found."""
    best: tuple[int, dict[str, int], int] | None = None
    for idx, row in enumerate(rows[:40]):
        mapping: dict[str, int] = {}
        for col, cell in enumerate(row):
            field = _match_field(cell_text(cell))
            if field and field not in mapping:
                mapping[field] = col
        score = len(mapping)
        if "date" in mapping:
            score += 2
        if "title" in mapping:
            score += 2
        if score >= 3 and "date" in mapping and "title" in mapping:
            if best is None or score > best[2]:
                best = (idx, mapping, score)
    if not best:
        return None
    return best[0], best[1]


def _guess_year_month(ws: Worksheet) -> tuple[int | None, int | None]:
    title_bits = [ws.title]
    for row in ws.iter_rows(min_row=1, max_row=5, max_col=8, values_only=True):
        for cell in row:
            text = cell_text(cell)
            if text:
                title_bits.append(text)
    blob = " ".join(title_bits)
    year = month = None
    ym = re.search(r"(20\d{2})\s*년\s*(\d{1,2})\s*월", blob)
    if ym:
        year, month = int(ym.group(1)), int(ym.group(2))
    else:
        y = re.search(r"(20\d{2})", blob)
        m = re.search(r"(\d{1,2})\s*월", blob)
        if y:
            year = int(y.group(1))
        if m:
            month = int(m.group(1))
    return year, month


def extract_schedules_from_sheet(
    ws: Worksheet,
    *,
    xlsx_path: str | Path | None = None,
) -> dict[str, Any]:
    """
    Extract church schedules from a worksheet.

    Sources:
    - cell table (날짜/제목 헤더) or calendar grid (일~토)
    - drawing shapes / text boxes (when xlsx_path is provided)
    """
    from schedule_reader.excel.calendar_grid import (
        _find_weekday_header,
        extract_calendar_grid,
    )
    from schedule_reader.excel.shape_events import shapes_to_events
    from schedule_reader.excel.shapes import extract_shapes_from_xlsx

    warnings: list[str] = []
    shape_dicts: list[dict[str, Any]] = []
    max_row = min(ws.max_row or 0, 500)
    max_col = min(ws.max_column or 0, 40)
    matrix: list[list[Any]] = []
    for row in ws.iter_rows(min_row=1, max_row=max_row, max_col=max_col, values_only=True):
        matrix.append(list(row))

    year_hint, month_hint = _guess_year_month(ws)
    text_matrix = [[cell_text(c) for c in row] for row in matrix]
    detected = detect_header_map(text_matrix)

    # Weekday header (일~토) → prefer calendar grid over accidental header matches
    weekday_header = _find_weekday_header(text_matrix)
    use_calendar = False
    if weekday_header and year_hint and month_hint:
        if not detected:
            use_calendar = True
        else:
            header_idx, _colmap = detected
            if header_idx == weekday_header[0]:
                use_calendar = True

    events: list[RawEvent] = []
    meta: dict[str, Any] = {
        "sheet": ws.title,
        "yearHint": year_hint,
        "monthHint": month_hint,
    }

    if use_calendar:
        cal = extract_calendar_grid(ws, year=year_hint, month=month_hint)
        meta.update(cal.get("meta") or {"strategy": "calendar_grid"})
        warnings.extend(cal.get("warnings") or [])
        events = list(cal.get("events") or [])
    elif detected:
        header_idx, colmap = detected
        meta.update(
            {
                "strategy": "header_table",
                "headerRow": header_idx + 1,
                "columns": colmap,
            }
        )
        for row in matrix[header_idx + 1 :]:

            def col(field: str, _row: list[Any] = row) -> Any:
                i = colmap.get(field)
                if i is None or i >= len(_row):
                    return None
                return _row[i]

            event_date = _parse_date(col("date"), default_year=year_hint)
            title = cell_text(col("title"))
            if not title:
                continue
            if not event_date:
                warnings.append(f"날짜 없음으로 건너뜀: {title}")
                continue
            end_raw = col("end_date") if "end_date" in colmap else None
            end_date = _parse_date(end_raw, default_year=year_hint) if end_raw else None
            speaker = cell_text(col("speaker")) if "speaker" in colmap else ""
            place = cell_text(col("place")) if "place" in colmap else ""
            note = cell_text(col("note")) if "note" in colmap else ""
            start_time = _parse_time_text(col("time")) if "time" in colmap else None
            events.append(
                RawEvent(
                    event_date=event_date,
                    end_date=end_date,
                    title=title,
                    speaker=speaker,
                    start_time=start_time,
                    place=place,
                    notes=note,
                    source_hint=f"{ws.title}!row",
                )
            )
    else:
        if weekday_header and (not year_hint or not month_hint):
            warnings.append(
                "달력(일~토)으로 보이지만 연·월을 찾지 못했습니다. "
                "시트 이름이나 상단에 ‘2026년 9월’처럼 적어 주세요."
            )
        else:
            warnings.append(
                "날짜·제목 헤더 표도, 일~토 달력 격자도 찾지 못했습니다. "
                "표 상단에 ‘날짜/제목/강사’ 열이 있거나, 요일 헤더가 있는 달력형인지 확인해 주세요."
            )

    # Drawing shapes / text boxes
    if xlsx_path is not None:
        shapes = extract_shapes_from_xlsx(Path(xlsx_path), sheet_name=ws.title)
        shape_dicts = [s.to_dict() for s in shapes]
        shape_events, shape_warnings = shapes_to_events(
            shapes, ws, year=year_hint, month=month_hint
        )
        warnings.extend(shape_warnings)
        # Prefer shape events when they overlap cell titles (shapes are often the real schedule bars)
        if shape_events:
            shape_keys = {
                (e.event_date, e.title) for e in shape_events if e.event_date and e.title
            }
            events = [
                e for e in events if (e.event_date, e.title) not in shape_keys
            ]
            events.extend(shape_events)
        meta["shapeCount"] = len(shapes)
        meta["shapeEventCount"] = len(shape_events)
    else:
        meta["shapeCount"] = 0
        meta["shapeEventCount"] = 0

    if not events:
        if detected and not use_calendar:
            warnings.append("헤더는 찾았지만 추출된 일정이 없습니다.")
        ok = False
    else:
        ok = True

    items = expand_and_normalize(
        events,
        expand_ranges=True,
        force_no_time=False,
        force_no_place=False,
        default_color="auto",
    )
    meta["eventCount"] = len(events)
    meta["itemCount"] = len(items)
    return {
        "ok": ok,
        "events": [e.model_dump(mode="json", by_alias=True) for e in events],
        "items": [i.model_dump() for i in items],
        "warnings": warnings,
        "meta": meta,
        "shapes": shape_dicts,
    }
