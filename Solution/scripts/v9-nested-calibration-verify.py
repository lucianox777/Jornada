#!/usr/bin/env python3
"""Real SQL regression of the default nested V9 flow, without performance certification."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
import subprocess
from pathlib import Path


def verify_support_math(snapshot):
    """Independent numerical check; do not trust the C# loader or its metadata."""
    provenance = snapshot["JointSupport"]
    cells = sorted(provenance["Cells"], key=lambda c: (c["Person"], c["Mother"]))
    actual = {(c["Person"], c["Mother"]): c for c in snapshot["JointCells"]}
    alpha = provenance["SmoothingAlpha"]
    coverage = provenance["Policy"]["ClassMassCoverage"]
    for kind in ["M", "U"]:
        total = sum(c["Observed" + kind] for c in cells)
        cumulative, core = 0, set()
        for c in sorted(cells, key=lambda c: (-c["Observed" + kind], c["Person"], c["Mother"])):
            core.add((c["Person"], c["Mother"]))
            cumulative += c["Observed" + kind]
            if cumulative / total >= coverage:
                break
        assert all(c["Core" + kind] == ((c["Person"], c["Mother"]) in core) for c in cells)
        assert math.isclose(provenance["ObservedCoreMass" + kind], cumulative / total, rel_tol=1e-12)
        denominator = total + 81 * alpha
        person = {i: (sum(c["Observed" + kind] for c in cells if c["Person"] == i) + 9 * alpha) / denominator for i in range(9)}
        mother = {i: (sum(c["Observed" + kind] for c in cells if c["Mother"] == i) + 9 * alpha) / denominator for i in range(9)}
        retain_tail = provenance["Policy"].get("SupportedTailRule") == "RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2"
        joint = core | {(c["Person"], c["Mother"]) for c in cells
            if retain_tail and c["Observed" + kind] >= provenance["Policy"]["Minimum" + kind]}
        rare = [c for c in cells if (c["Person"], c["Mother"]) not in joint]
        residual = sum((c["Observed" + kind] + alpha) / denominator for c in rare)
        product_mass = sum(person[c["Person"]] * mother[c["Mother"]] for c in rare)
        scale = residual / product_mass if rare else 0
        assert math.isclose(provenance["FallbackScale" + kind], scale, rel_tol=1e-12, abs_tol=1e-28)
        for c in cells:
            key = c["Person"], c["Mother"]
            expected_method = ("JOINT_DIRICHLET" if key in core else "JOINT_DIRICHLET_SUPPORTED_TAIL"
                if key in joint else "CONDITIONAL_PRODUCT_MARGINALS")
            assert c["Method" + kind] == expected_method
            expected = ((c["Observed" + kind] + alpha) / denominator if key in joint
                else scale * person[c["Person"]] * mother[c["Mother"]])
            assert math.isclose(actual[key][kind], expected, rel_tol=1e-12, abs_tol=1e-28), (kind, key)


def read_selection(root):
    reference = json.loads((root / "selection-reference.json").read_bytes())
    path = root / (reference["envelopeSha256"] + ".selection.json")
    raw = path.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == reference["envelopeSha256"]
    document = json.loads(raw)
    result = document["result"]
    assert not result["ExternalValidationUsed"] and not result["TestUsed"] and not result["Activated"]
    assert result["SnapshotEvaluationSlotPurpose"] == "TRAIN_SELECTION_NOT_EXTERNAL_VALIDATION"
    assignments = result["Split"]["Assignments"]
    ids = [a["BaseIdentity"] for a in assignments]
    assert len(ids) == len(set(ids))
    fit = {a["BaseIdentity"] for a in assignments if a["Purpose"] == "TRAIN_ADJUST"}
    selection = {a["BaseIdentity"] for a in assignments if a["Purpose"] == "TRAIN_SELECTION"}
    assert fit.isdisjoint(selection) and len(fit) == int(len(ids) * .7)
    plan = json.loads((root / "plan.json").read_bytes())
    assert len(result["Evaluations"]) <= plan["MaximumSearchConfigurations"]
    assert len({r["RequestSha256"] for r in result["Evaluations"]}) == len(result["Evaluations"])
    for row in result["Evaluations"]:
        candidate = row["Candidate"]
        if candidate is None:
            assert row["Rejections"]
            continue
        metric = candidate["Metric"]
        groups = metric["IdentityOutcomes"]
        assert {g["BaseIdentity"] for g in groups} == selection
        assert sum(g["EligibleSources"] for g in groups) == metric["EligibleSourceObservationsBeforeBlocking"]
        assert sum(g["TrueAccepted"] for g in groups) == metric["TrueAcceptedSourceObservations"]
        assert sum(g["FalseAccepted"] for g in groups) == metric["FalseAcceptedSourceObservations"]
        assert not metric["SyntheticGoldOracleGrouping"]
        assert metric["CandidateGrouping"] == "BLIND_FS_COMPLETE_LINK_CANDIDATES"
        support = candidate["Support"]
        assert len(support["Cells"]) == 81
        sha = candidate["Snapshot"]["ContentSha256"]
        raw_snapshot = (root / (sha + ".fitted.json")).read_bytes()
        assert hashlib.sha256(raw_snapshot).hexdigest() == sha
        snapshot = json.loads(raw_snapshot)
        verify_support_math(snapshot)
        provenance = snapshot["JointSupport"]
        assert snapshot["SchemaVersion"] == "V9_FITTED_SNAPSHOT_V3"
        assert provenance["TrainCorpusSha256"] == snapshot["TrainCorpusSha256"]
        expected_policy = dict(ClassMassCoverage=plan["ClassMassCoverage"],
            MinimumM=plan["MinimumObservedMPerCell"], MinimumU=plan["MinimumObservedUPerCell"])
        if plan.get("SupportedTailRule") is not None:
            expected_policy["SupportedTailRule"] = plan["SupportedTailRule"]
        assert provenance["Policy"] == expected_policy
        assert provenance["ObservedCoreMassM"] >= plan["ClassMassCoverage"]
        assert provenance["ObservedCoreMassU"] >= plan["ClassMassCoverage"]
        assert sum(c["ObservedM"] for c in provenance["Cells"]) == support["TrainMPairs"]
        assert sum(c["ObservedU"] for c in provenance["Cells"]) == support["TrainUPairs"]
        insufficient = any((c["CoreM"] and c["ObservedM"] < plan["MinimumObservedMPerCell"])
            or (c["CoreU"] and c["ObservedU"] < plan["MinimumObservedUPerCell"]) for c in provenance["Cells"])
        assert ("SUPORTE_OBSERVADO_NO_NUCLEO_DE_MASSA_INSUFICIENTE" in row["Rejections"]) == insufficient
        for kind in ["M", "U"]:
            assert abs(sum(c[kind] for c in snapshot["JointCells"]) - 1) < 1e-12
            assert all(c[kind] > 0 for c in snapshot["JointCells"])

    if result["StopReason"] == "TWO_STAGNANT_BATCHES_HEURISTIC_NOT_OPTIMALITY":
        assert [p["ConsecutiveStagnantBatches"] for p in result["Progress"][-2:]] == [1, 2]
        assert all(p["MaximumGainInResolutionUnits"] <= 1 for p in result["Progress"][-2:])
    return path, document


