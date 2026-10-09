#!/usr/bin/env python3
"""Verify the committed demographic bootstrap snapshot without recalculating it."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "data" / "reference" / "synthetic-birth-sp"

def main() -> None:
    manifest = json.loads((BASE / "manifest.json").read_text(encoding="utf-8"))
    output = manifest["output"]
    name = output["path"]
    if Path(name).name != name:
        raise ValueError("Snapshot path must be a filename in the reference directory")
    raw = (BASE / name).read_bytes()
    actual = hashlib.sha256(raw).hexdigest().upper()
    if actual != output["sha256"].upper():
        raise ValueError(f"Snapshot SHA-256 mismatch: {actual}")
    document = json.loads(raw)
    if document.get("schema_version") != output["schemaVersion"]:
        raise ValueError("Snapshot schema mismatch")
    rows = document.get("rows")
    if not isinstance(rows, list) or len(rows) != output["rowCount"]:
        raise ValueError("Snapshot row count mismatch")
    if not rows or any(not isinstance(row, dict) or
                       not isinstance(row.get("date"), str) or
                       type(row.get("births")) is not int or row["births"] <= 0
                       for row in rows):
        raise ValueError("Snapshot rows invalid")
    if len({row["date"] for row in rows}) != len(rows):
        raise ValueError("Duplicate birth dates")
    print(f"FROZEN BIRTH REFERENCE OK: {manifest['referenceCode']} "
          f"rows={len(rows)} sha256={actual} total_weight={sum(r['births'] for r in rows)}")
    print("No projection recalculated; no database modified.")

if __name__ == "__main__":
    main()
