from __future__ import annotations

from pathlib import Path
from typing import Optional

import typer
from rich.console import Console

from schedule_reader.excel.extract import extract_schedules_from_sheet
from schedule_reader.excel.session import session_store
from schedule_reader.export.to_json import write_schedule_json
from schedule_reader.models import ScheduleItem

app = typer.Typer(
    add_completion=False,
    no_args_is_help=True,
    help="교회 일정 엑셀 → schedule-data-manager JSON",
)
console = Console()


@app.command("serve")
def serve(
    host: str = typer.Option("127.0.0.1", "--host"),
    port: int = typer.Option(17823, "--port", "-p"),
    open_browser: bool = typer.Option(True, "--open/--no-open", help="브라우저 자동 실행"),
) -> None:
    """브라우저 UI를 엽니다."""
    import socket
    import threading
    import time
    import urllib.error
    import urllib.request
    import webbrowser

    import uvicorn

    def _can_bind(check_port: int) -> bool:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            try:
                sock.bind((host, check_port))
            except OSError:
                return False
            return True

    def _is_schedule_reader(check_port: int) -> bool:
        try:
            with urllib.request.urlopen(f"http://{host}:{check_port}/", timeout=1.5) as resp:
                body = resp.read(4000).decode("utf-8", errors="ignore")
            return "ScheduleReader" in body
        except (urllib.error.URLError, TimeoutError, OSError):
            return False

    def _pick_port(preferred: int) -> tuple[int, bool]:
        if _can_bind(preferred):
            return preferred, False
        if _is_schedule_reader(preferred):
            return preferred, True
        for candidate in range(preferred + 1, preferred + 20):
            if _can_bind(candidate):
                return candidate, False
        raise RuntimeError(
            f"{preferred}~{preferred + 19} 포트가 모두 사용 중입니다. "
            "기존 서버 창을 종료하거나 --port 로 다른 포트를 지정하세요."
        )

    port, already_running = _pick_port(port)
    url = f"http://{host}:{port}"

    if already_running:
        console.print(f"[yellow]이미 실행 중입니다 → {url}[/yellow]")
        if open_browser:
            webbrowser.open(url)
        console.print("브라우저를 다시 열었습니다. 서버를 끄려면 기존 실행 창에서 Ctrl+C")
        return

    console.print(f"[green]ScheduleReader UI → {url}[/green]")

    if open_browser:

        def _open() -> None:
            time.sleep(0.8)
            webbrowser.open(url)

        threading.Thread(target=_open, daemon=True).start()

    uvicorn.run(
        "schedule_reader.web.app:app",
        host=host,
        port=port,
        reload=False,
        log_level="info",
    )


@app.command("extract")
def extract_cmd(
    workbook: Path = typer.Argument(..., exists=True, readable=True),
    output: Path = typer.Option(Path("output/schedule-data.json"), "--output", "-o"),
    sheet: Optional[str] = typer.Option(None, "--sheet", "-s", help="시트 이름 (기본: 첫 시트)"),
) -> None:
    """엑셀 → schedule-data-manager JSON."""
    content = workbook.read_bytes()
    session = session_store.create(workbook.name, content)
    target = sheet or session.active_sheet
    if target not in session.sheet_names:
        raise typer.BadParameter(f"시트 없음: {target} / 가능={session.sheet_names}")
    wb = session.open_workbook(data_only=True)
    try:
        result = extract_schedules_from_sheet(wb[target], xlsx_path=session.path)
    finally:
        wb.close()
    if not result.get("ok"):
        for w in result.get("warnings") or []:
            console.print(f"[yellow]{w}[/yellow]")
        raise typer.Exit(code=1)
    items = [ScheduleItem.model_validate(x) for x in result["items"]]
    write_schedule_json(items, output)
    shape_n = (result.get("meta") or {}).get("shapeEventCount", 0)
    console.print(
        f"[green]{len(items)}건 추출 → {output}[/green] "
        f"(sheet={target}, 도형일정={shape_n})"
    )


if __name__ == "__main__":
    app()
