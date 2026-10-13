#!/usr/bin/env python3
"""Aggregate real C# V9 FS synthetic proxy outcomes from disjoint generated seeds.

This validates provenance, arithmetic and honest pass/fail recording; it NEVER
certifies real SQL VERSIONED_ALIAS blocking, TF calibration or real-world FDR.
"""
from __future__ import annotations

import argparse
from decimal import Decimal, InvalidOperation
import hashlib
import json
from pathlib import Path

VERSION = "SYNTHETIC_V9_FROZEN_SOURCE42_CROSS_SEED_TRANSFER_V1"
FROZEN_VERSION = "V9_SYNTHETIC_FROZEN_JOINT_FS_FROM_SEED42_V1"
FIT = "FIT_TRAIN_AND_VALIDATION_FOR_SOURCE_SEED42"
TRANSFERRED = "TRANSFER_SOURCE_SEED42_UNCHANGED_NO_REFIT_OR_RETHRESHOLD"
SCORER_VERSION = "V9_30000_FS_CSHARP_FUNCTIONAL_PROBE_V1"
SCOPE = "FIRST_INITIAL_PROXY_NOT_REAL_SQL_BLOCKING"
UNKNOWN = "NOT_PROVEN_ON_REAL_SQL_BLOCKING_OR_MUNICIPAL_POPULATION"
PASS = "PASS_ON_SAMPLED_SYNTHETIC_PROXY"
FAIL = "FAIL_ON_SAMPLED_SYNTHETIC_PROXY"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def dec(value: object, label: str) -> Decimal:
    try:
        result = Decimal(str(value))
    except (InvalidOperation, ValueError) as exc:
        raise ValueError(f"Non-decimal value: {label}") from exc
    require(result.is_finite(), f"Non-finite value: {label}")
    return result


def ratio(numerator: int, denominator: int) -> Decimal:
    require(denominator > 0 and numerator >= 0, "Invalid count denominator")
    return Decimal(numerator) / Decimal(denominator)


def verify_metrics(row: object, partition: str) -> dict:
    require(isinstance(row, dict) and row.get("Partition") == partition,
            f"Missing or crossed split: {partition}")
    keys = ("Tp", "Fp", "Fn", "Tn", "MatchedNotRetrieved")
    counts = {key: row[key] for key in keys}
    for key, value in counts.items():
        require(type(value) is int and value >= 0, f"Invalid {partition}.{key}")
    tp, fp, fn, tn = (counts[key] for key in keys[:4])
    require(counts["MatchedNotRetrieved"] <= fn, "Blocked matched pairs not included in FN")
    require(tp + fn > 0 and fp + tn > 0, "Split missing M or U")
    actual = {
        "Recall": ratio(tp, tp + fn),
        "Fpr": ratio(fp, fp + tn),
        "Precision": ratio(tp, tp + fp) if tp + fp else None,
        "Fdr": ratio(fp, tp + fp) if tp + fp else None
    }
    for key, expected in actual.items():
        raw = row.get(key)
        require((expected is None and raw is None) or
                (expected is not None and raw is not None and
                 abs(dec(raw, partition + "." + key) - expected) <= Decimal("0.000000001")),
                f"Metric differs from confusion matrix: {partition}.{key}")
    return dict(counts, **{key: str(value) if value is not None else None
                           for key, value in actual.items()})


