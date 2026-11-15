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
    r"(?:\s+\|\s+group_sha=(?P<sha>[a-f0-9]{12}))?"
)
PHASE_GROUP = re.compile(
    r"\[V9 PERF GROUP\]\s+batch=(?P<batch>\d+)\s+\|\s+"
    r"group_sha=(?P<sha>[a-f0-9]{12})\s+\|\s+"
    r"requests=(?P<requests>\d+)\s+\|\s+"
    r"replay_calls=(?P<replays>\d+)\s+\|\s+"
    r"sql_seconds=(?P<sql>\d+(?:\.\d+)?)\s+\|\s+"
    r"post_sql_seconds=(?P<post>\d+(?:\.\d+)?)\s+\|\s+"
    r"total_seconds=(?P<total>\d+(?:\.\d+)?)"
)
TIME = re.compile(r"^(?P<timestamp>\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z)\s+")


def read_log(path):
    progress, groups, phases = [], [], []
    for lineno, line in enumerate(Path(path).open(encoding="utf-8"), start=1):
        event = PROGRESS.search(line)
        group = GROUP.search(line)
        phase = PHASE_GROUP.search(line)
        if not event and not group and not phase:
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
                "sha": group["sha"],
            })
        if phase:
            item = {
                "batch": int(phase["batch"]), "sha": phase["sha"],
                "requests": int(phase["requests"]),
                "replayCalls": int(phase["replays"]),
                "sqlSeconds": float(phase["sql"]),
                "postSqlSeconds": float(phase["post"]),
                "totalSeconds": float(phase["total"]),
            }
            if (item["replayCalls"] > item["requests"]
                    or abs(item["totalSeconds"] - item["sqlSeconds"]
                           - item["postSqlSeconds"]) > 0.21
                    or any(old["sha"] == item["sha"] for old in phases)):
                raise ValueError(f"V9_TIMING_PHASE_GROUP_INCONSISTENT line={lineno}")
            phases.append(item)
    if not progress:
        raise ValueError("V9_TIMING_NO_PROGRESS")
    for name, rows in (("sql", groups), ("phase", phases)):
        shas = [row["sha"] for row in rows if row["sha"]]
        if len(shas) != len(set(shas)):
            raise ValueError(f"V9_TIMING_DUPLICATE_{name.upper()}_GROUP_SHA")
    for phase in phases:
        matched = [sql for sql in groups if sql["sha"] == phase["sha"]]
        if len(matched) != 1 or matched[0]["batch"] != phase["batch"]
                or matched[0]["requests"] != phase["requests"]
                or abs(matched[0]["seconds"] - phase["sqlSeconds"]) > 0.11:
            raise ValueError("V9_TIMING_PHASE_SQL_GROUP_MISMATCH")
    return {"progress": progress, "groups": groups, "phaseGroups": phases}


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
    group_comparison = []
    base_groups, cand_groups = baseline["groups"], candidate["groups"]
    # Old logs lack a stable group identity. Only compare by SHA when BOTH
    # runs provide it; otherwise keep the old ordinal proxy, explicitly
    # marked unverified (groups may complete in different orders).
    by_sha = (bool(base_groups) and bool(cand_groups)
              and all(g["sha"] for g in base_groups + cand_groups))
    candidate_group_by_sha = {g["sha"]: g for g in cand_groups} if by_sha else {}
    for i, old in enumerate(base_groups[:len(cand_groups)] if not by_sha
                            else base_groups, start=1):
        new = (candidate_group_by_sha.get(old["sha"]) if by_sha
               else cand_groups[i - 1])
        if new is None:
            group_comparison.append({
                "ordinal": i, "groupSha": old["sha"], "matchedBy": "sha",
                "signatureCompatible": False, "requests": None,
                "baselineSeconds": old["seconds"],
                "candidateSeconds": None, "candidateOverBaseline": None,
            })
            continue
        signature = ("batch", "requests", "options", "margins")
        same_signature = all(old[k] == new[k] for k in signature)
        group_comparison.append({
            "ordinal": i, "groupSha": old["sha"] if by_sha else None,
            "matchedBy": "sha" if by_sha else "ordinal_unverified",
            "signatureCompatible": same_signature,
            "requests": old["requests"] if same_signature else None,
            "baselineSeconds": old["seconds"], "candidateSeconds": new["seconds"],
            "candidateOverBaseline": round(new["seconds"] / old["seconds"], 4)
            if same_signature and old["seconds"] > 0 else None,
        })
    phases_base = {p["sha"]: p for p in baseline.get("phaseGroups", [])}
    phases_candidate = {p["sha"]: p for p in candidate.get("phaseGroups", [])}
    paired_phases = []
    for group_sha in sorted(phases_base.keys() & phases_candidate.keys()):
        old, new = phases_base[group_sha], phases_candidate[group_sha]
        paired_phases.append({
            "groupSha": group_sha,
            "sameBatchAndRequests": (old["batch"] == new["batch"]
                                     and old["requests"] == new["requests"]),
            "baselinePostSqlSeconds": old["postSqlSeconds"],
            "candidatePostSqlSeconds": new["postSqlSeconds"],
            "postSqlRatio": round(new["postSqlSeconds"] / old["postSqlSeconds"], 4)
            if old["postSqlSeconds"] > 0 else None,
            "baselineReplayCalls": old["replayCalls"],
            "candidateReplayCalls": new["replayCalls"],
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
        "phaseGroupCountBaseline": len(phases_base),
        "phaseGroupCountCandidate": len(phases_candidate),
        "pairedPostSqlGroups": paired_phases,
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
