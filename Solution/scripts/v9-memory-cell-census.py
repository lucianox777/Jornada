#!/usr/bin/env python3
"""Diagnostic-only V9 in-memory blocking census. TRAIN/VALIDATION only.

Input JSONL fields: id, truth_id, source, partition, person, mother, dob.
Rows must be synthetic, CPF-free and normalized upstream. Output is aggregate.
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path

MISSING = {"", "NULL", "NAO INFORMADO", "IGNORADO", "DESCONHECIDO"}
PASSES = (
    ("P_EXACT_DOB", ("person", "dob"), True),
    ("P_EXACT_M_EXACT", ("person", "mother"), True),
    ("P_PREFIX3_DOB", ("person3", "dob"), True),
    ("P_PREFIX3_M_PREFIX3", ("person3", "mother3"), True),
    ("M_PREFIX3_DOB_DIAGNOSTIC", ("mother3", "dob"), False),
)


def valid_name(value):
    return (isinstance(value, str) and value.strip().upper() not in MISSING
            and any(c.isalpha() for c in value) and not any(c.isdigit() for c in value))


def prepare(path):
    raw = Path(path).read_bytes()
    rows = []
    seen = set()
    partitions = {}
    for line in raw.splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        if row["partition"] not in ("TRAIN", "VALIDATION"):
            raise ValueError("TEST and unknown partitions are prohibited")
        if row["id"] in seen:
            raise ValueError("duplicate observation id")
        seen.add(row["id"])
        if row["truth_id"] in partitions and partitions[row["truth_id"]] != row["partition"]:
            raise ValueError("identity leakage between partitions")
        partitions[row["truth_id"]] = row["partition"]
        if row.get("cpf"):
            raise ValueError("CPF records must follow the separate deterministic flow")
        for field in ("person", "mother"):
            if not valid_name(row.get(field)):
                row[field] = None
        dob = row.get("dob")
        if not isinstance(dob, str) or len(dob) != 10 or dob in ("1900-01-01",):
            row["dob"] = None
        row["person3"] = row["person"][:3] if row["person"] and len(row["person"]) >= 3 else None
        row["mother3"] = row["mother"][:3] if row["mother"] and len(row["mother"]) >= 3 else None
        row["eligible"] = bool(row["person"] and (row["mother"] or row["dob"]))
        rows.append(row)
    return rows, hashlib.sha256(raw).hexdigest()


def candidate_pairs(rows, fields, limit):
    groups = defaultdict(list)
    for i, row in enumerate(rows):
        key = tuple(row.get(field) for field in fields)
        if row["eligible"] and all(key):
            groups[key].append(i)
    pairs = set()
    for group in groups.values():
        for n, i in enumerate(group):
            for j in group[n + 1:]:
                if rows[i]["source"] != rows[j]["source"]:
                    pairs.add((i, j))
                    if len(pairs) > limit:
                        raise ValueError("fanout cap exceeded; no truncation")
    return pairs


def census(rows, pairs, truth, eligible_truth):
    m = len(pairs & eligible_truth)
    u = len(pairs) - m
    degree = [0] * len(rows)
    for i, j in pairs:
        degree[i] += 1
        degree[j] += 1
    values = sorted(degree[i] for i, row in enumerate(rows) if row["eligible"])
    def pct(q):
        import math
        return values[max(0, math.ceil(q * len(values)) - 1)] if values else None
    return dict(M_total=len(truth), M_eligible=len(eligible_truth),
                M_recovered=m, U_candidates=u,
                recall_total=m / len(truth) if truth else None,
                recall_eligible=m / len(eligible_truth) if eligible_truth else None,
                FDR_hypothetical=u / (m + u) if m + u else None,
                U_per_M=u / m if m else None,
                fanout_p95=pct(.95), fanout_p99=pct(.99),
                zero_candidate_observations=values.count(0))


def run(path, cap=1000000):
    rows, digest = prepare(path)
    results = {}
    for partition in ("TRAIN", "VALIDATION"):
        part = [r for r in rows if r["partition"] == partition]
        truth = {(i, j) for i, a in enumerate(part)
                 for j in range(i + 1, len(part))
                 if a["truth_id"] == part[j]["truth_id"] and a["source"] != part[j]["source"]}
        eligible = {(i, j) for i, j in truth if part[i]["eligible"] and part[j]["eligible"]}
        union = set()
        cells = []
        for name, fields, included in PASSES:
            pairs = candidate_pairs(part, fields, cap)
            added = pairs - union if included else set()
            cells.append(dict(name=name, included_in_union=included,
                              new_M=len(added & eligible), new_U=len(added - eligible),
                              **census(part, pairs, truth, eligible)))
            if included:
                union |= pairs
                if len(union) > cap:
                    raise ValueError("union fanout cap exceeded")
        results[partition] = dict(observations=len(part),
                                  eligible_observations=sum(r["eligible"] for r in part),
                                  abstentions=sum(not r["eligible"] for r in part),
                                  cells=cells, union=census(part, union, truth, eligible))
    return dict(version="V9_MEMORY_CELLS_DIAGNOSTIC_V1",
                status="TRAIN_VALIDATION_ONLY_NOT_CALIBRATED",
                may_promote=False, cpf_deterministic_separate=True,
                ibge_marginals_not_multiplied=True,
                corpus_sha256=digest, partitions=results)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--cap", type=int, default=1000000)
    args = parser.parse_args()
    Path(args.output).write_text(json.dumps(run(args.input, args.cap), indent=2) + "\n")
