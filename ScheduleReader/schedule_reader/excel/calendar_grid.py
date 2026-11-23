from __future__ import annotations

import re
from calendar import monthrange
from datetime import date
from typing import Any

from openpyxl.worksheet.worksheet import Worksheet

from schedule_reader.excel.preview import cell_text
from schedule_reader.models import RawEvent

WEEKDAY_MARKERS = ("일", "월", "화", "수", "목", "금", "토")
DAY_ONLY_RE = re.compile(r"^(\d{1,2})(?:\s*[일日])?$")
SPEAKER_PAREN_RE = re.compile(r"[\(（]\s*([가-힣A-Za-z]{2,4})\s*[\)）]")
SPEAKER_LABEL_RE = re.compile(r"(?:강사|설교|설교자|인도)\s*[:：]?\s*([가-힣A-Za-z]{2,4})")
NAME_ONLY_RE = re.compile(r"^[가-힣]{2,4}$")
TITLE_HINT_RE = re.compile(
    r"(예배|모임|기도|말씀|찬양|특새|새벽|주일|수요|금요|청년|부흥|집회|세미나|회의|훈련)"
)


def _split_lines(text: str) -> list[str]:
    parts = re.split(r"[\r\n/;｜|]+", text)
    return [p.strip() for p in parts if p and p.strip()]


def _parse_cell_content(text: str) -> tuple[int | None, str, str]:
    """Return (day_number, title, speaker) from a calendar cell."""
    lines = _split_lines(text)
    if not lines:
        return None, "", ""

    day = None
    first = lines[0]
    m = DAY_ONLY_RE.match(first)
    if m:
        day = int(m.group(1))
        lines = lines[1:]
    else:
        m2 = re.match(r"^(\d{1,2})\s+(.+)$", first)
        if m2:
            day = int(m2.group(1))
            lines = [m2.group(2).strip()] + lines[1:]

    if not lines:
        return day, "", ""

    speaker = ""
    title_lines = list(lines)
    for i, line in enumerate(lines):
        sm = SPEAKER_LABEL_RE.search(line)
        if sm:
            speaker = sm.group(1)
            cleaned = SPEAKER_LABEL_RE.sub("", line).strip(" -–—:/")
            title_lines = lines[:i] + ([cleaned] if cleaned else []) + lines[i + 1 :]
            break
        sm = SPEAKER_PAREN_RE.search(line)
        if sm:
            speaker = sm.group(1)
            cleaned = SPEAKER_PAREN_RE.sub("", line).strip(" -–—:/")
            title_lines = lines[:i] + ([cleaned] if cleaned else []) + lines[i + 1 :]
            break
    else:
        if (
            len(title_lines) >= 2
            and NAME_ONLY_RE.match(title_lines[-1])
            and not TITLE_HINT_RE.search(title_lines[-1])
        ):
            speaker = title_lines[-1]
            title_lines = title_lines[:-1]

    title = " ".join(x for x in title_lines if x).strip()
    return day, title, speaker


def _find_weekday_header(matrix: list[list[str]]) -> tuple[int, int] | None:
    """Return (row_index, start_col) for a Sun–Sat header row."""
    for r, row in enumerate(matrix[:25]):
        for start in range(0, max(0, len(row) - 6)):
            window = row[start : start + 7]
            if not all(window):
                continue
            heads = [re.sub(r"\s+", "", t) for t in window]
            if all(heads[i].startswith(WEEKDAY_MARKERS[i]) for i in range(7)):
                return r, start
    return None


def _row_has_day(row: list[Any], start_col: int, days_in_month: int) -> bool:
    for c in range(start_col, start_col + 7):
        if c >= len(row):
            continue
        day, _, _ = _parse_cell_content(cell_text(row[c]))
        if day is not None and 1 <= day <= days_in_month:
            return True
    return False


def extract_calendar_grid(
    ws: Worksheet,
    *,
    year: int | None,
    month: int | None,
) -> dict[str, Any]:
    """Extract events from a monthly calendar sheet (7 weekday columns, Sun–Sat)."""
    warnings: list[str] = []
    if not year or not month:
        return {
            "ok": False,
            "events": [],
            "warnings": ["달력형 추출에는 연·월 힌트가 필요합니다."],
            "meta": {"strategy": "calendar_grid"},
        }

    max_row = min(ws.max_row or 0, 80)
    max_col = min(ws.max_column or 0, 20)
    matrix: list[list[Any]] = [
        list(row)
        for row in ws.iter_rows(min_row=1, max_row=max_row, max_col=max_col, values_only=True)
    ]
    text_matrix = [[cell_text(c) for c in row] for row in matrix]

    header = _find_weekday_header(text_matrix)
    if not header:
        return {
            "ok": False,
            "events": [],
            "warnings": ["요일(일~토) 헤더 행을 찾지 못했습니다."],
            "meta": {"strategy": "calendar_grid"},
        }

    header_row, start_col = header
    days_in_month = monthrange(year, month)[1]
    events: list[RawEvent] = []

    r = header_row + 1
    while r < len(matrix):
        row = matrix[r]
        next_row = matrix[r + 1] if r + 1 < len(matrix) else []
        merge_next = bool(next_row) and not _row_has_day(
            next_row, start_col, days_in_month
        )

        for offset in range(7):
            c = start_col + offset
            text = cell_text(row[c]) if c < len(row) else ""
            if merge_next and c < len(next_row):
                extra = cell_text(next_row[c])
                if extra:
                    text = f"{text}\n{extra}".strip()
            day, title, speaker = _parse_cell_content(text)
            if day is None or not (1 <= day <= days_in_month) or not title:
                continue
            events.append(
                RawEvent(
                    event_date=date(year, month, day),
                    title=title,
                    speaker=speaker,
                    source_hint=f"{ws.title}!cal r{r + 1}c{c + 1}",
                )
            )
        r += 2 if merge_next else 1

    uniq: dict[tuple, RawEvent] = {}
    for event in events:
        uniq[(event.event_date, event.title, event.speaker)] = event
    events = sorted(uniq.values(), key=lambda e: (e.event_date or date.min, e.title))

    if not events:
        warnings.append("달력 격자는 찾았지만 일정 텍스트를 읽지 못했습니다.")
        ok = False
    else:
        ok = True

    return {
        "ok": ok,
        "events": events,
        "warnings": warnings,
        "meta": {
            "strategy": "calendar_grid",
            "headerRow": header_row + 1,
            "startCol": start_col + 1,
            "year": year,
            "month": month,
            "eventCount": len(events),
        },
    }
