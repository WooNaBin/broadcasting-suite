from __future__ import annotations

import json
from pathlib import Path

from schedule_reader.models import ExtractResult, RawEvent, ScheduleItem


def write_schedule_json(items: list[ScheduleItem], path: Path) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = [item.model_dump() for item in items]
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def write_extract_json(result: ExtractResult, path: Path) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        result.model_dump_json(indent=2) + "\n",
        encoding="utf-8",
    )


def load_raw_events(path: Path) -> list[RawEvent]:
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if isinstance(data, dict) and "events" in data:
        return [RawEvent.model_validate(e) for e in data["events"]]
    if isinstance(data, list):
        return [RawEvent.model_validate(e) for e in data]
    raise ValueError("JSON must be ExtractResult object or RawEvent list")
