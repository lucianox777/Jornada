#!/usr/bin/env python3
"""Fail-closed inventory for immutable historical TF0 snapshots.

This is a preflight, not a rewrite: rewriting content-addressed .fitted.json
would change its SHA and would silently invalidate downstream pinned evidence.
Only a new immutable versioned artifact may be emitted by a future explicit
migration command, with old->new SHA mapping and verified score parity.
"""
import argparse
import hashlib
import json
from pathlib import Path


def inspect_archive(path: Path) -> dict:
    raw = path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    expected = path.name.removesuffix(".fitted.json")
    if path.suffix != ".json" or not path.name.endswith(".fitted.json") or expected != digest:
        raise ValueError(f"Historical snapshot name/content digest mismatch: {path}")
    doc = json.loads(raw)
    if doc.get("SchemaVersion") not in ("V9_FITTED_SNAPSHOT_V2", "V9_FITTED_SNAPSHOT_V3"):
        raise ValueError(f"Unexpected archived snapshot schema: {path}")
    if doc.get("TfWeight") != 0 or doc.get("TfCalibration") is not None or (
        doc.get("MotherTfCalibration") is not None or doc.get("UnitTfCalibrations") is not None
    ):
        raise ValueError(f"Legacy archive is not a TF0-only migration candidate: {path}")
    if len(doc.get("JointCells", [])) != 81:
        raise ValueError(f"Legacy snapshot missing its entire 81-cell model: {path}")
    return {
        "path": str(path),
        "original_sha256": digest,
        "original_schema": doc["SchemaVersion"],
        "option_id": doc.get("OptionId"),
        "legacy_tf0": True,
        "new_sha256": None,
        "rewritten": False,
        "operationally_activated": False,
        "migration_status": "PRECHECK_ONLY_LEGACY_IMMUTABLE_NO_NEW_SNAPSHOT",
    }


def archives(root: Path):
    return sorted(root.glob("*/" + "*.fitted.json"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive_root", type=Path)
    parser.add_argument("report", type=Path)
    args = parser.parse_args()
    if args.report.exists():
        raise FileExistsError(args.report)
    files = archives(args.archive_root)
    if not files:
        raise ValueError("No archived snapshots found")
    result = {"version": "V9_TF_ARCHIVE_PREFLIGHT_V1", "snapshots": [
        inspect_archive(p) for p in files],
        "migrated": False, "run_30k": False, "certified": False}
    with args.report.open("x", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