def audit_directory(directory: Path, expected_seed: int) -> dict:
    manifest = json.loads((directory / "generation-manifest.json").read_text(encoding="utf-8"))
    report = json.loads((directory / "v9-functional-probe.json").read_text(encoding="utf-8"))
    require(manifest.get("population_profile") == "DEMOGRAPHIC_PRIMARY_V1",
            "Only canonical demographic corpus is allowed")
    require(manifest.get("seed") == expected_seed, "Manifest seed diverges")
    require(report.get("Version") == SCORER_VERSION and report.get("Scope") == SCOPE,
            "Unrecognized V9 C# scorer version / candidate frame")
    require(report.get("Seed") == expected_seed and report.get("People") == 30000,
            "Seed or fixed corpus population does not match")
    require(type(report.get("Observations")) is int and report["Observations"] > 30000,
            "Incomplete synthetic observation set")
    require(report.get("InputFingerprint", "").upper() ==
            manifest.get("input_fingerprint_sha256", "").upper(),
            "Input fingerprint mismatch")
    require(report.get("OperationalConclusion") == UNKNOWN,
            "Unsafe assertion of SQL or operational validity")
    require(report.get("Scorer") == "REAL_CSHARP_FS_PREVIEW_SINGLE_JOINT_NOMINAL_AND_BIRTH",
            "A different scorer was used")
    require(report.get("TermFrequencyStatus") ==
            "TF_WEIGHT_ZERO_IN_THIS_EXPERIMENT_NOT_FULL_TF_CALIBRATION",
            "TF experimental semantics were changed")
    require(report.get("TrainMatchedRetrieved", 0) > 0 and
            report.get("TrainUnmatchedSampled", 0) > 0,
            "Missing TRAIN class support")
    cells = report.get("TrainCellsWithoutBilateralSupport")
    require(type(cells) is int and 0 <= cells <= 81, "Invalid 81-cell support")
    require(dec(report.get("ValidationFprBudget"), "budget") == Decimal("0.01"),
            "Predeclared exploratory budget altered")
    require(dec(report.get("TestExploratoryRecallMinimum"), "recall_floor") ==
            Decimal("0.80"), "Predeclared exploratory recall altered")
    require(dec(report.get("FrozenThresholdLogOdds"), "threshold").is_finite(),
            "Invalid frozen validation threshold")
    validation = verify_metrics(report.get("Validation"), "VALIDATION")
    test = verify_metrics(report.get("Test"), "TEST")
    calibration_mode = report.get("CalibrationMode")
    require(calibration_mode == (FIT if expected_seed == 42 else TRANSFERRED),
            "Target seed refitted or thresholded itself instead of transferring seed42 model")
    model = report.get("FrozenModel")
    require(isinstance(model, dict) and model.get("Version") == FROZEN_VERSION
            and model.get("SourceSeed") == 42,
            "Missing or invalid frozen source seed42 model")
    require(type(model.get("SourceInputFingerprint")) is str and
            model["SourceInputFingerprint"],
            "Frozen source corpus fingerprint absent")
    require(isinstance(model.get("Cells"), list) and len(model["Cells"]) == 81,
            "Frozen model must contain 81 joint nominal cells")
    coordinates = {(cell.get("Person"), cell.get("Mother")) for cell in model["Cells"]}
    require(len(coordinates) == 81 and
            coordinates == {(a, b) for a in range(9) for b in range(9)},
            "Frozen joint cell dimensions are incomplete or duplicated")
    for axis in ("M", "U"):
        prob = sum((dec(c.get(axis), axis) for c in model["Cells"]), Decimal(0))
        require(abs(prob - Decimal(1)) <= Decimal("0.0000001"),
                "Frozen nominal probability distribution is not normalized")
        require(all(dec(c.get(axis), axis) > 0 for c in model["Cells"]),
                "Frozen nominal probabilities must be positive")
    require(isinstance(model.get("Parameters"), dict)
            and len(model["Parameters"]) >= 8, "Frozen FS parameters absent")
    model_sha = report.get("FrozenModelSha256")
    require(isinstance(model_sha, str) and len(model_sha) == 64 and
            all(c in "0123456789ABCDEF" for c in model_sha),
            "Missing source model SHA-256")
    require(dec(model.get("ThresholdLogOdds"), "source threshold") ==
            dec(report.get("FrozenThresholdLogOdds"), "report threshold"),
            "Threshold does not match source frozen model")
    if expected_seed == 42:
        source_file = directory / "v9-frozen-model.json"
        source_bytes = source_file.read_bytes().rstrip(b"\n")
        require(hashlib.sha256(source_bytes).hexdigest().upper() == model_sha,
                "Frozen source model SHA-256 mismatch")
        require(json.loads(source_bytes) == model,
                "Embedded model changed from the pinned source snapshot")
        require(model["SourceInputFingerprint"].upper() ==
                report["InputFingerprint"].upper(),
                "Seed42 source model does not match training corpus")
        require(dec(validation["Fpr"], "validation_fpr") <= Decimal("0.01"),
                "Source VALIDATION threshold exceeded predeclared budget")
    else:
        require(model["SourceInputFingerprint"].upper() !=
                report["InputFingerprint"].upper(),
                "Target seed reused source training population")
    passed = (test["Tp"] > 0 and dec(test["Recall"], "test_recall") >= Decimal("0.80")
              and dec(test["Fpr"], "test_fpr") <= Decimal("0.01"))
    require(report.get("SyntheticProxyVerdict") == (PASS if passed else FAIL),
            "Stored V9 pass/fail disagrees with observed TEST confusion matrix")
    files = manifest.get("outputs")
    require(isinstance(files, list) and len(files) >= 3, "Missing corpus artifact digests")
    paths = {x["path"]: x["sha256"].upper() for x in files}
    for name in ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json"):
        require(name in paths, f"Missing generated corpus digest for {name}")
        # Verify the SOURCE files, not merely its attestations.
        file = directory / name
        require(hashlib.sha256(file.read_bytes()).hexdigest().upper() == paths[name],
                f"Digest mismatch: {name}")
    return {
        "seed": expected_seed,
        "inputFingerprintSha256": report["InputFingerprint"].upper(),
        "corpusTruthSha256": paths["gabarito.json"],
        "observations": report["Observations"],
        "trainMatchedRetrieved": report["TrainMatchedRetrieved"],
        "trainUnmatchedSampled": report["TrainUnmatchedSampled"],
        "trainCellsWithoutBilateralSupport": cells,
        "validation": validation,
        "test": test,
        "proxyVerdict": report["SyntheticProxyVerdict"],
        "calibrationMode": calibration_mode,
        "frozenModelSha256": model_sha,
        "frozenModelSourceSeed": model["SourceSeed"],
        "frozenModelSourceFingerprint": model["SourceInputFingerprint"].upper(),
        "frozenThreshold": str(dec(model["ThresholdLogOdds"], "threshold")),
        "operationalConclusion": UNKNOWN,
    }


