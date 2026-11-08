#!/usr/bin/env python3
"""Apply exact F/#1332 binary artifact to the SAME F branch only.

Default dry-run. Never commits, pushes, modifies SQL, or edits exceptional PDFs.
User must visually review documents and independently decide whether to commit.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import pathlib
import subprocess
import tempfile
import zipfile

BRANCH = "tools/neutralizacao-binarios-auditoria-ci-20261107"
EXPECTED_ARTIFACT_SHA256 = "376bae3e06b34802e736f1b3c7d47e2e7e3b6ecc1b98a1822564b301c8d3f988"
EXPECTED_BEFORE = 120
EXPECTED_AFTER = 49
EXPECTED_FILES = 14
EXPECTED_PDFS = 4


def run(*argv: str) -> str:
    return subprocess.check_output(argv, text=True).strip()


def main() -> None:
    cli = argparse.ArgumentParser()
    cli.add_argument("--zip", type=pathlib.Path, required=True,
                     help="Exact binary-edits.zip exported from CI artifact 13627958839")
    cli.add_argument("--apply", action="store_true", help="Explicitly write 14 files to current F branch")
    args = cli.parse_args()
    root = pathlib.Path(__file__).resolve().parents[2]
    branch = run("git", "-C", str(root), "branch", "--show-current")
    if branch != BRANCH:
        raise SystemExit("Refusing: checkout PR #1332 branch " + BRANCH + ", found " + branch)
    if run("git", "-C", str(root), "status", "--porcelain", "--", "Documentos"):
        raise SystemExit("Refusing: Documentos contains pre-existing changes")
    payload = args.zip.read_bytes()
    digest = hashlib.sha256(payload).hexdigest()
    if digest != EXPECTED_ARTIFACT_SHA256:
        raise SystemExit("Artifact SHA256 mismatch: refused, do not substitute another archive")
    with zipfile.ZipFile(args.zip) as z:
        names = z.namelist()
        if len(names) != len(set(names)) or len(names) != EXPECTED_FILES + 1:
            raise SystemExit("Unexpected entries, duplicate names or changed binary inventory")
        if "binary-patch-report.json" not in names:
            raise SystemExit("Missing provenance report")
        report = json.loads(z.read("binary-patch-report.json"))
        files = report.get("changedFiles")
        if (report.get("schema") != "NEUT_DEV_BINARY_SOURCE_REGEN_V1"
            or report.get("filesAudited") != 45 or report.get("before") != EXPECTED_BEFORE
            or report.get("after") != EXPECTED_AFTER or report.get("changedDocx") != 10
            or report.get("regeneratedPdf") != EXPECTED_PDFS
            or len(report.get("pdfExceptions", [])) != 6
            or report.get("notAudited") != []
            or not isinstance(files, list) or len(files) != EXPECTED_FILES
            or set(names) != set(files) | {"binary-patch-report.json"}):
            raise SystemExit("Artifact provenance counts or path list differ; refusing")
        validated = []
        for path in files:
            path_obj = pathlib.PurePosixPath(path)
            if (not path.startswith("Documentos/") or path_obj.is_absolute()
                or ".." in path_obj.parts or "\\" in path
                or path_obj.suffix.lower() not in {".docx", ".pdf"}):
                raise SystemExit("Unsafe or unauthorized path in ZIP: " + path)
            target = root / path_obj
            if not target.is_file() or target.is_symlink():
                raise SystemExit("Expected regular tracked file is missing or symlink: " + path)
            if not run("git", "-C", str(root), "ls-files", "--error-unmatch", "--", path):
                raise SystemExit("File not tracked: " + path)
            member = z.getinfo(path)
            if (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise SystemExit("Symlink in ZIP: " + path)
            binary = z.read(path)
            if len(binary) < 1000:
                raise SystemExit("Unexpected truncated document: " + path)
            if path.endswith(".docx") and not zipfile.is_zipfile(io.BytesIO(binary)):
                raise SystemExit("Invalid embedded DOCX ZIP: " + path)
            if path.endswith(".pdf") and not binary.startswith(b"%PDF-"):
                raise SystemExit("Invalid PDF header: " + path)
            validated.append((target, binary))
        print("Validated exact artifact:", digest)
        print("Expected edits:", len(validated), "10 DOCX + 4 PDF; remaining PDF exceptions:", 6)
        print("Status:", "APPLY" if args.apply else "DRY_RUN (no write)")
        if not args.apply:
            return
        for target, binary in validated:
            # Atomic file replacement within the same directory, without
            # changing Git history, permissions or any excluded PDF.
            fd, name = tempfile.mkstemp(prefix=".jornada-f-", dir=str(target.parent))
            try:
                with os.fdopen(fd, "wb") as fp:
                    fp.write(binary)
                    fp.flush()
                    os.fsync(fp.fileno())
                os.chmod(name, target.stat().st_mode & 0o777)
                os.replace(name, target)
            finally:
                if os.path.exists(name):
                    os.unlink(name)
        print("Applied in working tree only. Review DOCX/PDF layouts, rerun audit,")
        print("then explicitly stage/commit/push in existing PR #1332 branch.")
        print("Never stage the six exceptional PDFs, and never alter sealed SQL.")


if __name__ == "__main__":
    main()