def fixture(worker, train, fresh, root):
    root.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location("nested_plan", Path(__file__).with_name("v9-nested-calibration-study.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    plan = root / "fixture-plan.json"
    module.prepare(train, plan, fixture=True)
    subprocess.run(["dotnet", str(worker), "--v9-calibrate", str(plan), str(train), str(root / "search")], check=True)
    envelope, document = read_selection(root / "search")
    finalists = document["result"]["FinalistSnapshotShas"]
    assert 1 <= len(finalists) <= 3
    before = {sha: (root / "search" / (sha + ".fitted.json")).read_bytes() for sha in finalists}
    env = dict(os.environ, JORNADA_V9_HOLDOUT_REGISTRY=str((root / "fixture-exposure-registry").resolve()))

    def validate(source, output):
        return subprocess.run(["dotnet", str(worker), "--v9-validate-finalists", str(plan), str(envelope),
            str(source), str(root / output)], env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")

    old = validate(train, "historical-rejected")
    assert old.returncode != 0 and "VALIDATION histórica/exposta" in old.stderr
    valid = validate(fresh, "validation")
    if valid.returncode:
        raise AssertionError(valid.stderr)
    report = json.loads((root / "validation/finalist-validation.json").read_bytes())
    assert report["comparedConfigurations"] == len(finalists)
    assert not report["refitted"] and not report["certified"] and not report["testUsed"] and not report["activated"]
    assert not report["identityClusterIidIndependentlyValidated"]
    assert report["selectedApprovedSnapshotSha256"] is None
    for measured in report["measured"]:
        assert measured["SnapshotSha256"] in finalists and not measured["Refitted"]
        assert measured["Metric"]["BonferroniComparedConfigurations"] == len(finalists)
        assert len(measured["SamplesMilliseconds"]) == 20 and measured["P95Milliseconds"] > 0
    reused = validate(fresh, "reused-rejected")
    assert reused.returncode != 0
    assert json.loads((root / "reused-rejected/interrupted.json").read_bytes())["exceptionType"] == "IOException"
    assert len(list((root / "fixture-exposure-registry").glob("*.json"))) == 1
    assert all((root / "search" / (sha + ".fitted.json")).read_bytes() == raw for sha, raw in before.items())
    summary = dict(fixture_only=True, nested_train_search_verified=True, external_validation_not_used_in_search=True,
        historical_validation_rejected=True, one_time_holdout_verified=True, no_refit=True,
        finalists=len(finalists), compared_configurations=len(finalists), certified=False, test_used=False, activated=False)
    (root / "verification.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="mode", required=True)
    run = sub.add_parser("fixture")
    run.add_argument("worker", type=Path)
    run.add_argument("train", type=Path)
    run.add_argument("fresh", type=Path)
    run.add_argument("new_directory", type=Path)
    verify = sub.add_parser("search")
    verify.add_argument("directory", type=Path)
    args = parser.parse_args()
    if args.mode == "fixture":
        fixture(args.worker.resolve(), args.train.resolve(), args.fresh.resolve(), args.new_directory.resolve())
    else:
        _, doc = read_selection(args.directory)
        print(json.dumps(dict(verified=True, external_validation_used=False, test_used=False,
            configurations=len(doc["result"]["Evaluations"]), finalists=len(doc["result"]["FinalistSnapshotShas"]), certified=False)))
