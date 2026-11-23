from datetime import date
from pathlib import Path

from openpyxl import Workbook

from schedule_reader.excel.extract import extract_schedules_from_sheet
from schedule_reader.excel.session import session_store


def _save_and_extract(wb: Workbook, name: str = "t.xlsx"):
    path = Path("output") / f"_test_{name}"
    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)
    session = session_store.create(name, path.read_bytes())
    book = session.open_workbook()
    try:
        return extract_schedules_from_sheet(
            book[session.active_sheet],
            xlsx_path=session.path,
        )
    finally:
        book.close()


def test_header_table_extract():
    wb = Workbook()
    ws = wb.active
    ws.title = "9월"
    ws["A1"] = "2026년 9월"
    ws.append(["날짜", "제목", "강사", "시간"])
    ws.append(["2026-09-07", "주일말씀", "홍길동", "10:30"])
    ws.append(["9/10", "수요예배", "김철수", "19:30"])
    result = _save_and_extract(wb, "header.xlsx")
    assert result["ok"] is True
    assert result["meta"]["strategy"] == "header_table"
    assert len(result["items"]) == 2
    assert result["items"][0]["scheduleTitle"] == "주일말씀"
    assert result["items"][0]["sermonSpeaker"] == "홍길동"
    assert result["items"][0]["startTime"] == 1030


def test_calendar_grid_extract():
    wb = Workbook()
    ws = wb.active
    ws.title = "2026년 9월"
    ws.append(["일", "월", "화", "수", "목", "금", "토"])
    # 2026-09-01 is Tuesday → cols: empty, empty, 1, 2, 3, 4, 5
    ws.append(
        [
            "",
            "",
            "1\n개학예배\n홍길동",
            "2",
            "3",
            "4",
            "5",
        ]
    )
    ws.append(["6\n주일말씀\n(김철수)", "7", "8", "9", "10\n수요예배", "11", "12"])
    result = _save_and_extract(wb, "calendar.xlsx")
    assert result["ok"] is True
    assert result["meta"]["strategy"] == "calendar_grid"
    titles = {i["scheduleTitle"]: i for i in result["items"]}
    assert "개학예배" in titles
    assert titles["개학예배"]["scheduleDate"] == "2026-09-01"
    assert titles["개학예배"]["sermonSpeaker"] == "홍길동"
    assert "주일말씀" in titles
    assert titles["주일말씀"]["scheduleDate"] == "2026-09-06"
    assert titles["주일말씀"]["sermonSpeaker"] == "김철수"
