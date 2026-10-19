#!/usr/bin/env python3
"""Predeclared TRAIN/VALIDATION-only grid on the historical 30k corpus. Never TEST/certification."""
import argparse
import hashlib
import json
from pathlib import Path


def prepare(corpus: Path, plan_path: Path) -> None:
    manifest = json.loads((corpus.parent / "generation-manifest.json").read_bytes())
    assert manifest["seed"] == 42
    assert manifest["population_profile"] == "DEMOGRAPHIC_PRIMARY_V1"
    assert manifest["error_profile"] == "clean"
    input_sha = hashlib.sha256(corpus.read_bytes()).hexdigest()
    study = hashlib.sha256(("V9_ADAPTIVE_30K_TRAIN_ONLY_PROPOSALS_GRID_V1|" + input_sha).encode()).hexdigest()
    plan = {
        "StudyFingerprintSha256": study,
        "Discovery": {"MaximumPrefixLength": 4, "NonMatchSampleSize": 100000,
                      "NonMatchSampleSeed": 20261019,
                      "SearchOptions": {"MaxFieldsPerPass": 2, "MaxPasses": 2,
                                        "PrimitivePoolSize": 16, "MinimumTrueMatchRecall": 0.95,
                                        "RequireObservedNonMatchSupport": True,
                                        "CandidateRuleSetCap": 100000}},
        "ModelOptions": [{"OptionId": f"T{threshold}_TF0", "SmoothingAlpha": 0.5,
                          "DecisionThreshold": threshold, "TfWeight": 0}
                         for threshold in [0.90, 0.95, 0.99]],
        "ConflictMargins": [0.5, 2, 4],
        "Budget": {"Version": "ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1", "StudyFingerprintSha256": study,
                   "MinimumGlobalAcceptedRecall": 0.95, "MaximumObservedAcceptedFdrPoint": 0.01,
                   "MaximumBlockingFalseDegreeP99": 100, "MinimumValidationIdentityGroups": 1000},
        "SelectionPolicy": {"MinimumGlobalRecall": 0.95, "MaximumAcceptedFdrUpper": 0.01,
                            "MinimumIndependentIdentityGroups": 1000, "MaximumP95Milliseconds": 60000},
        "MaximumPhysicalRuleSets": 136, "MaximumComparedConfigurations": 1260,
        "PreviouslyComparedValidationConfigurations": 36,
        "Execution": {"MaximumPrefixLength": 4, "EndToEndRepetitions": 0,
                      "FsBlindGroupingOnly": True, "MaximumBlindClusterSize": 16,
                      "MaximumCandidatePairIncidences": 1000000, "MaximumProjectedSignatures": 1000000},
        "MinimumGroupingPosterior": 0.99, "MaximumAcceptedPerBaseIdentity": 16
    }
    with plan_path.open("x", encoding="utf-8") as stream:
        json.dump(plan, stream, indent=2)
        stream.write("\n")


def verify(root: Path) -> None:
    result = json.loads((root / "study.json").read_bytes())
    assert result["PredeclaredComparedConfigurations"] == 1260
    assert result["Discovery"]["EnumeratedUnionCount"] == 136
    assert result["Discovery"]["Options"]["NonMatchSampleSize"] == 100000
    assert len(result["Pareto"]["Table"]) == 1224
    assert result["Pareto"]["InitialSuggestedOptionId"] is None
    assert all(not row["Eligible"] for row in result["Pareto"]["Table"])
    assert all(row["ExclusionReasons"] for row in result["Pareto"]["Table"])
    assert result["EndToEndMeasurements"] == []
    rows = result["SourceMeasurements"]
    assert rows and all(row["BonferroniComparedConfigurations"] == 1260 for row in rows)
    assert all(row["CandidateGrouping"] == "BLIND_FS_COMPLETE_LINK_CANDIDATES" for row in rows)
    assert all(not row["ClusterIidIndependentlyValidated"] and not row["OperationallyCertified"] for row in rows)
    assert result["SourceSelection"]["TestUsed"] is False
    assert len(rows) + sum(len(item["ConfigurationIds"]) for item in result["Rejections"]) == 1224
    assert all(row["TrueAcceptedSourceObservations"] + row["FalseAcceptedSourceObservations"]
               + row["Conflicts"] + row["Unresolved"] == row["AllValidationObservations"] for row in rows)
    assert all(abs(row["GlobalSourceRecall"] - row["TrueAcceptedSourceObservations"]
                   / row["EligibleSourceObservationsBeforeBlocking"]) < 1e-12 for row in rows)
    for row in rows:
        accepted = row["TrueAcceptedSourceObservations"] + row["FalseAcceptedSourceObservations"]
        assert (row["FdrAfterResolve"] is None) == (accepted == 0)
        if accepted:
            assert abs(row["FdrAfterResolve"] - row["FalseAcceptedSourceObservations"] / accepted) < 1e-12
    for fit in result["FrozenConfigurations"]:
        snapshot = fit["Snapshot"]
        assert hashlib.sha256((root / (snapshot["ContentSha256"] + ".fitted.json")).read_bytes()).hexdigest() == snapshot["ContentSha256"]
    summary = {
        "scope": "HISTORICAL_TRAIN_VALIDATION_EXPLORATORY_NOT_CERTIFICATION",
        "plan_sha256": result["PlanSha256"], "configurations": 1224,
        "evaluated_source_rows": len(rows), "rejected_rows": 1224 - len(rows),
        "multiplicity_including_previous_known_family": 1260,
        "historical_validation_reused": True, "complete_historical_look_count_verified": False,
        "p95_measured": False, "test_used": False, "certified": False, "activated": False,
        "maximum_observed_global_source_recall": max(row["GlobalSourceRecall"] for row in rows),
        "minimum_observed_accepted_fdr": min((row["FdrAfterResolve"] for row in rows
                                               if row["FdrAfterResolve"] is not None), default=None)
    }
    with (root / "diagnostic-summary.json").open("x", encoding="utf-8") as stream:
        json.dump(summary, stream, indent=2)
    print(json.dumps(summary))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="mode", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("corpus", type=Path)
    prep.add_argument("plan", type=Path)
    check = commands.add_parser("verify")
    check.add_argument("result", type=Path)
    args = parser.parse_args()
    if args.mode == "prepare":
        prepare(args.corpus, args.plan)
    else:
        verify(args.result)
