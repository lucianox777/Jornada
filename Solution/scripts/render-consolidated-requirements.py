#!/usr/bin/env python3
"""Render the consolidated requirements baseline to institutional DOCX/PDF.

All institutional tables are materialized as real Word Table Grid structures.
This avoids the narrow/stacked table layout that LibreOffice may produce from
Pandoc-generated auto-width tables in the consolidated portrait documents.
"""
from __future__ import annotations

import argparse
import importlib.util
import re
import subprocess
from pathlib import Path

from docx import Document
from docx.enum.section import WD_ORIENT
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.shared import Cm, Pt


def load_helper(root: Path):
    path = root / "Solution" / "scripts" / "generate-document-deliverables.py"
    spec = importlib.util.spec_from_file_location("jornada_document_helper", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load document helper")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def convert_pdf(docx: Path) -> Path:
    subprocess.run(
        ["libreoffice", "--headless", "--convert-to", "pdf", "--outdir", str(docx.parent), str(docx)],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    pdf = docx.with_suffix(".pdf")
    if not pdf.is_file() or pdf.stat().st_size < 1000:
        raise RuntimeError(f"invalid PDF: {pdf}")
    return pdf


def parse_table_block(block: list[str]) -> list[list[str]]:
    rows: list[list[str]] = []
    for pos, raw in enumerate(block):
        values = [v.strip() for v in raw.strip().strip("|").split("|")]
        if pos == 1 and all(re.fullmatch(r":?-{3,}:?", v) for v in values):
            continue
        rows.append(values)
    if rows:
        ncol = len(rows[0])
        if any(len(row) != ncol for row in rows):
            raise RuntimeError(f"inconsistent Markdown table with expected {ncol} columns")
    return rows


def portrait_widths(ncol: int) -> list[float]:
    # Printable width = 17.4 cm (A4 with 1.8 cm margins).
    if ncol == 5:
        return [1.35, 4.45, 1.30, 4.40, 5.90]
    if ncol == 4:
        return [1.55, 2.35, 7.15, 6.35]
    if ncol == 3:
        return [2.6, 6.1, 8.7]
    if ncol == 2:
        return [5.0, 12.4]
    return [17.4 / ncol] * ncol


def matrix_widths(ncol: int) -> list[float]:
    if ncol == 5:
        return [2.4, 5.3, 5.3, 5.8, 8.1]
    if ncol == 4:
        return [5.0, 4.4, 5.8, 11.7]
    if ncol == 3:
        return [5.0, 7.0, 14.9]
    return [26.9 / ncol] * ncol


def set_table_row_cant_split(row) -> None:
    from docx.oxml import OxmlElement
    from docx.oxml.ns import qn

    tr_pr = row._tr.get_or_add_trPr()
    cant_split = OxmlElement("w:cantSplit")
    cant_split.set(qn("w:val"), "true")
    tr_pr.append(cant_split)


def add_table(helper, doc: Document, rows: list[list[str]], widths: list[float], font_size: float) -> None:
    if not rows:
        return
    ncol = len(rows[0])
    table = doc.add_table(rows=1, cols=ncol)
    table.style = "Table Grid"
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    table.autofit = False

    header = table.rows[0]
    helper.set_header_repeat(header)
    set_table_row_cant_split(header)
    for j, value in enumerate(rows[0]):
        cell = header.cells[j]
        cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
        helper.set_cell_width(cell, widths[j])
        helper.set_cell_margins(cell, top=45, start=55, bottom=45, end=55)
        para = cell.paragraphs[0]
        para.paragraph_format.space_after = Pt(0)
        helper.add_inline(para, value, size=font_size)
        for run in para.runs:
            run.bold = True

    for values in rows[1:]:
        row = table.add_row()
        set_table_row_cant_split(row)
        for j, value in enumerate(values):
            cell = row.cells[j]
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.TOP
            helper.set_cell_width(cell, widths[j])
            helper.set_cell_margins(cell, top=38, start=50, bottom=38, end=50)
            para = cell.paragraphs[0]
            para.paragraph_format.space_after = Pt(0)
            para.paragraph_format.line_spacing = 1.0
            helper.add_inline(para, value, size=font_size)

    spacer = doc.add_paragraph()
    spacer.paragraph_format.space_after = Pt(0)


def configure_portrait(helper, doc: Document) -> None:
    sec = doc.sections[0]
    sec.page_width = Cm(21.0)
    sec.page_height = Cm(29.7)
    sec.top_margin = Cm(1.65)
    sec.bottom_margin = Cm(1.65)
    sec.left_margin = Cm(1.8)
    sec.right_margin = Cm(1.8)
    for style_name, size, bold in [
        ("Normal", 9.4, False),
        ("Heading 1", 15.5, True),
        ("Heading 2", 12.4, True),
        ("Heading 3", 10.8, True),
        ("Heading 4", 9.8, True),
        ("Footer", 8.0, False),
    ]:
        if style_name not in doc.styles:
            continue
        style = doc.styles[style_name]
        helper.set_font(style, size=size, bold=bold)
        if style_name == "Normal":
            style.paragraph_format.space_after = Pt(3)
            style.paragraph_format.line_spacing = 1.02
        elif style_name.startswith("Heading"):
            style.paragraph_format.space_before = Pt(6)
            style.paragraph_format.space_after = Pt(3)
            style.paragraph_format.keep_with_next = True
    helper.add_page_number(sec, size=8)


def add_code_block(helper, doc: Document, lines: list[str]) -> None:
    for line in lines:
        p = doc.add_paragraph()
        p.paragraph_format.left_indent = Cm(0.5)
        p.paragraph_format.space_after = Pt(0)
        run = p.add_run(line)
        run.font.name = "Consolas"
        run.font.size = Pt(8.5)


def build_portrait(helper, md_path: Path, out_path: Path) -> None:
    lines = md_path.read_text(encoding="utf-8").splitlines()
    doc = Document()
    configure_portrait(helper, doc)
    idx = 0
    in_code = False
    code_lines: list[str] = []

    while idx < len(lines):
        line = lines[idx]

        if line.startswith("```"):
            if in_code:
                add_code_block(helper, doc, code_lines)
                code_lines = []
                in_code = False
            else:
                in_code = True
            idx += 1
            continue
        if in_code:
            code_lines.append(line)
            idx += 1
            continue
        if not line.strip():
            idx += 1
            continue

        heading = re.match(r"^(#{1,4})\s+(.*)$", line)
        if heading:
            level = len(heading.group(1))
            p = doc.add_paragraph(style=f"Heading {level}")
            p.paragraph_format.keep_with_next = True
            helper.add_inline(p, heading.group(2))
            idx += 1
            continue

        if line.startswith("|"):
            block: list[str] = []
            while idx < len(lines) and lines[idx].startswith("|"):
                block.append(lines[idx])
                idx += 1
            rows = parse_table_block(block)
            add_table(helper, doc, rows, portrait_widths(len(rows[0])), 7.6 if len(rows[0]) >= 5 else 8.0)
            continue

        if line.startswith("> "):
            p = doc.add_paragraph()
            p.paragraph_format.left_indent = Cm(0.45)
            p.paragraph_format.right_indent = Cm(0.25)
            helper.add_inline(p, line[2:].strip(), size=8.9)
            idx += 1
            continue

        bullet = re.match(r"^[-*]\s+(.*)$", line)
        if bullet:
            p = doc.add_paragraph(style="List Bullet")
            helper.add_inline(p, bullet.group(1))
            idx += 1
            continue

        numbered = re.match(r"^\d+\.\s+(.*)$", line)
        if numbered:
            p = doc.add_paragraph(style="List Number")
            helper.add_inline(p, numbered.group(1))
            idx += 1
            continue

        p = doc.add_paragraph()
        helper.add_inline(p, line.rstrip())
        idx += 1

    if in_code and code_lines:
        add_code_block(helper, doc, code_lines)
    doc.save(out_path)


def build_matrix(helper, md_path: Path, out_path: Path) -> None:
    lines = md_path.read_text(encoding="utf-8").splitlines()
    doc = Document()
    sec = doc.sections[0]
    sec.orientation = WD_ORIENT.LANDSCAPE
    sec.page_width = Cm(29.7)
    sec.page_height = Cm(21.0)
    sec.top_margin = Cm(1.25)
    sec.bottom_margin = Cm(1.25)
    sec.left_margin = Cm(1.4)
    sec.right_margin = Cm(1.4)

    for style_name, size, bold in [
        ("Normal", 8.6, False),
        ("Heading 1", 15.0, True),
        ("Heading 2", 11.5, True),
        ("Heading 3", 10.2, True),
        ("Footer", 8.0, False),
    ]:
        style = doc.styles[style_name]
        helper.set_font(style, size=size, bold=bold)
        if style_name == "Normal":
            style.paragraph_format.space_after = Pt(2)
            style.paragraph_format.line_spacing = 1.0
        elif style_name.startswith("Heading"):
            style.paragraph_format.space_before = Pt(5)
            style.paragraph_format.space_after = Pt(2)
            style.paragraph_format.keep_with_next = True
    helper.add_page_number(sec, size=8)

    idx = 0
    while idx < len(lines):
        line = lines[idx]
        if not line.strip():
            idx += 1
            continue
        heading = re.match(r"^(#{1,3})\s+(.*)$", line)
        if heading:
            p = doc.add_paragraph(style=f"Heading {len(heading.group(1))}")
            p.paragraph_format.keep_with_next = True
            helper.add_inline(p, heading.group(2))
            idx += 1
            continue
        if line.startswith("|"):
            block: list[str] = []
            while idx < len(lines) and lines[idx].startswith("|"):
                block.append(lines[idx])
                idx += 1
            rows = parse_table_block(block)
            add_table(
                helper,
                doc,
                rows,
                matrix_widths(len(rows[0])),
                7.0 if len(rows[0]) >= 5 else 7.7,
            )
            continue
        bullet = re.match(r"^[-*]\s+(.*)$", line)
        if bullet:
            p = doc.add_paragraph(style="List Bullet")
            helper.add_inline(p, bullet.group(1), size=8.3)
            idx += 1
            continue
        p = doc.add_paragraph()
        helper.add_inline(p, line.rstrip())
        idx += 1

    doc.save(out_path)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    root = args.root.resolve()
    req = root / "Documentos" / "Requisitos"
    helper = load_helper(root)

    portrait_stems = [
        "00_Indice_Mestre_Requisitos_Jornada_v1.1",
        "02_Requisitos_Funcionais_Jornada_v1.1",
        "03_Requisitos_Nao_Funcionais_Jornada_v1.1",
    ]
    for stem in portrait_stems:
        md = req / f"{stem}.md"
        docx = req / f"{stem}.docx"
        build_portrait(helper, md, docx)
        convert_pdf(docx)

    matrix_md = req / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md"
    matrix_docx = matrix_md.with_suffix(".docx")
    build_matrix(helper, matrix_md, matrix_docx)
    convert_pdf(matrix_docx)

    required = {
        "00_Indice_Mestre_Requisitos_Jornada_v1.1.md": ["um único documento por número", "SolutionSchema v3.70"],
        "02_Requisitos_Funcionais_Jornada_v1.1.md": ["RF-001", "RF-050", "RF-051", "RF-056"],
        "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md": ["RNF-001", "RNF-033", "RNF34-A", "RNF34-D"],
        "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md": ["RF-001", "RF-051", "RNF-001", "RNF34-C"],
    }
    for name, markers in required.items():
        md = req / name
        text = md.read_text(encoding="utf-8")
        for marker in markers:
            if marker not in text:
                raise RuntimeError(f"{name}: missing marker {marker}")
        docx = md.with_suffix(".docx")
        pdf = md.with_suffix(".pdf")
        parsed = Document(docx)
        if not parsed.tables:
            raise RuntimeError(f"{name}: institutional document must contain real Word tables")
        if any(table.style.name != "Table Grid" for table in parsed.tables):
            raise RuntimeError(f"{name}: every institutional table must use Table Grid")
        if not pdf.is_file() or pdf.stat().st_size < 1000:
            raise RuntimeError(f"invalid PDF: {pdf}")

    print("REQUIREMENTS RENDER: OK (00/02/03/05 v1.1 DOCX/PDF; all tables are real Table Grid)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
