#!/usr/bin/env python3
"""Freeze the exact historical V2(default) x V3(classwise) M/U TRAIN plan.

Transforms a freshly generated, SHA-pinned V9 nested TF plan into a
single-option, two-M/U-method *technical* comparison on the known seed42.
Does not run SQL, open VALIDATION/TEST, discover values or authorize activation.
"""
import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
METHODS = ROOT / "data/reference/v9-mu-estimation-methods-20261105.json"
V3_POLICY = ROOT / "data/reference/v9-tf-frozen-joint-trial-20261105/historical-v3-support-policy.json"
VERSION = "V9_MU_V2_V3_HISTORICAL_TRAIN_PLAN_V1"
HISTORICAL_OPTION = "TF_ONE_MF_W1_M0_A0.5_T0.95"
HISTORICAL_PASSES = {"P_PHON_M_LAST", "P_LAST_M_PHON", "P_FIRST_BIRTH_YEAR"}
BUDGET = 4


def _read_json(path):
    def unique(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON member: " + key)
            result[key] = value
        return result

    def no_nonfinite(token):
        raise ValueError("Non-finite JSON token: " + token)

    return json.loads(Path(path).read_bytes(), object_pairs_hook=unique,
                      parse_constant=no_nonfinite)


def _helper():
    spec = importlib.util.spec_from_file_location("v9_tf_option_grid", HERE / "v9-tf-option-grid.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _sha(value):
    return hashlib.sha256(value).hexdigest()


def _historical_catalogue():
    spec = importlib.util.spec_from_file_location("v9_integral", HERE / "v9-integral-search-study.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.CATALOGUE


def freeze(source_plan, destination):
    source = Path(source_plan)
    destination = Path(destination)
    if destination.exists():
        raise FileExistsError("Refusing to overwrite frozen comparison plan: " + str(destination))

    raw = source.read_bytes()
    plan = _read_json(source)
    methods = _read_json(METHODS)
    v3 = _read_json(V3_POLICY)
    expected_methods = {"MuEstimationMethods": [
        {"Method": "DEFAULT"},
        {"Method": "CLASSWISE_SUPPORT_BACKOFF", "Parameters": {
            "ClassMassCoverage": [0.99], "MinimumObservedMPerCell": [5],
            "MinimumObservedUPerCell": [5], "RetainSupportedTail": [True]
        }}
    ]}
    if methods != expected_methods or v3 != {
        "ClassMassCoverage": 0.99, "MinimumM": 5, "MinimumU": 5,
        "SupportedTailRule": "RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2"
    }:
        raise ValueError("Pinned historical V3 parameters have changed; no implicit retuning")
    if not isinstance(plan, dict) or plan.get("Purpose") != "BOOTSTRAP_CANDIDATE_SELECTION" or "MuEstimationMethods" in plan:
        raise ValueError("Expected original TRAIN nested plan without an existing M/U method grid")

    calibration = plan.get("Calibration")
    if not isinstance(calibration, dict):
        raise ValueError("Nested calibration options missing")
    options = calibration.get("ModelOptions")
    if not isinstance(options, list) or len(options) < 1:
        raise ValueError("No original TF options")
    original = [x for x in options if isinstance(x, dict) and x.get("OptionId") == HISTORICAL_OPTION]
    expected = [x for x in _helper().option_grid(alpha=0.5, threshold=0.95)
                if x["OptionId"] == HISTORICAL_OPTION]
    if len(original) != 1 or len(expected) != 1 or original[0] != expected[0]:
        raise ValueError("Missing or modified historic ONE_MF alpha=.5, threshold=.95, weight=1, symmetric")
    if not isinstance(calibration.get("BaselineRuleSets"), list) or len(calibration["BaselineRuleSets"]) != 1:
        raise ValueError("Historical 3-pass physical baseline missing")
    baseline = calibration["BaselineRuleSets"][0]
    expected_catalogue = _historical_catalogue()
    expected_baseline = {key: expected_catalogue[key] for key in HISTORICAL_PASSES}
    def passes(items):
        if not isinstance(items, list) or any(not isinstance(x, dict)
                or set(x) != {"PassId", "Fields"} or not isinstance(x["PassId"], str)
                or not isinstance(x["Fields"], list) for x in items):
            raise ValueError("Missing/invalid historical blocking passes")
        mapped = {x["PassId"]: x["Fields"] for x in items}
        if len(mapped) != len(items):
            raise ValueError("Duplicate historical blocking pass")
        return mapped

    if passes(baseline) != expected_baseline:
        raise ValueError("Historical 3-pass blocking fields or IDs changed")
    declared = calibration.get("DeclaredSearchSpace")
    if not isinstance(declared, dict) or declared.get("MinimumPasses") != 19 or declared.get("MaximumPasses") != 19:
        raise ValueError("The full 19-pass TRAIN control was removed")
    if passes(declared.get("Catalogue")) != expected_catalogue:
        raise ValueError("Declared 19-pass TRAIN catalogue differs from frozen generator")

    pin = _helper().pinned_reference({key: plan[key] for key in (
        "PinnedIbgeReferenceVersionId", "PinnedIbgePublishedSha256",
        "PinnedIbgeMaterializedFrequencySha256", "PinnedIbgeBootstrap"
    )})
    if pin["PinnedIbgeBootstrap"]["People"] != 30000 or pin["PinnedIbgeBootstrap"]["GeneratorSeed"] != 42:
        raise ValueError("This reproducibility experiment accepts only already-known seed 42 and 30k")
    cap = calibration.get("ObservationCapDeclaration")
    if not isinstance(cap, dict) or any(
        not isinstance(cap.get(k), str) or len(cap[k]) != 64
        for k in ("ManifestSha256", "ObservationsSha256")
    ):
        raise ValueError("A source corpus manifest and observation hashes are required")
    if calibration.get("MaximumAcceptedPerBaseIdentity") != 4:
        raise ValueError("Historical base-identity cap changed")

    # Preserve all original risk, TF, bootstrap, splitting and execution defaults.
    # Restrict ONLY the declared initial TF and conflict-margin search family.
    result = copy.deepcopy(plan)
    result["Calibration"]["ModelOptions"] = [original[0]]
    result["Calibration"]["ConflictMargins"] = [0.5]
    result["MuEstimationMethods"] = copy.deepcopy(methods["MuEstimationMethods"])
    # This *technical* comparison evaluates just the first matched batch; do
    # not tune threshold, margins or blocking in a regression-only experiment.
    result["MaximumBatches"] = 1
    result["MaximumSearchConfigurations"] = BUDGET
    result["Calibration"]["MaximumComparedConfigurations"] = BUDGET
    result["Calibration"]["MaximumPhysicalRuleSets"] = 2
    result["Calibration"]["MaximumParallelRuleSets"] = min(2, result["Calibration"]["MaximumParallelRuleSets"])
    # The existing physical post-Resolve selector (V9PostResolveAdaptiveSelector)
    # requires a predeclared accepted FDR budget <= 0.01. The exploratory TF
    # producer uses 1.0: narrow it here, NEVER change the selector or relax
    # the statistical gate merely to make the technical test pass.
    result["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"] = min(
        0.01, result["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"])
    result["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"] = min(
        0.01, result["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"])
    # Exactly four paired initial comparisons: two policies x one TF x one
    # margin x {19-pass catalogue, historical three-pass baseline}. No tuning.
    if not (result["MaximumBatches"] >= 1 and result["Calibration"]["MaximumParallelRuleSets"] >= 1
            and result["Calibration"].get("Execution", {}).get("EndToEndRepetitions", 0) >= 20):
        raise ValueError("Original train plan lacks required physical execution budget")
    identity = _sha((VERSION + "|" + _sha(raw) + "|" + json.dumps(
        result["MuEstimationMethods"], sort_keys=True, separators=(",", ":")) +
        "|" + HISTORICAL_OPTION + "|0.5|19+3").encode())
    result["Calibration"]["StudyFingerprintSha256"] = identity
    result["Calibration"]["Budget"]["StudyFingerprintSha256"] = identity
    # If the source uses an explicit TOTAL field, do not preserve a now-stale
    # count for the original 41-option search.
    result["Calibration"]["Execution"].pop("TotalComparedConfigurations", None)
    result["Calibration"].pop("SampleDesign", None)
    # No output write until every preflight condition has been checked.
    data = (json.dumps(result, indent=2, allow_nan=False) + "\n").encode()
    with destination.open("xb") as handle:
        handle.write(data)
    return dict(version=VERSION, planSha256=_sha(data), originalPlanSha256=_sha(raw),
                studyFingerprintSha256=identity, initialComparisons=4,
                methods=2, mode="KNOWN_SEED42_TRAIN_TECHNICAL_NON_CONFIRMATORY",
                testUsed=False, operationalActivation=False)


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("source_nested_tf_plan", type=Path)
    cli.add_argument("new_mu_plan", type=Path)
    args = cli.parse_args()
    print(json.dumps(freeze(args.source_nested_tf_plan, args.new_mu_plan),
                     sort_keys=True))