def aggregate(rows: list[dict]) -> dict:
    seeds = [row["seed"] for row in rows]
    require(len(seeds) >= 3 and len(seeds) == len(set(seeds)), "Require three distinct seeds")
    require(len({row["inputFingerprintSha256"] for row in rows}) == len(rows),
            "Different seeds cannot share input fingerprints")
    require(len({row["corpusTruthSha256"] for row in rows}) == len(rows),
            "Different seeds cannot have identical generated truth")
    by_seed = {row["seed"]: row for row in rows}
    require(sorted(by_seed) == [42, 43, 44], "Frozen cross-seed transfer requires seeds42-44")
    require(by_seed[42]["calibrationMode"] == FIT and
            all(by_seed[s]["calibrationMode"] == TRANSFERRED for s in (43, 44)),
            "Target model must be transferred, not refitted")
    require(len({row["frozenModelSha256"] for row in rows}) == 1,
            "Frozen model digest differs across target seeds")
    require(len({row["frozenThreshold"] for row in rows}) == 1,
            "Target threshold was silently retuned")
    require(all(row["frozenModelSourceSeed"] == 42 and
                row["frozenModelSourceFingerprint"] ==
                    by_seed[42]["inputFingerprintSha256"] for row in rows),
            "Frozen model source fingerprint not identical to seed42 truth")
    test_recalls = [dec(x["test"]["Recall"], "recall") for x in rows]
    test_fprs = [dec(x["test"]["Fpr"], "fpr") for x in rows]
    passed = sum(row["proxyVerdict"] == PASS for row in rows)
    return {
        "version": VERSION,
        "scope": SCOPE,
        "populationPerSeed": 30000,
        "seeds": seeds,
        "replicationCount": len(rows),
        "passedProxySeeds": passed,
        "failedProxySeeds": len(rows) - passed,
        "crossSeedProxyVerdict": "PASS_ALL_FROZEN_MODEL_TRANSFER_REPLICATES"
                                    if passed == len(rows)
                                    else "AT_LEAST_ONE_FROZEN_MODEL_TRANSFER_FAILED",
        "frozenModelSha256": by_seed[42]["frozenModelSha256"],
        "frozenModelSourceSeed": 42,
        "frozenModelSourceFingerprint": by_seed[42]["inputFingerprintSha256"],
        "testRecallMinimum": str(min(test_recalls)),
        "testRecallMaximum": str(max(test_recalls)),
        "testFprMinimum": str(min(test_fprs)),
        "testFprMaximum": str(max(test_fprs)),
        "operationalConclusion": UNKNOWN,
        "limitations": [
            "NOT_SQL_VERSIONED_ALIAS_BLOCKING",
            "UNMATCHED_ADJACENT_PROXY_NOT_PROBABILITY_SAMPLE",
            "NO_CALIBRATED_TF",
            "NO_CLUSTER_INDEPENDENT_FDR_INTERVAL",
            "SOURCE42_PRIOR_AND_THRESHOLD_FIXED_FOR_TARGET43_44",
            "SOURCE42_TRAIN_81_CELL_SPARSITY_REMAINS",
            "NOT_APPROVED_BY_DT15",
        ],
        "runs": rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seed-dir", action="append", nargs=2, metavar=("SEED", "DIRECTORY"),
                        required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    paths = [(int(seed), Path(directory)) for seed, directory in args.seed_dir]
    require(sorted(seed for seed, _ in paths) == [42, 43, 44],
            "Only the preregistered canonical 42,43,44 replication is supported")
    rows = [audit_directory(path, seed) for seed, path in paths]
    summary = aggregate(rows)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n",
                           encoding="utf-8")
    print("V9 30000 x 3 FS C# SYNTHETIC PROXY:", json.dumps({
        "seeds": summary["seeds"], "passes": summary["passedProxySeeds"],
        "verdict": summary["crossSeedProxyVerdict"],
        "min_recall": summary["testRecallMinimum"],
        "max_fpr": summary["testFprMaximum"],
        "operational": summary["operationalConclusion"]
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
