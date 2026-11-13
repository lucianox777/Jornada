#!/usr/bin/env python3
"""Fail-closed, secret-free V9 TF4/324 finalist transfer between GitHub jobs.

Do not transfer TRAIN/VALIDATION corpora, SQL state, secrets or a TEST dataset.
Only a successfully audited plan, selection envelope, and pinned finalist fits
may cross the runner boundary. Large envelopes are hashed in bounded chunks.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil

SCHEMA = "V9_TF4_324_VALIDATION_HANDOFF_V1"
AUDIT_SCHEMA = "V9_V3_TF4_100K_TECHNICAL_AUDIT_V1"
AUDIT_STATUS = "TRAIN_TECHNICAL_PASS_NOT_SCIENTIFIC_CERTIFICATION"
SHA64 = re.compile(r"^[a-f0-9]{64}$")
SHA40 = re.compile(r"^[a-f0-9]{40}$")
METADATA = {
    "plan.json": "bound/plan.json",
    "binding-receipt.json": "bound/binding-receipt.json",
    "selection-reference.json": "results/selection-reference.json",
    "technical-audit.json": "bound/technical-audit.json",
}
MANIFEST = "validation-handoff-receipt.json"


def require(condition, reason):
    if not condition:
        raise ValueError("V9_TF4_HANDOFF_FAIL_CLOSED: " + reason)


def sha256_file(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as file:
        for chunk in iter(lambda: file.read(4 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def regular_file(path):
    file = Path(path)
    require(file.is_file() and not file.is_symlink(), "missing or linked file: " + file.name)
    return file


def small_json(path):
    file = regular_file(path)
    require(file.stat().st_size <= 32 * 1024 * 1024, "metadata too large: " + file.name)
    return json.loads(file.read_text(encoding="utf-8"))


def verify_contents(directory, code_sha, run_id, require_manifest=True):
    require(SHA40.fullmatch(code_sha) is not None and type(run_id) is int and run_id > 0,
            "invalid immutable run identity")
    directory = Path(directory)
    require(directory.is_dir() and not directory.is_symlink(), "missing handoff directory")
    receipt = small_json(directory / "binding-receipt.json")
    audit = small_json(directory / "technical-audit.json")
    selection = small_json(directory / "selection-reference.json")
    ids = selection.get("FinalistSnapshotShas")
    envelope = selection.get("envelopeSha256")
    require(isinstance(ids, list) and 1 <= len(ids) <= 3
            and len(set(ids)) == len(ids)
            and all(isinstance(x, str) and SHA64.fullmatch(x) for x in ids),
            "finalist snapshot identities invalid or empty")
    require(isinstance(envelope, str) and SHA64.fullmatch(envelope),
            "selection envelope identity invalid")
    plan_sha = sha256_file(regular_file(directory / "plan.json"))
    require(receipt.get("planSha256") == plan_sha
            and receipt.get("physicalHypotheses") == 324
            and receipt.get("tfOptions") == 81
            and receipt.get("testUsed") is False
            and receipt.get("certified") is False
            and receipt.get("activated") is False, "plan receipt invalid")
    require(audit.get("schema") == AUDIT_SCHEMA
            and audit.get("status") == AUDIT_STATUS
            and audit.get("physicalMeasured") == 324
            and audit.get("planSha256") == plan_sha
            and audit.get("codeSha") == code_sha
            and audit.get("workflowRunId") == run_id
            and audit.get("finalistSnapshotShas") == ids
            and audit.get("testUsed") is False
            and audit.get("certified") is False
            and audit.get("activated") is False, "physical audit identity or status invalid")
    selection_file = envelope + ".selection.json"
    pinned_files = [selection_file] + [x + ".fitted.json" for x in ids]
    expected = set(METADATA) | set(pinned_files)
    if require_manifest:
        expected.add(MANIFEST)
    actual = {p.name for p in directory.iterdir()}
    require(actual == expected, "unexpected or missing handoff members")
    records = {}
    for name in sorted(expected - {MANIFEST}):
        file = regular_file(directory / name)
        digest = sha256_file(file)
        require(not name.endswith(".selection.json") or digest == envelope,
                "selection envelope SHA mismatch")
        require(not name.endswith(".fitted.json") or digest == name.split(".", 1)[0],
                "frozen finalist SHA mismatch")
        records[name] = {"sha256": digest, "bytes": file.stat().st_size}
    if require_manifest:
        manifest = small_json(directory / MANIFEST)
        require(manifest == {
            "schema": SCHEMA, "codeSha": code_sha, "workflowRunId": run_id,
            "selectionEnvelopeSha256": envelope, "finalistSnapshotShas": ids,
            "files": records,
        }, "handoff manifest drift")
    return {
        "schema": SCHEMA, "codeSha": code_sha, "workflowRunId": run_id,
        "selectionEnvelopeSha256": envelope, "finalistSnapshotShas": ids,
        "files": records,
    }


def prepare(source_root, handoff, code_sha, run_id):
    source_root, handoff = Path(source_root), Path(handoff)
    require(handoff.is_dir() and not handoff.is_symlink(),
            "frozen finalist staging directory is missing")
    selection = small_json(source_root / METADATA["selection-reference.json"])
    ids, envelope = selection.get("FinalistSnapshotShas"), selection.get("envelopeSha256")
    require(isinstance(ids, list) and 1 <= len(ids) <= 3
            and all(isinstance(x, str) and SHA64.fullmatch(x) for x in ids)
            and len(set(ids)) == len(ids)
            and isinstance(envelope, str) and SHA64.fullmatch(envelope),
            "no pinned finalists or invalid envelope")
    files = {envelope + ".selection.json"} | {x + ".fitted.json" for x in ids}
    require({p.name for p in handoff.iterdir()} == files,
            "frozen directory contains unexpected files")
    for name, relative in METADATA.items():
        src = regular_file(source_root / relative)
        require(not (handoff / name).exists(), "metadata overwrite denied")
        shutil.copyfile(src, handoff / name)
    manifest = verify_contents(handoff, code_sha, run_id, require_manifest=False)
    (handoff / MANIFEST).open("x", encoding="utf-8").write(
        json.dumps(manifest, sort_keys=True, indent=2) + "\n")
    verify_contents(handoff, code_sha, run_id)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["prepare", "verify"])
    parser.add_argument("--handoff", type=Path, required=True)
    parser.add_argument("--source-root", type=Path)
    parser.add_argument("--code-sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    args = parser.parse_args()
    if args.operation == "prepare":
        require(args.source_root is not None, "source root required for prepare")
        report = prepare(args.source_root, args.handoff, args.code_sha, args.run_id)
    else:
        report = verify_contents(args.handoff, args.code_sha, args.run_id)
    print(json.dumps({"schema": report["schema"], "status": "PINNED_AND_AUDITED",
                      "finalists": len(report["finalistSnapshotShas"]),
                      "runId": args.run_id, "testUsed": False, "activated": False}))


if __name__ == "__main__":
    main()
