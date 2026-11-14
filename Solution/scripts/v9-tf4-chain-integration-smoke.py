#!/usr/bin/env python3
"""Synthetic *cross-job* TRAIN -> AUDIT -> VALIDATION GitHub artifact smoke.

This NEVER runs calibration, produces scientific passes, touches SQL, opens
real holdouts, or accesses production/TEST secrets. It tests the exact pinned
preaudit transfer and validates that each isolated job receives the right
artifact while the optional TEST preparation is skipped.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re

SCRIPT_DIR = Path(__file__).resolve().parent
PREAUDIT_PATH = SCRIPT_DIR / "v9-v3-four-name-100k-preaudit.py"
spec = importlib.util.spec_from_file_location("v9_tf4_preaudit_smoke", PREAUDIT_PATH)
assert spec is not None and spec.loader is not None
preaudit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preaudit)
SHA40 = re.compile(r"^[0-9a-f]{40}$")
AUDIT_FILENAME = "synthetic-audit-bridge-receipt.json"
AUDIT_SCHEMA = "V9_SYNTHETIC_AUDIT_BRIDGE_ONLY_V1"
AUDIT_STATUS = "SIMULATED_CHAIN_ONLY_NOT_SCIENTIFIC_AUDIT"


def require(condition, reason):
    if not condition:
        raise ValueError("V9_CHAIN_INTEGRATION_SMOKE_FAIL_CLOSED: " + reason)


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n",
                    encoding="utf-8")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require_identity(code_sha, run_id):
    require(SHA40.fullmatch(code_sha or "") is not None,
            "invalid source SHA")
    require(type(run_id) is int and run_id > 0, "invalid workflow run id")


def train(source, handoff, code_sha, run_id):
    require_identity(code_sha, run_id)
    require(not source.exists() and not handoff.exists(), "output already exists")
    (source / "bound").mkdir(parents=True)
    (source / "results").mkdir()
    plan = b'{"syntheticOnly":true,"calibrationNotRun":true}'
    (source / "bound/plan.json").write_bytes(plan)
    write_json(source / "bound/binding-receipt.json", {
        "planSha256": sha(plan), "physicalHypotheses": 324,
        "tfOptions": 81, "testUsed": False, "certified": False,
        "activated": False, "syntheticOnly": True,
    })
    write_json(source / "results/mu-statistical-analysis.json", {
        "syntheticOnly": True, "calibrationNotRun": True,
    })
    write_json(source / "results/runtime-diagnostics.json", {
        "syntheticOnly": True, "calibrationNotRun": True,
    })
    frozen = b'{"syntheticOnly":true,"fittedNeverComputed":true}'
    fitted_sha = sha(frozen)
    (source / "results" / (fitted_sha + ".fitted.json")).write_bytes(frozen)
    envelope = b'{"syntheticOnly":true,"scientificSelectionNeverRun":true}'
    envelope_sha = sha(envelope)
    (source / "results" / (envelope_sha + ".selection.json")).write_bytes(envelope)
    write_json(source / "results/selection-reference.json", {
        "envelopeSha256": envelope_sha,
        "FinalistSnapshotShas": [fitted_sha],
        "testUsed": False, "activated": False,
        "externalValidationUsed": False, "syntheticOnly": True,
    })
    # Explicit PII-like exclusion sentinel. It must NEVER appear in the artifact.
    raw = source / "people-100k/observacoes.csv"
    raw.parent.mkdir()
    raw.write_text("NEVER_TRANSFER_THIS_SYNTHETIC_SENTINEL", encoding="utf-8")
    report = preaudit.prepare(source, handoff, code_sha, run_id)
    require(report["status"] == "UNAUDITED_TRAIN_EVIDENCE_ONLY"
            and report["finalistSnapshotShas"] == [fitted_sha],
            "pinned synthetic handoff unexpectedly invalid")
    require("observacoes.csv" not in {p.name for p in handoff.iterdir()},
            "raw corpus leaked")
    print("V9_CHAIN_SYNTHETIC_TRAIN_HANDOFF_PASS")


def audit(handoff, output, code_sha, run_id):
    require_identity(code_sha, run_id)
    require(not output.exists(), "audit output already exists")
    report = preaudit.verify(handoff, code_sha, run_id)
    require(report["status"] == "UNAUDITED_TRAIN_EVIDENCE_ONLY",
            "non-TRAIN or already certified evidence")
    require(len(report["finalistSnapshotShas"]) == 1,
            "smoke fixture must contain exactly one *synthetic* finalist")
    require(json.loads((handoff / "selection-reference.json").read_text(
        encoding="utf-8")).get("syntheticOnly") is True,
        "refuse real scientific input in smoke")
    output.mkdir(parents=True)
    # Only a bridge receipt, expressly not a scientific PASS or source result.
    write_json(output / AUDIT_FILENAME, {
        "schema": AUDIT_SCHEMA, "status": AUDIT_STATUS,
        "codeSha": code_sha, "workflowRunId": run_id,
        "preauditManifestSha256": preaudit.sha256_file(handoff / preaudit.MANIFEST)
            if hasattr(preaudit, "sha256_file")
            else preaudit.digest(handoff / preaudit.MANIFEST),
        "syntheticOnly": True, "scientificAuditExecuted": False,
        "independentValidationExecuted": False, "testUsed": False,
        "finalistSnapshotShas": report["finalistSnapshotShas"],
    })
    print("V9_CHAIN_SYNTHETIC_AUDIT_BRIDGE_PASS")


def validation(handoff, audit_dir, code_sha, run_id):
    require_identity(code_sha, run_id)
    report = preaudit.verify(handoff, code_sha, run_id)
    require({p.name for p in audit_dir.iterdir()} == {AUDIT_FILENAME},
            "unexpected/missing AUDIT stage members")
    receipt = json.loads((audit_dir / AUDIT_FILENAME).read_text(encoding="utf-8"))
    require(receipt == {
        "schema": AUDIT_SCHEMA, "status": AUDIT_STATUS,
        "codeSha": code_sha, "workflowRunId": run_id,
        "preauditManifestSha256": preaudit.digest(handoff / preaudit.MANIFEST),
        "syntheticOnly": True, "scientificAuditExecuted": False,
        "independentValidationExecuted": False, "testUsed": False,
        "finalistSnapshotShas": report["finalistSnapshotShas"],
    }, "AUDIT artifact identity or stage status drift")
    require(len(report["finalistSnapshotShas"]) == 1,
            "synthetic finalist not transferred")
    require("observacoes.csv" not in {p.name for p in handoff.iterdir()},
            "raw TRAIN corpus leaked into VALIDATION")
    print("V9_CHAIN_SYNTHETIC_VALIDATION_RECEIPT_PASS")
    print("V9_CHAIN_INTEGRATION_ONLY_NO_100K_NO_SQL_NO_TEST")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("phase", choices=("train", "audit", "validation"))
    p.add_argument("--source", type=Path)
    p.add_argument("--handoff", type=Path, required=True)
    p.add_argument("--audit", type=Path)
    p.add_argument("--code-sha", required=True)
    p.add_argument("--run-id", type=int, required=True)
    a = p.parse_args()
    if a.phase == "train":
        require(a.source is not None, "source required")
        train(a.source, a.handoff, a.code_sha, a.run_id)
    elif a.phase == "audit":
        require(a.audit is not None, "audit output required")
        audit(a.handoff, a.audit, a.code_sha, a.run_id)
    else:
        require(a.audit is not None, "audit input required")
        validation(a.handoff, a.audit, a.code_sha, a.run_id)


if __name__ == "__main__":
    main()
