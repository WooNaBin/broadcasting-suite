from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml

from schedule_reader.models import RawEvent

DEFAULT_CONFIG = Path(__file__).resolve().parents[2] / "config" / "filters.yaml"


def load_filter_config(path: Path | None = None) -> dict[str, Any]:
    cfg_path = path or DEFAULT_CONFIG
    with cfg_path.open(encoding="utf-8") as f:
        return yaml.safe_load(f) or {}


def apply_filters(
    events: list[RawEvent],
    config: dict[str, Any] | None = None,
) -> list[RawEvent]:
    cfg = config if config is not None else load_filter_config()
    exclude = [str(x).lower() for x in (cfg.get("exclude_keywords") or [])]
    include = [str(x).lower() for x in (cfg.get("include_keywords") or [])]
    drop_empty = bool(cfg.get("drop_empty_title", True))

    kept: list[RawEvent] = []
    for event in events:
        title = (event.title or "").strip()
        if drop_empty and not title:
            continue
        text = f"{title} {event.notes or ''}".lower()
        if any(k and k in text for k in exclude):
            continue
        if include and not any(k and k in text for k in include):
            continue
        kept.append(event)
    return kept
