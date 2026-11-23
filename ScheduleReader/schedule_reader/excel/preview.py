from __future__ import annotations

from typing import Any

from openpyxl.worksheet.worksheet import Worksheet


def cell_text(value: Any) -> str:
    if value is None:
        return ""
    if hasattr(value, "isoformat"):
        try:
            return value.isoformat()
        except Exception:
            pass
    return str(value).strip()


def sheet_preview(ws: Worksheet, max_rows: int = 30, max_cols: int = 20) -> dict[str, Any]:
    rows: list[list[str]] = []
    for i, row in enumerate(ws.iter_rows(max_row=max_rows, max_col=max_cols, values_only=True), start=1):
        cells = [cell_text(v) for v in row]
        if any(cells):
            rows.append(cells)
        if i >= max_rows:
            break
    return {
        "sheet": ws.title,
        "rowCountHint": ws.max_row or 0,
        "colCountHint": ws.max_column or 0,
        "rows": rows,
    }
