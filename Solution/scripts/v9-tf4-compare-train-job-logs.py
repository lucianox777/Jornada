#!/usr/bin/env python3
"""Compare V9 324 physical TRAIN GitHub job logs without running calibration.

This tool consumes already-existing logs locally; it NEVER dispatches a run,
accesses SQL or TEST, changes science, or treats a timing difference as proof
of a particular cause. Output contains aggregate timings and SHA prefixes
only (never observation rows or secrets).
"""
import argparse
from datetime import datetime
import json
from pathlib import Path
import re

PROGRESS = re.compile(
    r"\[V9 PROGRESS\]\s+(?P<number>\d+)/(?P<total>\d+)"
    r".*?\bhipotese=(?P<sha>[a-f0-9]{12})\s+"
    r"resultado=(?P<result>AVALIADA|REJEITADA)"
)
GROUP = re.compile(
    r"\[V9 SQL GROUP\]\s+batch=(?P<batch>\d+)\s+\|\s+"
    r"requests=(?P<requests>\d+)\s+\|\s+options=(?P<options>\d+)"
    r"\s+\|\s+margins=(?P<margins>\d+)\s+\|\s+"
    r"run_async_seconds=(?P<seconds>\d+(?:\.\d+)?)"
)
TIME = re.compile(r"^(?P<timestamp>\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z)\s+")


def read_log(path):
    progress, groups = [], []
    for lineno, line in enumerate(Path(path).open(encoding="utf-8"), start=1):
        event = PROGRESS.search(line)
        group = GROUP.search(line)
        if not event and not group:
            continue
        ts = TIME.match(line)
        if ts is None:
            raise ValueError(f"V9_TIMING_LOG_TIMESTAMP_MISSING line={lineno}")
        moment = datetime.fromisoformat(ts.group("timestamp").replace("Z", "+00:00"))
        if event:
            item = {
                "number": int(event["number"]), "total": int(event["total"]),
                "sha": event["sha"], "result": event["result"], "at": moment,
            }
            if item["total"] != 324 or item["number"] != len(progress) + 1:
                raise ValueError(f"V9_TIMING_PROGRESS_SEQUENCE_INVALID line={lineno}")
            if progress and moment < progress[-1]["at"]:
                raise ValueError(f"V9_TIMING_PROGRESS_CLOCK_REGRESSION line={lineno}")
            if any(row["sha"] == item["sha"] for row in progress):
                raise ValueError(f"V9_TIMING_PROGRESS_DUPLICATE_SHA line={lineno}")
            progress.append(item)
        if group:
            groups.append({
                "batch": int(group["batch"]), "requests": int(group["requests"]),
                "options": int(group["options"]), "margins": int(group["margins"]),
                "seconds": float(group["seconds"]), "at": moment,
            })
    if not progress:
        raise ValueError("V9_TIMING_NO_PROGRESS")
    return {"progress": progress, "groups": groups}


def seconds_between(first, second):
    return round((second - first).total_seconds(), 3)


def compare(baseline, candidate):
    a, b = baseline["progress"], candidate["progress"]
    if a[0]["total"] != b[0]["total"] or a[0]["total"] != 324:
        raise ValueError("V9_TIMING_TOTAL_MISMATCH")
    base_by_sha = {x["sha"]: x for x in a}
    candidate_by_sha = {x["sha"]: x for x in b}
    common = set(base_by_sha) & set(candidate_by_sha)
    mismatch = sorted(sha for sha in common
                      if base_by_sha[sha]["result"] != candidate_by_sha[sha]["result"])
    milestone_numbers = [64, 128, 200, 256, 278, 300, 308, 324]
    milestones = []
    for number in milestone_numbers:
        if number > min(len(a), len(b)):
            continue
        aa, bb = a[number - 1], b[number - 1]
        # A time per completion rank is a rate comparison, NOT a matching
        # individual group; concurrent hypotheses may finish in a new order.
        base_seconds = seconds_between(a[0]["at"], aa["at"])
        candidate_seconds = seconds_between(b[0]["at"], bb["at"])
        milestones.append({
            "completed": number,
            "baselineSecondsSinceFirstCompletion": base_seconds,
            "candidateSecondsSinceFirstCompletion": candidate_seconds,
            "candidateOverBaseline": round(candidate_seconds / base_seconds, 4)
            if base_seconds > 0 else None,
        })
    for name, rows in (("baseline", a), ("candidate", b)):
        if len(rows) > 324:
            raise ValueError(f"V9_TIMING_{name.upper()}_OUT_OF_BOUNDS")
    # Repeated group signatures are distinguished ONLY by appearance order.
    # They are not proven to share frame ID; report as a diagnostic proxy.
    group_comparison = []
    base_groups, cand_groups = baseline["groups"], candidate["groups"]
    for i, (old, new) in enumerate(zip(base_groups, cand_groups), start=1):
        signature = ("batch", "requests", "options", "margins")
        same_signature = all(old[k] == new[k] for k in signature)
        group_comparison.append({
            "ordinal": i, "signatureCompatible": same_signature,
            "requests": old["requests"] if same_signature else None,
            "baselineSeconds": old["seconds"], "candidateSeconds": new["seconds"],
            "candidateOverBaseline": round(new["seconds"] / old["seconds"], 4)
            if same_signature and old["seconds"] > 0 else None,
        })
    return {
        "schema": "V9_TF4_TRAIN_LOG_COMPARISON_V1",
        "baselineCompleted": len(a), "candidateCompleted": len(b),
        "totalDeclared": 324,
        "sharedHypothesisShas": len(common),
        "candidateUnknownHypothesisShas": len(set(candidate_by_sha) - set(base_by_sha)),
        "commonStatusMismatches": len(mismatch),
        "firstCompletionShaSame": a[0]["sha"] == b[0]["sha"],
        "sameOrderedShaPrefix": all(a[i]["sha"] == b[i]["sha"] for i in
                                    range(min(len(a), len(b)))),
        "milestones": milestones,
        "sqlGroups": group_comparison,
        "sqlGroupCountBaseline": len(base_groups),
        "sqlGroupCountCandidate": len(cand_groups),
        "causalityCertified": False, "productionParityCertified": False,
        "scientificTestUsed": False,
        "note": ("Performance association only; different hosted runners and "
                 "overlapping async tasks do not establish code causation. "
                 "SQL group timings are not the total end-to-end runtime."),
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline-log", required=True, type=Path)
    parser.add_argument("--candidate-log", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args(argv)
    if args.output.exists():
        parser.error("V9_TIMING_OUTPUT_MUST_BE_NEW")
    report = compare(read_log(args.baseline_log), read_log(args.candidate_log))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as f:
        json.dump(report, f, indent=2, sort_keys=True)
        f.write("\n")
    print("V9_TF4_TRAIN_LOGS_COMPARED_NO_CAUSALITY_CLAIM")


if __name__ == "__main__":
    main()
