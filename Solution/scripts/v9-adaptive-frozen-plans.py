#!/usr/bin/env python3
"""Diagnostic adaptive plans from a verified immutable canonical TRAIN snapshot."""
import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("adaptive_planner", HERE / "v9-adaptive-frequency-planner.py")
planner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(planner)

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def execute(snapshot_dir, out, cap=30, max_passes=3):
    root = Path(snapshot_dir)
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    source = root / "canonical_observations.csv"
    if manifest.get("frozen") is not True or manifest.get("test_used_for_frequency") is not False:
        raise ValueError("invalid immutable TRAIN-only snapshot")
    if sha(source) != manifest.get("frozen_source_sha256"):
        raise ValueError("canonical source hash mismatch")
    for name, key in (("train_name_frequencies.json", "frequency_sha256"),
                      ("train_joint_name_frequencies.json", "joint_frequency_sha256")):
        if sha(root / name) != manifest.get(key):
            raise ValueError("TRAIN frequency snapshot hash mismatch")
    with source.open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        raise ValueError("empty canonical snapshot")
    ids = [r["observacao_id"] for r in rows]
    if any(not i for i in ids) or len(ids) != len(set(ids)):
        raise ValueError("missing or duplicate observation IDs")
    counts = planner.train_counts(rows)
    plans = [{"observacao_id": row["observacao_id"], "particao": row["particao"],
              "passes": planner.plan(row, counts, cap=cap, max_passes=max_passes)}
             for row in rows]
    report = {"diagnostic_only": True, "adaptive_strategy_executed": False,
              "source_sha256": sha(source), "train_only": True,
              "frequency_method": "distinct TRAIN identities from hash-verified canonical snapshot",
              "caution": "Plans only: no live SQL fanout, retrieval, Rank/Resolve or D-arm equivalence",
              "plans": plans}
    Path(out).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot-dir", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--cap", type=int, default=30)
    parser.add_argument("--max-passes", type=int, default=3)
    args = parser.parse_args()
    execute(args.snapshot_dir, args.out, args.cap, args.max_passes)
