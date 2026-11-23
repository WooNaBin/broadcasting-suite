from __future__ import annotations

import re
import uuid
from datetime import date, timedelta
from pathlib import Path
from typing import Any

import yaml

from schedule_reader.models import RawEvent, ScheduleItem

PLACE_CONFIG = Path(__file__).resolve().parents[2] / "config" / "place_map.yaml"

VALID_PLACES = {
    "None",
    "Main_Large",
    "Main_Medium",
    "Main_Small",
    "Edu_Large",
    "Edu_Medium",
    "Edu_Small",
    "Edu_Nongseon",
    "Edu_Siseon",
    "Edu_Go2",
    "Etc",
}


def load_place_map(path: Path | None = None) -> dict[str, Any]:
    cfg_path = path or PLACE_CONFIG
    with cfg_path.open(encoding="utf-8") as f:
        return yaml.safe_load(f) or {}


def map_place(raw: str, place_cfg: dict[str, Any] | None = None) -> str:
    cfg = place_cfg if place_cfg is not None else load_place_map()
    default = str(cfg.get("default") or "None")
    text = (raw or "").strip()
    if not text:
        return default if default in VALID_PLACES else "None"
    if text in VALID_PLACES:
        return text
    mappings: dict[str, str] = cfg.get("mappings") or {}
    # Longer keys first for better match
    for key in sorted(mappings.keys(), key=len, reverse=True):
        if key in text:
            value = mappings[key]
            return value if value in VALID_PLACES else default
    return default if default in VALID_PLACES else "None"


def parse_hhmm(value: str | None) -> int:
    """Convert 'HH:MM' / 'HHmm' / 'HHMM' to HHmm int on 10-minute grid."""
    if not value:
        return 0
    s = str(value).strip()
    m = re.match(r"^(\d{1,2}):(\d{2})$", s)
    if m:
        hour, minute = int(m.group(1)), int(m.group(2))
    else:
        digits = re.sub(r"\D", "", s)
        if len(digits) in (3, 4):
            digits = digits.zfill(4)
            hour, minute = int(digits[:2]), int(digits[2:])
        else:
            return 0
    if hour < 0 or hour > 23 or minute < 0 or minute > 59:
        return 0
    minute = (minute // 10) * 10
    return hour * 100 + minute


def _daterange(start: date, end: date) -> list[date]:
    if end < start:
        start, end = end, start
    days: list[date] = []
    cur = start
    while cur <= end:
        days.append(cur)
        cur += timedelta(days=1)
    return days


def expand_and_normalize(
    events: list[RawEvent],
    *,
    expand_ranges: bool = True,
    place_cfg: dict[str, Any] | None = None,
    default_color: str = "auto",
    force_no_time: bool = True,
    force_no_place: bool = True,
) -> list[ScheduleItem]:
    place_cfg = place_cfg if place_cfg is not None else load_place_map()
    items: list[ScheduleItem] = []

    for event in events:
        title = (event.title or "").strip()
        if not title:
            continue
        start = event.event_date
        end = event.end_date or event.event_date
        if start is None:
            continue
        if end is None:
            end = start

        dates = _daterange(start, end) if expand_ranges else [start]
        start_time = 0 if force_no_time else parse_hhmm(event.start_time)
        place = "None" if force_no_place else map_place(event.place, place_cfg)
        speaker = (event.speaker or "").strip()
        notes = (event.notes or "").strip()
        color_token = (default_color or "auto").strip() or "auto"
        color_lower = color_token.lower()
        color_mode = color_lower if color_lower in ("auto", "none") else "custom"
        if color_mode in ("auto", "none"):
            color_token = color_mode

        for d in dates:
            iso = d.isoformat()
            items.append(
                ScheduleItem(
                    id=str(uuid.uuid4()),
                    scheduleDate=iso,
                    releaseDate=iso,
                    startTime=start_time,
                    scheduleTitle=title,
                    accessoryInfo=notes,
                    place=place,
                    sermonSpeaker=speaker,
                    preparation=[],
                    preaparationNote="",
                    color=color_token,
                    colorMode=color_mode,
                )
            )
    items.sort(key=lambda x: (x.scheduleDate, x.startTime, x.scheduleTitle))
    return items
