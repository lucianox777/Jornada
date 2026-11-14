#!/usr/bin/env python3
"""Strict, byte-pinned TRAIN -> AUDIT transfer. Never transfer raw corpora or SQL.

The independent audit job verifies this manifest *before* auditing. This
preaudit artifact is expressly NOT a certified or audited finalist handoff.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil

SCHEMA = "V9_TF4_324_PREAUDIT_TRANSFER_V1"
MANIFEST = "calibration-preaudit-receipt.json"
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")
METADATA = {
    "plan.json": "bound/plan.json",
    "binding-receipt.json": "bound/binding-receipt.json",
    "mu-statistical-analysis.json": "results/mu-statistical-analysis.json",
    "runtime-diagnostics.json": "results/runtime-diagnostics.json",
    "selection-reference.json": "results/selection-reference.json",
}


def require(ok, message):
    if not ok:
        raise ValueError("V9_TF4_PREAUDIT_FAIL_CLOSED: " + message)


def regular_file(path):
    file = Path(path)
    require(file.is_file() and not file.is_symlink(),
            "missing, symlinked or non-file member: " + file.name)
    return file


def digest(path):
    hashed = hashlib.sha256()
    with regular_file(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            hashed.update(chunk)
    return hashed.hexdigest()


def small_json(path):
    file = regular_file(path)
    require(file.stat().st_size <= 32 * 1024 * 1024,
            "metadata too large: " + file.name)
    return json.loads(file.read_text(encoding="utf-8"))


def members(selection):
    ids = selection.get("FinalistSnapshotShas")
    envelope = selection.get("envelopeSha256")
    require(isinstance(ids, list) and 0 <= len(ids) <= 3
            and len(set(ids)) == len(ids)
            and all(isinstance(item, str) and SHA64.fullmatch(item) for item in ids),
            "invalid finalist identities")
    require(isinstance(envelope, str) and SHA64.fullmatch(envelope),
            "invalid envelope identity")
    require(selection.get("testUsed") is False
            and selection.get("activated") is False
            and selection.get("externalValidationUsed") is False,
            "unapproved test or validation exposure")
    return ids, envelope, [envelope + ".selection.json"] + [
        item + ".fitted.json" for item in ids]


def verify(directory, code_sha, run_id, check_manifest=True):
    require(SHA40.fullmatch(code_sha or "") is not None
            and type(run_id) is int and run_id > 0, "invalid immutable run")
    root = Path(directory)
    require(root.is_dir() and not root.is_symlink(),
            "missing or linked preaudit directory")
    receipt = small_json(root / "binding-receipt.json")
    selection = small_json(root / "selection-reference.json")
    ids, envelope, models = members(selection)
    require(receipt.get("planSha256") == digest(root / "plan.json")
            and receipt.get("physicalHypotheses") == 324
            and receipt.get("tfOptions") == 81
            and receipt.get("testUsed") is False
            and receipt.get("certified") is False
            and receipt.get("activated") is False,
            "plan identity, study cardinality or TRAIN-only flags drift")
    expected = set(METADATA) | set(models)
    if check_manifest:
        expected.add(MANIFEST)
    require({p.name for p in root.iterdir()} == expected,
            "unexpected or absent transfer member")
    records = {}
    for name in sorted(expected - {MANIFEST}):
        file = regular_file(root / name)
        sha = digest(file)
        require(not name.endswith(".selection.json") or sha == envelope,
                "selection envelope content hash drift")
        require(not name.endswith(".fitted.json") or sha == name.split(".", 1)[0],
                "fitted finalist content hash drift")
        records[name] = {"sha256": sha, "bytes": file.stat().st_size}
    result = {
        "schema": SCHEMA, "status": "UNAUDITED_TRAIN_EVIDENCE_ONLY",
        "codeSha": code_sha, "workflowRunId": run_id,
        "selectionEnvelopeSha256": envelope, "finalistSnapshotShas": ids,
        "files": records, "testUsed": False, "activated": False,
    }
    if check_manifest:
        require(small_json(root / MANIFEST) == result,
                "immutable preaudit manifest mismatch")
    return result


def prepare(source_root, target, code_sha, run_id):
    source_root, target = Path(source_root), Path(target)
    require(not target.exists(), "preaudit directory already exists")
    selection = small_json(source_root / METADATA["selection-reference.json"])
    _, _, models = members(selection)
    target.mkdir(parents=True)
    for name, relative in METADATA.items():
        shutil.copyfile(regular_file(source_root / relative), target / name)
    for name in models:
        shutil.copyfile(regular_file(source_root / "results" / name), target / name)
    report = verify(target, code_sha, run_id, check_manifest=False)
    (target / MANIFEST).write_text(
        json.dumps(report, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    verify(target, code_sha, run_id)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "verify"))
    parser.add_argument("--source-root", type=Path)
    parser.add_argument("--handoff", required=True, type=Path)
    parser.add_argument("--code-sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    args = parser.parse_args()
    if args.operation == "prepare":
        require(args.source_root is not None, "source root required")
        report = prepare(args.source_root, args.handoff,
                         args.code_sha, args.run_id)
    else:
        report = verify(args.handoff, args.code_sha, args.run_id)
    print(json.dumps({
        "schema": report["schema"], "status": report["status"],
        "finalistCandidates": len(report["finalistSnapshotShas"]),
        "runId": args.run_id, "testUsed": False, "activated": False,
    }))


if __name__ == "__main__":
    main()
