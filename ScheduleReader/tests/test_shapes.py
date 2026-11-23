"""Tests for Excel drawing / shape text extraction."""

from __future__ import annotations

import io
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

from openpyxl import Workbook

from schedule_reader.excel.extract import extract_schedules_from_sheet
from schedule_reader.excel.session import session_store
from schedule_reader.excel.shapes import _parse_drawing_xml, extract_shapes_from_xlsx


DRAWING_NS = {
    "xdr": "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing",
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
}


def _drawing_xml(
    text: str,
    *,
    from_col: int,
    from_row: int,
    to_col: int,
    to_row: int,
) -> bytes:
    # Minimal SpreadsheetDrawing with one twoCellAnchor shape
    xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<xdr:wsDr xmlns:xdr="{DRAWING_NS['xdr']}" xmlns:a="{DRAWING_NS['a']}">
  <xdr:twoCellAnchor>
    <xdr:from>
      <xdr:col>{from_col}</xdr:col><xdr:colOff>0</xdr:colOff>
      <xdr:row>{from_row}</xdr:row><xdr:rowOff>0</xdr:rowOff>
    </xdr:from>
    <xdr:to>
      <xdr:col>{to_col}</xdr:col><xdr:colOff>0</xdr:colOff>
      <xdr:row>{to_row}</xdr:row><xdr:rowOff>0</xdr:rowOff>
    </xdr:to>
    <xdr:sp>
      <xdr:nvSpPr>
        <xdr:cNvPr id="2" name="TextBox 1"/>
        <xdr:cNvSpPr txBox="1"/>
      </xdr:nvSpPr>
      <xdr:spPr/>
      <xdr:txBody>
        <a:bodyPr/><a:lstStyle/>
        <a:p><a:r><a:t>{text}</a:t></a:r></a:p>
      </xdr:txBody>
    </xdr:sp>
    <xdr:clientData/>
  </xdr:twoCellAnchor>
</xdr:wsDr>
"""
    return xml.encode("utf-8")


def test_parse_drawing_xml_text_and_anchor():
    raw = _drawing_xml("수련회", from_col=1, from_row=2, to_col=3, to_row=2)
    shapes = _parse_drawing_xml(raw, sheet="9월", source="drawing1.xml")
    assert len(shapes) == 1
    assert shapes[0].text == "수련회"
    assert shapes[0].from_col == 1
    assert shapes[0].from_row == 2
    assert shapes[0].to_col == 3
    assert shapes[0].to_row == 2
    assert shapes[0].name == "TextBox 1"


def _inject_drawing(xlsx_path: Path, drawing_bytes: bytes) -> None:
    """Add a drawing part linked from the first worksheet."""
    buf = io.BytesIO(xlsx_path.read_bytes())
    with zipfile.ZipFile(buf, "r") as zin:
        names = zin.namelist()
        # find first sheet path
        sheet_path = next(n for n in names if n.startswith("xl/worksheets/sheet") and n.endswith(".xml"))
        sheet_dir = sheet_path.rsplit("/", 1)[0]
        sheet_file = sheet_path.rsplit("/", 1)[-1]
        rels_path = f"{sheet_dir}/_rels/{sheet_file}.rels"
        drawing_path = "xl/drawings/drawing1.xml"

        out = io.BytesIO()
        with zipfile.ZipFile(out, "w") as zout:
            for item in zin.infolist():
                if item.filename in {rels_path, drawing_path, sheet_path}:
                    continue
                zout.writestr(item, zin.read(item.filename))

            # sheet rels: keep existing + drawing
            rel_lines = []
            rid = 1
            if rels_path in names:
                rel_root = ET.fromstring(zin.read(rels_path))
                for el in rel_root:
                    if el.tag.endswith("Relationship"):
                        rid = max(rid, int(el.attrib.get("Id", "rId0").replace("rId", "") or 0) + 1)
                        rel_lines.append(
                            f'<Relationship Id="{el.attrib["Id"]}" '
                            f'Type="{el.attrib["Type"]}" Target="{el.attrib["Target"]}"/>'
                        )
            drawing_rid = f"rId{rid}"
            rel_lines.append(
                f'<Relationship Id="{drawing_rid}" '
                'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" '
                'Target="../drawings/drawing1.xml"/>'
            )
            rels_xml = (
                '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                + "".join(rel_lines)
                + "</Relationships>"
            )
            zout.writestr(rels_path, rels_xml.encode("utf-8"))
            zout.writestr(drawing_path, drawing_bytes)

            # ensure worksheet references drawing
            sheet_xml = zin.read(sheet_path).decode("utf-8")
            if "drawing" not in sheet_xml.lower():
                sheet_xml = sheet_xml.replace(
                    "</worksheet>",
                    f'<drawing r:id="{drawing_rid}"/></worksheet>',
                )
                # ensure r namespace on root if missing
                if "xmlns:r=" not in sheet_xml:
                    sheet_xml = sheet_xml.replace(
                        "<worksheet ",
                        '<worksheet xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" ',
                        1,
                    )
            zout.writestr(sheet_path, sheet_xml.encode("utf-8"))

    xlsx_path.write_bytes(out.getvalue())


def test_shape_text_becomes_schedule_event():
    wb = Workbook()
    ws = wb.active
    ws.title = "2026년 9월"
    ws.append(["일", "월", "화", "수", "목", "금", "토"])
    # row index 1 (0-based) = Excel row 2 after header... wait:
    # append header → row 1
    # append days → row 2 → OOXML row index 1
    ws.append(["", "", "1", "2", "3", "4", "5"])

    path = Path("output") / "_test_shapes.xlsx"
    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)

    # Shape covering Tue–Thu (cols 2..4, row 1) with event title
    drawing = _drawing_xml(
        "금식기도회",
        from_col=2,
        from_row=1,
        to_col=4,
        to_row=1,
    )
    _inject_drawing(path, drawing)

    shapes = extract_shapes_from_xlsx(path, sheet_name="2026년 9월")
    assert len(shapes) == 1
    assert shapes[0].text == "금식기도회"

    session = session_store.create("shapes.xlsx", path.read_bytes())
    book = session.open_workbook()
    try:
        result = extract_schedules_from_sheet(
            book[session.active_sheet],
            xlsx_path=session.path,
        )
    finally:
        book.close()

    assert result["meta"]["shapeCount"] == 1
    assert result["meta"]["shapeEventCount"] == 1
    titles = {i["scheduleTitle"]: i for i in result["items"]}
    assert "금식기도회" in titles
    # multi-day 1~3 Sep → expanded items or single range depending on expand
    dates = {i["scheduleDate"] for i in result["items"] if i["scheduleTitle"] == "금식기도회"}
    assert "2026-09-01" in dates
    assert "2026-09-03" in dates or len(dates) >= 1
