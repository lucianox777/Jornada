#!/usr/bin/env python3
"""Independent reconciliation of physical 30k controls and the full catalogue census.

These controls do NOT evaluate every equivalence class or certify an optimum.
"""
import argparse
import hashlib
import json
import math
from decimal import Decimal
from pathlib import Path


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(controls: Path, census: Path):
    raw = (controls / "study.json").read_bytes()
    study = json.loads(raw)
    exact = json.loads(raw, parse_float=Decimal)
    plan = json.loads((controls / "plan.json").read_bytes())
    coverage = json.loads((census / "candidate-frame-coverage.json").read_bytes())
    graph = json.loads((census / "catalogue-census.json").read_bytes())
    assert coverage["ConfigurationsEnumerated"] == "524287"
    assert sum(c["MemberCount"] for c in coverage["Classes"]) == 524287
    assert len(coverage["Classes"]) == 60563
    assert graph["DistinctPhysicalEdges"] == 1146177
    assert coverage["FunctionalCandidateSpaceExhaustive"]
    assert not coverage["AllEquivalentPhysicalLatenciesMeasured"]
    assert study["PredeclaredComparedConfigurations"] == 14159502
    assert len(study["Pareto"]["Table"]) == 54
    assert not study["Rejections"]
    rows = study["SourceMeasurements"]
    assert len(rows) == len(study["FrozenConfigurations"]) == 54
    assert study["SourceSelection"]["TestUsed"] is False
    assert study["Pareto"]["InitialSuggestedOptionId"] is None
    assert all(not r["OperationallyCertified"] and not r["ClusterIidIndependentlyValidated"] for r in rows)
    for row in rows:
        assert row["BonferroniComparedConfigurations"] == 14159502
        assert row["TrueAcceptedSourceObservations"] + row["FalseAcceptedSourceObservations"] + row["Conflicts"] + row["Unresolved"] == row["AllValidationObservations"]
        assert math.isclose(row["GlobalSourceRecall"], row["TrueAcceptedSourceObservations"] / row["EligibleSourceObservationsBeforeBlocking"])
    for fit in exact["FrozenConfigurations"]:
        support = fit["JointSupport"]
        assert sha(controls / (fit["Snapshot"]["ContentSha256"] + ".fitted.json")) == fit["Snapshot"]["ContentSha256"]
        assert len(support["Cells"]) == 81
        m, u, a = support["TrainMPairs"], support["TrainUPairs"], Decimal(support["SmoothingAlpha"])
        assert sum(c["ObservedMPairs"] for c in support["Cells"]) == m
        assert sum(c["ObservedUPairs"] for c in support["Cells"]) == u
        for cell, probability in zip(support["Cells"], fit["Snapshot"]["Payload"]["JointCells"]):
            assert cell["Person"] == probability["Person"] and cell["Mother"] == probability["Mother"]
            assert abs(probability["M"] - (cell["ObservedMPairs"] + a) / (m + 81 * a)) < Decimal("1e-24")
            assert abs(probability["U"] - (cell["ObservedUPairs"] + a) / (u + 81 * a)) < Decimal("1e-24")
    zero = max((r for r in rows if r["FalseAcceptedSourceObservations"] == 0), key=lambda r: r["GlobalSourceRecall"])
    best = max(rows, key=lambda r: r["GlobalSourceRecall"])
    assert (zero["TrueAcceptedSourceObservations"], best["TrueAcceptedSourceObservations"], best["FalseAcceptedSourceObservations"]) == (7563, 7642, 1)
    summary = dict(scope="PHYSICAL_30K_CONTROLS_AND_EXHAUSTIVE_LABEL_BLIND_CENSUS_NOT_OPTIMALITY",
        observations_sha256=plan["ObservationCapDeclaration"]["ObservationsSha256"],
        study_sha256=sha(controls / "study.json"), plan_sha256=study["PlanSha256"],
        catalogue_census=graph, candidate_coverage_sha256=sha(census / "candidate-frame-coverage.json"),
        configurations_enumerated=524287, exact_candidate_classes=60563,
        whole_configuration_search_evaluated=False, physically_evaluated_controls=54,
        multiplicity=14159502, complete_historical_look_count_verified=False,
        best_zero_fp=zero, best_observed_recall=best, risk_frontier=study["RiskFrontier"],
        sample_design=study["SamplePlan"], test_used=False, certified=False, activated=False)
    destination = controls / "integral-verification.json"
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(summary, stream, indent=2)
        stream.write("\n")
    print(json.dumps({"verified": True, "controls": 54, "exact_classes": 60563,
        "whole_search_evaluated": False, "test_used": False, "certified": False}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("controls", type=Path)
    parser.add_argument("census", type=Path)
    args = parser.parse_args()
    verify(args.controls, args.census)
