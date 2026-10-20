#!/usr/bin/env python3
"""Real isolated SQL regression: serial/parallel, exact quotient, global-M shards.

Requires ConnectionStrings__Jornada pointing at disposable JornadaHarness.
Tiny plumbing fixture ONLY; it cannot certify statistical performance.
"""
import argparse
import importlib.util
import json
import subprocess
from pathlib import Path


def execute(worker: Path, root: Path) -> None:
    root.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location("fixture", Path(__file__).with_name("v9-adaptive-pipeline-smoke.py"))
    fixture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture)
    fixture.prepare(root / "input")
    original = json.loads((root / "input/plan.json").read_bytes())

    def run(name, plan):
        path = root / (name + ".json")
        path.write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
        output = root / name
        subprocess.run(["dotnet", str(worker.resolve()), "--v9-adaptive-study", str(path.resolve()),
            str((root / "input/observacoes.csv").resolve()), str(output.resolve())], check=True)
        return json.loads((output / "study.json").read_bytes())

    serial = run("serial", original)
    parallel = run("parallel", dict(original, MaximumParallelRuleSets=2))
    for field in ["SourceMeasurements", "FrozenConfigurations", "Rejections"]:
        assert serial[field] == parallel[field], ("serial/parallel drift", field)
    fixture.verify(root / "serial")
    fixture.verify(root / "parallel")

    full_plan = dict(original, MaximumParallelRuleSets=2, MaximumPhysicalRuleSets=7,
        MaximumComparedConfigurations=14, RiskCeilings=[.001, .01, .05],
        DeclaredSearchSpace={"Catalogue": [
            {"PassId": "A", "Fields": ["name_first"]},
            {"PassId": "B", "Fields": ["name_prefix_3"]},
            {"PassId": "C", "Fields": ["mother_name_first"]}]})
    full = run("full", full_plan)
    collapsed_plan = dict(full_plan, CollapseEquivalentCandidateFrames=True)
    quotient = run("quotient", collapsed_plan)
    coverage = quotient["CandidateFrameCoverage"]
    assert coverage["ConfigurationsEnumerated"] == "7"
    assert sum(g["MemberCount"] for g in coverage["Classes"]) == 7
    assert len(coverage["Classes"]) == 2 < full["TotalCandidateRuleSets"]
    assert not coverage["AllEquivalentPhysicalLatenciesMeasured"]
    assert quotient["CatalogueCensus"]["DistinctPhysicalEdges"] == 18
    assert quotient["PredeclaredComparedConfigurations"] == 14
    assert all(r["BonferroniComparedConfigurations"] == 14 for r in quotient["SourceMeasurements"])
    full_by_key = {(r["OptionId"], r["ConflictMargin"]): r for r in full["SourceMeasurements"]}
    assert quotient["SourceMeasurements"]
    for row in quotient["SourceMeasurements"]:
        assert row == full_by_key[(row["OptionId"], row["ConflictMargin"])]
    fits = {(r["OptionId"], r["ConflictMargin"]): r for r in full["FrozenConfigurations"]}
    for fit in quotient["FrozenConfigurations"]:
        assert fit == fits[(fit["OptionId"], fit["ConflictMargin"])]
        support = fit["JointSupport"]
        assert len(support["Cells"]) == 81
        assert sum(c["ObservedMPairs"] for c in support["Cells"]) == support["TrainMPairs"]
        assert sum(c["ObservedUPairs"] for c in support["Cells"]) == support["TrainUPairs"]

    shards = [run("shard-" + str(i), dict(collapsed_plan, ShardCount=2, ShardIndex=i)) for i in range(2)]
    assert len({s["Shard"]["FamilyPlanSha256"] for s in shards}) == 1
    assert sum(s["TotalCandidateRuleSets"] for s in shards) == quotient["TotalCandidateRuleSets"]
    combined = sorted([r for s in shards for r in s["SourceMeasurements"]], key=lambda r: r["OptionId"])
    assert combined == sorted(quotient["SourceMeasurements"], key=lambda r: r["OptionId"])
    for shard in shards:
        assert shard["PredeclaredComparedConfigurations"] == 14
        assert not shard["Shard"]["WholeSearchEvaluated"]
        assert shard["Pareto"]["InitialSuggestedOptionId"] is None
        assert shard["Pareto"]["Status"] == "PARTIAL_SEARCH_NO_GLOBAL_SUGGESTION"
    summary = dict(fixture_only=True, serial_parallel_identical=True, exact_quotient_verified=True,
        full_subsets=7, exact_classes=2, shards_reconcile=True, global_multiplicity=14,
        test_used=False, certified=False, activated=False)
    (root / "verification.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("worker", type=Path)
    parser.add_argument("new_directory", type=Path)
    args = parser.parse_args()
    execute(args.worker, args.new_directory)
