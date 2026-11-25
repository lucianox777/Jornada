#!/usr/bin/env python3
"""Phase 4 v6: plan only, never score, publish, fit or start a database."""
import argparse
import hashlib
import importlib.util
import itertools
import json
from pathlib import Path
import re
import uuid
import zipfile

BASE_SHA = "058f185f6ac6e3f3f9fc519b0c10a3274f11efda"
BASE_ARTIFACT = 15026250198
BASE_ZIP_SHA = "155de4c59181146e565f6d3c20c2ac2a17138863a830542b1ebd04854487b94c"
WINNER = "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee"
COUNTS = {"baseIdentities": 1000, "sourceVersions": 2349,
          "persistentOrigins": 1957, "cpfVersions": 315}
FACTORS = (("evidence", ("b1", "b2", "summary")),
           ("prior", ("records", "people")),
           ("policy", ("r1", "r2")),
           ("blocking", ("broad", "secure")))
BASELINE = ("b1", "records", "r1", "broad")
METRICS = (
    "pair_tp_fp_fn_recall_fdr", "false_merges_splits_by_true_person",
    "indefinite_by_raw_and_effective_reason", "b2_exclusive_correct_incorrect_by_score_band",
    "propagation_by_round", "secure_records_per_person", "candidate_only_via_nonsecure",
    "indefinite_vs_new_broad_secure", "fp_by_round_and_cumulative",
    "indefinite_later_resolved", "new_identity_later_duplicate",
    "fs_per_record_mean_p95_p99_max", "keys_per_person", "time_by_phase",
    "process_memory_peak_and_series", "tempdb_version_store_peak_and_series",
    "snapshot_duration", "index_size_bytes")
SCHEDULES = (("single", (2,)), ("per_wave", (1, 2)))


def require(ok):
    if not ok:
        raise ValueError("V9_PHASE4_PLAN_REJECTED")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"),
                      allow_nan=False).encode()


def load_baseline(path):
    require(path.is_file() and not path.is_symlink() and 0 < path.stat().st_size < 1024 * 1024)
    raw = path.read_bytes()
    require(sha(raw) == BASE_ZIP_SHA)
    import io
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        require(z.testzip() is None)
        require(z.read("commit.txt").decode().strip() == BASE_SHA)
        nucleus = json.loads(z.read("nucleus-receipt.json"))
        identity = json.loads(z.read("identity-receipt.json"))
        fn = json.loads(z.read("fn-receipt.json"))
        winner = json.loads(z.read("winner-pin-receipt.json"))
    require(nucleus["commit"] == BASE_SHA and nucleus["E2E_NUCLEO_PASS"] is True
            and nucleus["E2E_INTEGRAL_PASS"] is False
            and nucleus["scope"] == "DEV_1K_SINGLE_PHYSICAL_FULL_NUCLEUS")
    require(winner["winnerSha256"] == identity["winnerSha256"] == WINNER
            and winner["activated"] is False and winner["refitted"] is False
            and winner["scientificTest"] is False
            and nucleus["runId"] == identity["runId"])
    require(all(identity[k] == n for k, n in COUNTS.items()))
    metrics = identity["pairMetrics"]
    require((metrics["tp"], metrics["fp"], metrics["fn"], metrics["truePairs"]) ==
            (563, 0, 1842, 2405) and fn["fn"] == metrics["fn"] and fn["status"] == "PASS")
    return {"commit": BASE_SHA, "artifactId": BASE_ARTIFACT, "zipSha256": BASE_ZIP_SHA,
            "winnerSha256": WINNER, "counts": COUNTS.copy(), "pairMetrics": metrics,
            "unresolvedVersions": identity["unresolvedVersions"],
            "readOnlyRetryRecoveredFn": fn["singleFullContrast"]["recoveredFnPairs"]}


