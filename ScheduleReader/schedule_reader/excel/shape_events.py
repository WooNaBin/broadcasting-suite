from __future__ import annotations

from calendar import monthrange
from datetime import date
from typing import Any

from openpyxl.worksheet.worksheet import Worksheet

from schedule_reader.excel.calendar_grid import _parse_cell_content
from schedule_reader.excel.preview import cell_text
from schedule_reader.excel.shapes import ShapeText
from schedule_reader.models import RawEvent


def _days_under_shape(
    ws: Worksheet,
    shape: ShapeText,
    *,
    year: int,
    month: int,
) -> list[int]:
    """Collect day numbers from worksheet cells covered by the shape anchor."""
    if shape.from_col is None or shape.from_row is None:
        return []
    days_in_month = monthrange(year, month)[1]
    c0 = shape.from_col
    r0 = shape.from_row
    c1 = shape.to_col if shape.to_col is not None else c0
    r1 = shape.to_row if shape.to_row is not None else r0
    if c1 < c0:
        c0, c1 = c1, c0
    if r1 < r0:
        r0, r1 = r1, r0

    days: list[int] = []
    for r in range(r0, r1 + 1):
        for c in range(c0, c1 + 1):
            # openpyxl is 1-based
            value = ws.cell(row=r + 1, column=c + 1).value
            day, _title, _speaker = _parse_cell_content(cell_text(value))
            if day is not None and 1 <= day <= days_in_month:
                days.append(day)
            else:
                # bare integer day in cell
                if isinstance(value, (int, float)) and not isinstance(value, bool):
                    n = int(value)
                    if 1 <= n <= days_in_month:
                        days.append(n)
    return sorted(set(days))


def shapes_to_events(
    shapes: list[ShapeText],
    ws: Worksheet,
    *,
    year: int | None,
    month: int | None,
) -> tuple[list[RawEvent], list[str]]:
    """
    Convert drawing shape texts into RawEvents.

    Date range is inferred from day numbers in cells under the shape anchor when possible.
    """
    warnings: list[str] = []
    events: list[RawEvent] = []
    if not shapes:
        return events, warnings

    for shape in shapes:
        text = (shape.text or "").strip()
        if not text:
            continue
        # Reuse cell parser: may start with day number inside the shape itself
        day_in_text, title, speaker = _parse_cell_content(text)
        if not title:
            # whole text as title if parser emptied it
            title = text.replace("\n", " ").strip()
        if not title:
            continue

        days: list[int] = []
        if year and month:
            days = _days_under_shape(ws, shape, year=year, month=month)
        if day_in_text and year and month:
            days = sorted(set(days + [day_in_text]))

        if not days:
            warnings.append(
                f"도형 텍스트는 읽었지만 날짜 칸을 못 찾음: {title[:40]}"
            )
            # Still keep as undated? skip for schedule JSON which needs date
            continue

        start = date(year, month, days[0])  # type: ignore[arg-type]
        end = date(year, month, days[-1])  # type: ignore[arg-type]
        events.append(
            RawEvent(
                event_date=start,
                end_date=end if end != start else None,
                title=title,
                speaker=speaker,
                notes="",
                source_hint=f"shape:{shape.name or shape.source}",
                confidence=0.95,
            )
        )

    return events, warnings
