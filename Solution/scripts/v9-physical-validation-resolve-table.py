#!/usr/bin/env python3
"""Fail-closed table from externally measured physical VALIDATION + final Resolve outcomes.

This tool does NOT invoke SQL/Resolve or certify evidence provenance. It refuses
pair-threshold metrics, incomplete grids, TEST use and impossible denominators.
"""
import argparse
import csv
import json
import math
from pathlib import Path

COLUMNS = ("profile threshold tf_weight eligible_true sql_true tp fp blocking_recall "
           "global_recall fdr_after_resolve hoeffding_upper_by_identity conflict "
           "unresolved sql_p95_ms").split()

def aggregate(data):
    if data.get("seed") != 42 or data.get("split") != "VALIDATION" or data.get("test_used") is not False:
        raise ValueError("requires seed 42, VALIDATION only, sealed TEST")
    if data.get("source_kind") != "PHYSICAL_SQL_RUNNER_RESOLVE":
        raise ValueError("physical SQL + final Resolve source required")
    runs = data.get("runs")
    if not isinstance(runs, list) or len(runs) != 12:
        raise ValueError("expected twelve measured configurations")
    output, seen, thresholds, weights = [], set(), set(), set()
    for r in runs:
        if r.get("final_rank_resolve_evaluated") is not True or r.get("independent_truth_verified") is not True:
            raise ValueError("pair-level or unverified outcome rejected")
        p, t, w = r.get("profile"), r.get("threshold"), r.get("tf_weight")
        if p not in ("clean", "correlated") or type(t) not in (float, int) or type(w) not in (float, int):
            raise ValueError("invalid grid")
        if not math.isfinite(t) or not math.isfinite(w) or not 0 < t < 1 or not 0 <= w <= 1:
            raise ValueError("invalid threshold/TF")
        key = (p, t, w)
        if key in seen: raise ValueError("duplicate configuration")
        seen.add(key); thresholds.add(t); weights.add(w)
        counts = [r.get(k) for k in ("eligible_true", "sql_true", "tp", "fp", "conflict", "unresolved")]
        if any(type(v) is not int or v < 0 for v in counts): raise ValueError("invalid counts")
        eligible, sql_true, tp, fp, conflict, unresolved = counts
        if eligible == 0 or sql_true > eligible or tp > sql_true:
            raise ValueError("inconsistent recall denominators")
        p95, upper = r.get("sql_p95_ms"), r.get("hoeffding_upper_by_identity")
        if any(type(x) not in (float, int) or not math.isfinite(x) for x in (p95, upper)):
            raise ValueError("missing physical P95 or grouped upper bound")
        if p95 < 0 or not 0 <= upper <= 1: raise ValueError("invalid latency/confidence")
        fdr = fp / (tp + fp) if tp + fp else None
        if fdr is not None and upper + 1e-12 < fdr:
            raise ValueError("upper bound below observed FDR")
        output.append(dict(profile=p, threshold=t, tf_weight=w, eligible_true=eligible,
            sql_true=sql_true, tp=tp, fp=fp, blocking_recall=sql_true / eligible,
            global_recall=tp / eligible, fdr_after_resolve=fdr,
            hoeffding_upper_by_identity=upper, conflict=conflict,
            unresolved=unresolved, sql_p95_ms=p95))
    if len(thresholds) != 3 or len(weights) != 2 or seen != {
        (p, t, w) for p in ("clean", "correlated") for t in thresholds for w in weights
    }: raise ValueError("incomplete 3x2 grid for each profile")
    return sorted(output, key=lambda r: (r["profile"], r["threshold"], r["tf_weight"]))

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--evidence", required=True, type=Path)
    p.add_argument("--out", required=True, type=Path)
    args = p.parse_args()
    if args.out.exists(): p.error("output exists; refusing overwrite")
    rows = aggregate(json.loads(args.evidence.read_text(encoding="utf-8")))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("x", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=COLUMNS)
        writer.writeheader()
        writer.writerows(rows)

if __name__ == "__main__": main()
