from __future__ import annotations

from datetime import date
from typing import Optional

from pydantic import BaseModel, Field


class RawEvent(BaseModel):
    """Intermediate event extracted from a calendar image."""

    event_date: Optional[date] = Field(
        default=None,
        description="Start date YYYY-MM-DD",
        validation_alias="date",
        serialization_alias="date",
    )
    end_date: Optional[date] = None
    title: str = ""
    speaker: str = ""
    start_time: Optional[str] = Field(
        default=None,
        description="HH:MM 24h, or null if unknown",
    )
    place: str = ""
    notes: str = ""
    confidence: float = 1.0
    source_hint: str = ""

    model_config = {"populate_by_name": True}


class ExtractResult(BaseModel):
    year: int
    month: int
    calendar_title: str = ""
    events: list[RawEvent] = Field(default_factory=list)
    raw_model_notes: str = ""


class ScheduleItem(BaseModel):
    """schedule-data-manager web JSON item."""

    id: str
    scheduleDate: str
    releaseDate: str
    startTime: int = 0
    scheduleTitle: str
    accessoryInfo: str = ""
    place: str = "None"
    sermonSpeaker: str = ""
    preparation: list[str] = Field(default_factory=list)
    preaparationNote: str = ""
    color: str = "auto"
    colorMode: str = "auto"