def bind_fixture(root):
    """Revalidate original private waves; no raw person/truth output."""
    spec = importlib.util.spec_from_file_location("wave", Path(__file__).with_name(
        "v9-e2e-1k-wave-preflight.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    pre = mod.verify(root)
    require(pre["status"] == "PASS" and pre["waveCount"] == 2
            and all(pre[k] == n for k, n in COUNTS.items()))
    # Preserve the actual package order used by the phase-3 HTTP loop.
    groups = pre["packagesByWave"]
    require([g["wave"] for g in groups] == [1, 2])
    require(all(len(g["packages"]) == 4 for g in groups))
    manifest = (root / "corpus" / "generation-manifest.json")
    source = mod.BRIDGE.read_json(manifest)
    truth_hashes = [sha(mod.read_private(
        root / "ingestion" / ("wave-%02d" % wave) / "bridge-truth.jsonl",
        mod.BRIDGE.MAX_TRUTH_BYTES)) for wave in (1, 2)]
    return {"inputFingerprint": source["input_fingerprint_sha256"],
            "orderedPackagesSha256": sha(canonical(groups)),
            "truthSha256ByWave": truth_hashes,
            "preflightSha256": sha(canonical(pre)),
            "counts": COUNTS.copy(), "waveCount": 2}


def build_plan(baseline, attempt, fixture=None):
    require(str(uuid.UUID(attempt)) == attempt and baseline["commit"] == BASE_SHA
            and baseline["winnerSha256"] == WINNER and baseline["counts"] == COUNTS)
    if fixture is not None:
        require(fixture["counts"] == COUNTS and fixture["waveCount"] == 2)
        require(all(re.fullmatch("[0-9a-fA-F]{64}", d) for d in (
            fixture["inputFingerprint"], fixture["orderedPackagesSha256"],
            fixture["preflightSha256"], *fixture["truthSha256ByWave"])))
        require(len(fixture["truthSha256ByWave"]) == 2)
    binding = sha(canonical(fixture)) if fixture is not None else None
    cells = []
    for values in itertools.product(*(v for _, v in FACTORS)):
        cell = "-".join(values)
        trajectories = []
        for schedule, waves in SCHEDULES:
            namespace = str(uuid.uuid5(uuid.UUID(attempt), cell + "/" + schedule))
            stages = []
            previous = None
            for number, through_wave in enumerate(waves, 1):
                state = str(uuid.uuid5(uuid.UUID(namespace), str(number)))
                stages.append({"round": number, "throughWave": through_wave,
                               "stateId": state, "previousStateId": previous,
                               "status": "NOT_RUN"})
                previous = state
            trajectories.append({"schedule": schedule, "trajectoryId": namespace,
                                 "rounds": stages, "fixtureBinding": binding,
                                 "publication": False})
        cells.append({"cell": cell, "factors": dict(zip((k for k, _ in FACTORS), values)),
                      "officialBaseline": values == BASELINE, "trajectories": trajectories,
                      "status": "NOT_RUN"})
    return {"schema": "V9_PHASE4_1K_MATRIX_PLAN_V1", "status": "PLANNED_ONLY",
            "attempt": attempt, "baseline": baseline, "fixture": fixture,
            "fixtureBinding": binding, "cells": cells, "requiredMetrics": METRICS,
            "firstContrast": {"id":"S4B","variants":["official_indefinite","one_new_per_group","one_new_per_record"],
                              "status":"NOT_RUN","beforeFactorial":True,"publication":False,
                              "officialPolicyUnchanged":True,"requireOriginContinuitySensitivity":True,
                              "posteriorMeasure":"MIN_INTERNAL_DIRECTIONAL_POSTERIOR",
                              "separateSameManagerPairs":True},
            "factorialRequiresS4bReceipt":True,
            "population": 1000, "seed": 355, "scientificTest": False,
            "publication": False, "activation": False, "executedCells": 0,
            "r2RulesPendingSpecification": True,
            "executionAuthorizedByThisPlan": False,
            "officialBaselineParityRequiredBeforeContrasts": True}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--baseline-zip", type=Path, required=True)
    p.add_argument("--attempt", required=True)
    p.add_argument("--fixture-root", type=Path)
    p.add_argument("--receipt", type=Path, required=True)
    args = p.parse_args()
    try:
        baseline = load_baseline(args.baseline_zip)
        fixture = bind_fixture(args.fixture_root) if args.fixture_root else None
        result = build_plan(baseline, args.attempt, fixture)
        with args.receipt.open("x", encoding="utf-8") as out:
            json.dump(result, out, sort_keys=True, indent=2)
    except (ValueError, OSError, KeyError, TypeError, AttributeError, zipfile.BadZipFile):
        raise SystemExit("V9_PHASE4_PLAN_REJECTED") from None
    print("V9_PHASE4_PLAN_ONLY_24_CELLS_ZERO_EXECUTED")


if __name__ == "__main__":
    main()
