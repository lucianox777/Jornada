#!/usr/bin/env python3
"""Prepare/verify a tiny SQL plumbing fixture. Never a statistical experiment or TEST."""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def prepare(root: Path) -> None:
    root.mkdir(parents=True, exist_ok=False)
    names = ["MARIA SILVA", "MARCOS LIMA", "MARTA COSTA"]
    with (root / "observacoes.csv").open("x", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["base_person_id", "particao", "gestor", "nome", "nome_mae", "data_nascimento", "cpf"])
        for partition in ["TRAIN", "VALIDATION"]:
            for index, name in enumerate(names):
                for manager in ["G1", "G2"]:
                    writer.writerow([f"{partition}-{index}", partition, manager, name,
                                     "ANA " + name.split()[-1], f"1980-05-{10 + index}", ""])
    study = hashlib.sha256(b"V9_ADAPTIVE_PHYSICAL_PLUMBING_FIXTURE_V1_NOT_STATISTICAL").hexdigest()
    plan = {
        "StudyFingerprintSha256": study,
        "Discovery": {"MaximumPrefixLength": 4, "NonMatchSampleSize": 6,
                      "SearchOptions": {"MaxFieldsPerPass": 1, "MaxPasses": 1,
                                        "PrimitivePoolSize": 27, "MinimumTrueMatchRecall": 0,
                                        "RequireObservedNonMatchSupport": True,
                                        "CandidateRuleSetCap": 100, "RequireExhaustivePrimitivePool": True}},
        "ModelOptions": [{"OptionId": "LOW", "SmoothingAlpha": 0.5, "DecisionThreshold": 0.5},
                         {"OptionId": "HIGH", "SmoothingAlpha": 0.5, "DecisionThreshold": 0.999999}],
        "ConflictMargins": [0.5],
        "Budget": {"Version": "ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1", "StudyFingerprintSha256": study,
                   "MinimumGlobalAcceptedRecall": 0, "MaximumObservedAcceptedFdrPoint": 0.01,
                   "MaximumBlockingFalseDegreeP99": 100, "MinimumValidationIdentityGroups": 2},
        "SelectionPolicy": {"MinimumGlobalRecall": 0, "MaximumAcceptedFdrUpper": 0.01,
                            "MinimumIndependentIdentityGroups": 2, "MaximumP95Milliseconds": 60000},
        "MaximumPhysicalRuleSets": 27, "MaximumComparedConfigurations": 54,
        "Execution": {"MaximumPrefixLength": 4, "EndToEndRepetitions": 20,
                      "FsBlindGroupingOnly": True, "MaximumBlindClusterSize": 16},
        "MinimumGroupingPosterior": 0.99, "MaximumAcceptedPerBaseIdentity": 16
    }
    (root / "plan.json").write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")


def verify(root: Path) -> None:
    result = json.loads((root / "study.json").read_text(encoding="utf-8"))
    assert result["PredeclaredComparedConfigurations"] == 54
    assert result["Discovery"]["ExhaustivePrimitivePool"] is True
    assert len(result["Pareto"]["Table"]) == 54
    assert result["Pareto"]["InitialSuggestedOptionId"] is None
    assert all(not row["Eligible"] for row in result["Pareto"]["Table"])
    assert all(row["ExclusionReasons"] for row in result["Pareto"]["Table"])
    measurements = result["SourceMeasurements"]
    assert measurements, "No complete SQL->FS->Rank->Resolve execution reached the report"
    assert all(row["BonferroniComparedConfigurations"] == 54 for row in measurements)
    assert all(row["CandidateGrouping"] == "BLIND_FS_COMPLETE_LINK_CANDIDATES" for row in measurements)
    assert all(row["ClusterIidIndependentlyValidated"] is False for row in measurements)
    assert all(row["OperationallyCertified"] is False for row in measurements)
    assert all(row["AllValidationObservations"] == 6 for row in measurements)
    low = [row for row in measurements if row["OptionId"].endswith(":LOW")]
    high = [row for row in measurements if row["OptionId"].endswith(":HIGH")]
    assert any(row["TrueAcceptedSourceObservations"] == 6 for row in low)
    assert high and all(row["TrueAcceptedSourceObservations"] == 0 for row in high)
    times = result["EndToEndMeasurements"]
    assert len(times) == len(measurements)
    assert all(len(row["SamplesMilliseconds"]) == 20 and row["Quantiles"]["SampleCount"] == 20 for row in times)
    assert all(row["Quantiles"]["P95Milliseconds"] > 0 for row in times)
    fits = result["FrozenConfigurations"]
    assert len(fits) == len(measurements)
    prefix_fits = [fit for fit in fits if any("name_prefix_" in field
                  for item in fit["Snapshot"]["Payload"]["BlockingPasses"] for field in item["Fields"])]
    assert prefix_fits, "Opt-in prefix keys were never executed by actual SQL"
    for fit in fits:
        snapshot = fit["Snapshot"]
        raw = (root / (snapshot["ContentSha256"] + ".fitted.json")).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == snapshot["ContentSha256"]
        assert len(snapshot["Payload"]["JointCells"]) == 81
        assert snapshot["Payload"]["ConflictMargin"] == fit["ConflictMargin"]
    assert result["SourceSelection"]["TestUsed"] is False
    print(json.dumps({"fixture_only": True, "configurations": 54,
                      "physical_full_path_rows": len(measurements), "test_used": False,
                      "p95_min_ms": min(row["Quantiles"]["P95Milliseconds"] for row in times),
                      "p95_max_ms": max(row["Quantiles"]["P95Milliseconds"] for row in times),
                      "portable_fit_count": len(fits),
                      "certified": False, "activated": False}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["prepare", "verify"])
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    (prepare if args.mode == "prepare" else verify)(args.directory)
