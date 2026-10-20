#!/usr/bin/env python3
"""Summarize pinned TRAIN-only support studies and the already recorded finalist holdout.

Does not run scoring, fit, search, or read corpus/TEST attributes. Historical metrics
are paired only when split/corpus identities and logical configurations agree.
"""
import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path


def selection(directory):
    reference = json.loads((directory / "selection-reference.json").read_bytes())
    sha = reference["envelopeSha256"]
    raw = (directory / (sha + ".selection.json")).read_bytes()
    assert hashlib.sha256(raw).hexdigest() == sha
    doc = json.loads(raw)
    assert not doc["result"]["ExternalValidationUsed"] and not doc["result"]["TestUsed"]
    return sha, doc


def metric(candidate):
    m = candidate["Metric"]
    p = candidate["Snapshot"]["Payload"]
    support = p.get("JointSupport")
    return dict(support_version=support["Version"] if support else "LEGACY_JOINT_DIRICHLET",
        estimation_methods_m=dict(Counter(c["MethodM"] for c in support["Cells"])) if support else {"JOINT_DIRICHLET": 81},
        estimation_methods_u=dict(Counter(c["MethodU"] for c in support["Cells"])) if support else {"JOINT_DIRICHLET": 81},
        snapshot_sha256=candidate["Snapshot"]["ContentSha256"],
        passes=[b["PassId"] for b in p["BlockingPasses"]], alpha=candidate["Support"]["SmoothingAlpha"],
        threshold=p["Threshold"], margin=p["ConflictMargin"],
        eligible_sources=m["EligibleSourceObservationsBeforeBlocking"],
        tp=m["TrueAcceptedSourceObservations"], fp=m["FalseAcceptedSourceObservations"],
        recall=m["GlobalSourceRecall"], fdr=m["FdrAfterResolve"],
        conflicts=m["Conflicts"], unresolved=m["Unresolved"],
        fanout_false_p99=candidate["FalseDegreeP99"], p95_ms=candidate["P95Milliseconds"],
        rejections=candidate["Rejections"])


def report(legacy, current, validation, destination):
    if destination.exists():
        raise FileExistsError(destination)
    old_sha, old = selection(legacy)
    new_sha, new = selection(current)
    before, after = old["result"], new["result"]
    assert old["originalObservationsSha256"] == new["originalObservationsSha256"]
    assert before["Split"] == after["Split"]
    indexed = {e["RequestSha256"]: e["Candidate"] for e in before["Evaluations"] if e["Candidate"]}
    paired = []
    for e in after["Evaluations"]:
        prior = indexed.get(e["RequestSha256"])
        candidate = e["Candidate"]
        if prior is None or candidate is None:
            continue
        a, b = metric(prior), metric(candidate)
        assert (a["passes"], a["alpha"], a["threshold"], a["margin"], a["eligible_sources"]) == (
            b["passes"], b["alpha"], b["threshold"], b["margin"], b["eligible_sources"])
        assert prior["Snapshot"]["Payload"]["TrainCorpusSha256"] == candidate["Snapshot"]["Payload"]["TrainCorpusSha256"]
        assert prior["Support"]["Cells"] == candidate["Support"]["Cells"]
        paired.append(dict(request_sha256=e["RequestSha256"], joint_only=a, marginal_backoff=b,
            recall_difference=b["recall"] - a["recall"], fp_difference=b["fp"] - a["fp"]))
    candidates = [e["Candidate"] for e in after["Evaluations"] if e["Candidate"]]
    finalist_ids = after["FinalistSnapshotShas"]
    front_ids = after["FrontierSnapshotShas"]
    result = dict(scope="TRAIN_SELECTION_COMPARISON_NOT_CERTIFICATION",
        purpose=json.loads((current / "plan.json").read_bytes())["Purpose"],
        train_identity_count=len(after["Split"]["Assignments"]),
        identity_split_counts=dict(Counter(a["Purpose"] for a in after["Split"]["Assignments"])),
        source_csv_sha256=new["originalObservationsSha256"],
        legacy_selection_sha256=old_sha, selection_sha256=new_sha,
        assignment_sha256=after["Split"]["AssignmentSha256"], evaluations=len(after["Evaluations"]),
        initial_same_frame_comparisons=paired, rejection_counts=dict(Counter(r for e in after["Evaluations"] for r in e["Rejections"])),
        frontier=[metric(c) for c in candidates if c["Snapshot"]["ContentSha256"] in front_ids],
        finalists=[metric(c) for c in candidates if c["Snapshot"]["ContentSha256"] in finalist_ids],
        stop_reason=after["StopReason"], progress=after["Progress"],
        external_validation_multiplicity=len(finalist_ids), frozen_test_multiplicity=1,
        historical_known_comparisons=14159502, historical_multiplicity_applies_only_to_consumed_validation=True,
        certified=False, test_used=False, activated=False)
    if validation:
        raw = validation.read_bytes()
        measured = json.loads(raw)
        assert measured["selectionEnvelopeSha256"] == new_sha
        assert measured["comparedConfigurations"] == len(finalist_ids)
        assert not measured["testUsed"] and not measured["refitted"] and not measured["activated"]
        assert measured["selectedApprovedSnapshotSha256"] is None and not measured["certified"]
        assert {m["SnapshotSha256"] for m in measured["measured"]} == set(finalist_ids)
        result["external_validation"] = dict(report_sha256=hashlib.sha256(raw).hexdigest(),
            producer_seed=measured["fresh"]["Seed"], status=measured["status"],
            conditional_winner=measured["bestConditionalSnapshotSha256"],
            measurements=[dict(snapshot_sha256=m["SnapshotSha256"], p95_ms=m["P95Milliseconds"],
                false_degree_p99=m["FalseDegreeP99"],
                eligible_sources=m["Metric"]["EligibleSourceObservationsBeforeBlocking"],
                identities=m["Metric"]["PreBlockingBaseIdentities"], tp=m["Metric"]["TrueAcceptedSourceObservations"],
                fp=m["Metric"]["FalseAcceptedSourceObservations"], recall=m["Metric"]["GlobalSourceRecall"],
                fdr=m["Metric"]["FdrAfterResolve"], conditional_fdr_upper=m["Metric"]["ConditionalClusterCpFdrUpper"],
                bonferroni=m["Metric"]["BonferroniComparedConfigurations"], latency_repetitions=len(m["SamplesMilliseconds"]))
                for m in measured["measured"]])
    with destination.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps(dict(evaluations=result["evaluations"], paired=len(paired), finalists=len(finalist_ids), certified=False)))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("legacy_search", type=Path)
    parser.add_argument("current_search", type=Path)
    parser.add_argument("new_report", type=Path)
    parser.add_argument("--validation", type=Path)
    args = parser.parse_args()
    report(args.legacy_search, args.current_search, args.validation, args.new_report)
