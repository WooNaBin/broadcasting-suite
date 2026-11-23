from __future__ import annotations

import tempfile
import threading
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from openpyxl import load_workbook
from openpyxl.workbook import Workbook


@dataclass
class WorkbookSession:
    id: str
    filename: str
    path: Path
    sheet_names: list[str] = field(default_factory=list)
    active_sheet: str = ""
    extract_items: list[dict[str, Any]] = field(default_factory=list)
    extract_events: list[dict[str, Any]] = field(default_factory=list)
    extract_ok: bool = False
    extract_warnings: list[str] = field(default_factory=list)
    extract_meta: dict[str, Any] = field(default_factory=dict)

    def open_workbook(self, data_only: bool = True) -> Workbook:
        return load_workbook(self.path, data_only=data_only, read_only=False)


class SessionStore:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._sessions: dict[str, WorkbookSession] = {}
        self._tmpdir = Path(tempfile.mkdtemp(prefix="schedule_reader_xlsx_"))

    def create(self, filename: str, content: bytes) -> WorkbookSession:
        sid = uuid.uuid4().hex
        safe_name = Path(filename or "schedule.xlsx").name
        if not safe_name.lower().endswith((".xlsx", ".xlsm")):
            raise ValueError("엑셀 파일(.xlsx / .xlsm)만 지원합니다.")
        path = self._tmpdir / f"{sid}_{safe_name}"
        path.write_bytes(content)
        wb = load_workbook(path, data_only=True, read_only=True)
        try:
            names = list(wb.sheetnames)
        finally:
            wb.close()
        if not names:
            raise ValueError("시트가 없는 엑셀입니다.")
        session = WorkbookSession(
            id=sid,
            filename=safe_name,
            path=path,
            sheet_names=names,
            active_sheet=names[0],
        )
        with self._lock:
            self._sessions[sid] = session
        return session

    def get(self, session_id: str) -> WorkbookSession | None:
        with self._lock:
            return self._sessions.get(session_id)

    def drop(self, session_id: str) -> None:
        with self._lock:
            session = self._sessions.pop(session_id, None)
        if session and session.path.is_file():
            try:
                session.path.unlink()
            except OSError:
                pass


session_store = SessionStore()
