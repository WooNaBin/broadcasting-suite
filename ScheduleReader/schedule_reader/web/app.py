from __future__ import annotations

import json
from pathlib import Path

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse, JSONResponse, Response
from fastapi.staticfiles import StaticFiles

from schedule_reader.excel.extract import extract_schedules_from_sheet
from schedule_reader.excel.preview import sheet_preview
from schedule_reader.excel.session import session_store

WEB_DIR = Path(__file__).resolve().parent
STATIC_DIR = WEB_DIR / "static"

app = FastAPI(title="ScheduleReader", docs_url=None, redoc_url=None)
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")


@app.get("/")
def index() -> FileResponse:
    return FileResponse(STATIC_DIR / "index.html")


@app.post("/api/workbook")
async def upload_workbook(file: UploadFile = File(...)) -> dict:
    raw = await file.read()
    if not raw:
        raise HTTPException(400, "빈 파일입니다.")
    try:
        session = session_store.create(file.filename or "schedule.xlsx", raw)
    except ValueError as exc:
        raise HTTPException(400, str(exc)) from exc
    except Exception as exc:  # noqa: BLE001
        raise HTTPException(400, f"엑셀을 열 수 없습니다: {exc}") from exc

    wb = session.open_workbook(data_only=True)
    try:
        ws = wb[session.active_sheet]
        preview = sheet_preview(ws)
    finally:
        wb.close()

    return {
        "sessionId": session.id,
        "filename": session.filename,
        "sheets": session.sheet_names,
        "activeSheet": session.active_sheet,
        "preview": preview,
        "extractReady": False,
    }


@app.post("/api/workbook/sheet")
async def select_sheet(sessionId: str = Form(...), sheet: str = Form(...)) -> dict:
    session = session_store.get(sessionId)
    if not session:
        raise HTTPException(404, "불러온 엑셀 세션이 없습니다. 다시 불러오세요.")
    if sheet not in session.sheet_names:
        raise HTTPException(400, "없는 시트입니다.")
    session.active_sheet = sheet
    session.extract_ok = False
    session.extract_items = []
    session.extract_events = []
    session.extract_warnings = []
    wb = session.open_workbook(data_only=True)
    try:
        preview = sheet_preview(wb[sheet])
    finally:
        wb.close()
    return {
        "sessionId": session.id,
        "activeSheet": sheet,
        "preview": preview,
        "extractReady": False,
    }


@app.post("/api/extract")
async def extract(sessionId: str = Form(...), sheet: str | None = Form(None)) -> dict:
    session = session_store.get(sessionId)
    if not session:
        raise HTTPException(404, "불러온 엑셀 세션이 없습니다. 다시 불러오세요.")
    target = sheet or session.active_sheet
    if target not in session.sheet_names:
        raise HTTPException(400, "없는 시트입니다.")
    session.active_sheet = target
    wb = session.open_workbook(data_only=True)
    try:
        result = extract_schedules_from_sheet(wb[target], xlsx_path=session.path)
    finally:
        wb.close()

    session.extract_ok = bool(result.get("ok"))
    session.extract_events = list(result.get("events") or [])
    session.extract_items = list(result.get("items") or [])
    session.extract_warnings = list(result.get("warnings") or [])
    session.extract_meta = dict(result.get("meta") or {})

    return {
        "ok": session.extract_ok,
        "sessionId": session.id,
        "sheet": target,
        "eventCount": len(session.extract_events),
        "itemCount": len(session.extract_items),
        "warnings": session.extract_warnings,
        "meta": session.extract_meta,
        "events": session.extract_events[:200],
        "items": session.extract_items[:200],
        "shapes": (result.get("shapes") or [])[:200],
        "extractReady": session.extract_ok and bool(session.extract_items),
    }


@app.get("/api/export")
def export_json(sessionId: str) -> Response:
    session = session_store.get(sessionId)
    if not session:
        raise HTTPException(404, "불러온 엑셀 세션이 없습니다.")
    if not session.extract_ok or not session.extract_items:
        raise HTTPException(400, "일정 추출이 아직 성공하지 않았습니다.")
    body = json.dumps(session.extract_items, ensure_ascii=False, indent=2) + "\n"
    filename = "schedule-data.json"
    return Response(
        content=body.encode("utf-8"),
        media_type="application/json; charset=utf-8",
        headers={
            "Content-Disposition": f'attachment; filename="{filename}"',
        },
    )


@app.get("/api/health")
def health() -> JSONResponse:
    return JSONResponse({"ok": True, "app": "ScheduleReader", "mode": "excel"})
