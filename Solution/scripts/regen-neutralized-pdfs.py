#!/usr/bin/env python3
"""Controlled PDF regeneration from same-stem DOCX, never PDF string substitution.

Outputs only aggregate file paths/verification counts. Does not edit historical
SQL or arbitrary PDFs and does not claim visual parity on font/layout drift.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

from pypdf import PdfReader

LEGACY = "PRO" + "DAM"


def pages(path: Path) -> int:
    r = PdfReader(str(path), strict=True)
    if r.is_encrypted:
        raise ValueError("encrypted PDF")
    return len(r.pages)


def mentions(path: Path) -> int:
    r = PdfReader(str(path), strict=True)
    if r.is_encrypted:
        raise ValueError("encrypted PDF")
    text = "\n".join(page.extract_text() or "" for page in r.pages).lower()
    return text.count(LEGACY.lower())


def regenerate(root: Path, before: dict, out: Path) -> dict:
    if before.get("total_files") != 45:
        raise ValueError("Expected exactly 45 tracked office files; re-audit the changed inventory")
    targets = [row for row in before["files"] if row["type"] == ".pdf" and row.get("before", 0) > 0]
    exceptions = []
    regenerated = []
    for row in targets:
        rel = row["path"]
        current = root / rel
        source = current.with_suffix(".docx")
        if not source.is_file():
            exceptions.append({"path": rel, "reason": "SOURCE_DOCX_NOT_PRESENT"})
            continue
        if shutil.which("libreoffice") is None:
            exceptions.append({"path": rel, "reason": "RENDERER_LIBREOFFICE_NOT_INSTALLED"})
            continue
        try:
            original_pages = pages(current)
            with tempfile.TemporaryDirectory(prefix="jornada-pdf-rebuild-") as d:
                folder = Path(d)
                result = subprocess.run(
                    ["libreoffice", "-env:UserInstallation=file://" + str(folder / "profile"),
                     "--headless", "--convert-to", "pdf:writer_pdf_Export",
                     "--outdir", str(folder), str(source)],
                    capture_output=True, text=True, timeout=180, check=True,
                )
                rendered = folder / current.name
                if not rendered.is_file() or rendered.stat().st_size < 1000:
                    raise ValueError("renderer produced no nonempty PDF")
                new_pages = pages(rendered)
                if new_pages != original_pages:
                    exceptions.append({"path": rel,
                                       "reason": "PAGE_COUNT_CHANGED_NEEDS_VISUAL_REVIEW",
                                       "oldPages": original_pages, "newPages": new_pages})
                    continue
                remaining = mentions(rendered)
                if remaining:
                    exceptions.append({"path": rel,
                                       "reason": "LEGACY_TOKEN_REMAINS_IN_REGENERATED_PDF",
                                       "remaining": remaining})
                    continue
                digest = hashlib.sha256(rendered.read_bytes()).hexdigest()
                shutil.copyfile(rendered, current)
                regenerated.append({"path": rel, "source": str(source.relative_to(root)),
                                    "pages": new_pages, "sha256": digest})
        except Exception as exc:
            exceptions.append({"path": rel, "reason": type(exc).__name__ + ": " + str(exc)[:180]})
    result = {"schema": "NEUT_DEV_PDF_REGEN_WITH_EXCEPTIONS_V1",
              "original_pdf_with_legacy": len(targets),
              "regenerated_pdf": len(regenerated),
              "regenerated": regenerated,
              "exceptions_count": len(exceptions), "exceptions": exceptions,
              "policy": "PDF only from same-stem DOCX; page-count mismatch is not silently accepted; no PDF text overlay"}
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print(json.dumps({"originalPdfWithLegacy": len(targets), "regeneratedPdf": len(regenerated),
                      "exceptions": len(exceptions)}))
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--before", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    regenerate(args.root.resolve(), json.loads(args.before.read_text(encoding="utf8")),
               args.report)


if __name__ == "__main__":
    main()
