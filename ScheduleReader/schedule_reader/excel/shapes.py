from __future__ import annotations

import re
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from xml.etree import ElementTree as ET

# SpreadsheetDrawing / DrawingML namespaces
NS = {
    "xdr": "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing",
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
    "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
    "pr": "http://schemas.openxmlformats.org/package/2006/relationships",
    "main": "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
}


@dataclass
class ShapeText:
    """Text extracted from an Excel drawing shape / text box."""

    text: str
    name: str = ""
    # 0-based cell anchors (Excel col/row in OOXML are 0-based)
    from_col: int | None = None
    from_row: int | None = None
    to_col: int | None = None
    to_row: int | None = None
    sheet: str = ""
    source: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "text": self.text,
            "name": self.name,
            "fromCol": self.from_col,
            "fromRow": self.from_row,
            "toCol": self.to_col,
            "toRow": self.to_row,
            "sheet": self.sheet,
            "source": self.source,
        }


def _local(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[-1]
    return tag


def _child(el: ET.Element, *names: str) -> ET.Element | None:
    want = set(names)
    for child in list(el):
        if _local(child.tag) in want:
            return child
    return None


def _findall_local(el: ET.Element, name: str) -> list[ET.Element]:
    return [c for c in el.iter() if _local(c.tag) == name]


def _anchor_cell(marker: ET.Element | None) -> tuple[int | None, int | None]:
    if marker is None:
        return None, None
    col_el = _child(marker, "col")
    row_el = _child(marker, "row")
    col = int(col_el.text) if col_el is not None and col_el.text is not None else None
    row = int(row_el.text) if row_el is not None and row_el.text is not None else None
    return col, row


def _txbody_text(tx_body: ET.Element) -> str:
    paragraphs: list[str] = []
    for p in list(tx_body):
        if _local(p.tag) != "p":
            continue
        parts: list[str] = []
        for node in p.iter():
            if _local(node.tag) == "t" and node.text:
                parts.append(node.text)
        paragraphs.append("".join(parts))
    # Keep empty lines between paragraphs
    text = "\n".join(paragraphs).strip()
    text = re.sub(r"[ \t]+\n", "\n", text)
    return text.strip()


def _shape_name(sp: ET.Element) -> str:
    # xdr:nvSpPr / xdr:cNvPr @name
    nv = _child(sp, "nvSpPr")
    if nv is None:
        return ""
    cnv = _child(nv, "cNvPr")
    if cnv is None:
        return ""
    return (cnv.attrib.get("name") or "").strip()


def _extract_from_anchor(anchor: ET.Element, *, sheet: str, source: str) -> list[ShapeText]:
    from_el = _child(anchor, "from")
    to_el = _child(anchor, "to")
    from_col, from_row = _anchor_cell(from_el)
    to_col, to_row = _anchor_cell(to_el)
    if to_col is None:
        to_col = from_col
    if to_row is None:
        to_row = from_row

    results: list[ShapeText] = []
    # Single shape
    for sp in list(anchor):
        local = _local(sp.tag)
        if local == "sp":
            tx = _child(sp, "txBody")
            if tx is None:
                continue
            text = _txbody_text(tx)
            if not text:
                continue
            results.append(
                ShapeText(
                    text=text,
                    name=_shape_name(sp),
                    from_col=from_col,
                    from_row=from_row,
                    to_col=to_col,
                    to_row=to_row,
                    sheet=sheet,
                    source=source,
                )
            )
        elif local == "grpSp":
            # Flatten group: use same anchor for child shapes' text
            for nested in _findall_local(sp, "sp"):
                tx = _child(nested, "txBody")
                if tx is None:
                    continue
                text = _txbody_text(tx)
                if not text:
                    continue
                results.append(
                    ShapeText(
                        text=text,
                        name=_shape_name(nested),
                        from_col=from_col,
                        from_row=from_row,
                        to_col=to_col,
                        to_row=to_row,
                        sheet=sheet,
                        source=source,
                    )
                )
    return results


def _parse_drawing_xml(xml_bytes: bytes, *, sheet: str, source: str) -> list[ShapeText]:
    try:
        root = ET.fromstring(xml_bytes)
    except ET.ParseError:
        return []
    out: list[ShapeText] = []
    for anchor in list(root):
        tag = _local(anchor.tag)
        if tag in {"twoCellAnchor", "oneCellAnchor", "absoluteAnchor"}:
            out.extend(_extract_from_anchor(anchor, sheet=sheet, source=source))
    return out


def _sheet_target_to_name(workbook_xml: bytes) -> list[tuple[str, str]]:
    """Return list of (sheet_name, rId) from workbook.xml."""
    root = ET.fromstring(workbook_xml)
    sheets: list[tuple[str, str]] = []
    for el in root.iter():
        if _local(el.tag) != "sheet":
            continue
        name = el.attrib.get("name") or ""
        rid = el.attrib.get(
            "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"
        ) or el.attrib.get("id")
        if name and rid:
            sheets.append((name, rid))
    return sheets


def _rels_map(rels_xml: bytes) -> dict[str, str]:
    root = ET.fromstring(rels_xml)
    mapping: dict[str, str] = {}
    for el in root.iter():
        if _local(el.tag) != "Relationship":
            continue
        rid = el.attrib.get("Id")
        target = el.attrib.get("Target")
        if rid and target:
            mapping[rid] = target.replace("\\", "/")
    return mapping


def _resolve_path(base_dir: str, target: str) -> str:
    target = target.replace("\\", "/")
    if target.startswith("/"):
        return target.lstrip("/")
    parts = base_dir.strip("/").split("/") if base_dir else []
    for piece in target.split("/"):
        if piece in ("", "."):
            continue
        if piece == "..":
            if parts:
                parts.pop()
            continue
        parts.append(piece)
    return "/".join(parts)


def extract_shapes_from_xlsx(
    path: str | Path,
    *,
    sheet_name: str | None = None,
) -> list[ShapeText]:
    """
    Read text from drawing shapes / text boxes in an .xlsx/.xlsm file.

    Positions use OOXML 0-based column/row indices from twoCell/oneCell anchors.
    """
    path = Path(path)
    shapes: list[ShapeText] = []
    with zipfile.ZipFile(path, "r") as zf:
        names = set(zf.namelist())
        if "xl/workbook.xml" not in names or "xl/_rels/workbook.xml.rels" not in names:
            return []

        sheet_entries = _sheet_target_to_name(zf.read("xl/workbook.xml"))
        wb_rels = _rels_map(zf.read("xl/_rels/workbook.xml.rels"))

        for name, rid in sheet_entries:
            if sheet_name and name != sheet_name:
                continue
            target = wb_rels.get(rid)
            if not target:
                continue
            sheet_path = _resolve_path("xl", target)
            # sheet rels
            sheet_dir = sheet_path.rsplit("/", 1)[0]
            sheet_file = sheet_path.rsplit("/", 1)[-1]
            rels_path = f"{sheet_dir}/_rels/{sheet_file}.rels"
            if rels_path not in names:
                continue
            rels = _rels_map(zf.read(rels_path))
            for rel_target in rels.values():
                if "drawing" not in rel_target.lower() or not rel_target.endswith(".xml"):
                    continue
                drawing_path = _resolve_path(sheet_dir, rel_target)
                if drawing_path not in names:
                    # sometimes target is ../drawings/drawing1.xml already resolved wrong
                    alt = rel_target.lstrip("/")
                    if alt.startswith("xl/"):
                        drawing_path = alt
                    elif "drawings/" in alt:
                        drawing_path = "xl/" + alt.split("xl/")[-1] if "xl/" in alt else f"xl/{alt}"
                    # common: ../drawings/drawing1.xml from worksheets/
                    if drawing_path not in names and "drawings/" in rel_target:
                        drawing_path = "xl/drawings/" + rel_target.rsplit("/", 1)[-1]
                if drawing_path not in names:
                    continue
                shapes.extend(
                    _parse_drawing_xml(
                        zf.read(drawing_path),
                        sheet=name,
                        source=drawing_path,
                    )
                )
    return shapes
