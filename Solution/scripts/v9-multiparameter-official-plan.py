#!/usr/bin/env python3
"""Build the SINGLE official V9 multi-parameter plan.json from pinned V2 provenance.

Do not create an experiment.json or a scientific second plan. The historical
V2 defaults, TF0/ONE_MF option, 3/19 blocking, pin, TF, risk, prior and
TRAIN identity split are inherited and checked by the existing frozen
technical chain. This builder produces a new, explicitly role-tagged study;
historical plans and fits remain byte-identical on disk.
"""
from __future__ import annotations
import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile

HERE = Path(__file__).resolve().parent
SCHEMA = "V9_OFFICIAL_MULTIPARAM_V2_WITNESS_ANCHOR_V1"
ROLES = ("PRIMARY", "PREDECLARED_SENSITIVITY")
PHYSICAL_COUNT = 24
WITNESS_COUNT = 16  # V2 4 + lambda=0 4 + eight diagnostic 19-pass candidate-role arms
CANDIDATE_CP_COUNT = PHYSICAL_COUNT - WITNESS_COUNT  # eight selectable three-pass arms


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def read(path: Path) -> dict:
    def unique(items):
        out = {}
        for key, value in items:
            if key in out:
                raise ValueError("Duplicate JSON member in official plan")
            out[key] = value
        return out
    return json.loads(path.read_bytes(), object_pairs_hook=unique,
                      parse_constant=lambda _: (_ for _ in ()).throw(
                          ValueError("Nonfinite number in plan")))


def canonical_explicit_option(legacy: dict) -> dict:
    # Effective C# V9FsAdaptiveOptionConfiguration historical defaults.
    # All fields EXPLICIT in new official executable plan.json (not an
    # explanatory sidecar). Never change the effective fingerprint/fit.
    defaults = {
        "TfSide": 0,
        "TfMinimumFrequency": .000001,
        "TfMinimumIndependentSupport": 1,
        "JointSupportPolicy": None,
        "UnitTfOptions": None,
        "SharedSurnameMode": 0,
        "EligibleForSelection": True,
        "UnpublishedTermPolicy": 0,  # historical V2 default; frozen source stays unchanged
    }
    if any(k in legacy and legacy[k] != v for k, v in defaults.items()
           if k != "UnitTfOptions"):
        raise ValueError("Historical V2 parameter differs from exact default: no retuning allowed")
    if set(legacy) - (set(defaults) | {
        "OptionId", "SmoothingAlpha", "DecisionThreshold", "TfWeight"}):
        raise ValueError("Unexpected historical V2 parameter, cannot silently discard")
    # Keep the ORIGINAL four-unit TF vector byte-for-byte, if present;
    # adding None would silently change the ONE_MF historical control.
    return {**defaults, **legacy}


def freeze(source_plan: Path, official_plan: Path, role: str = "PRIMARY") -> dict:
    source_plan, official_plan = Path(source_plan), Path(official_plan)
    if official_plan.name != "plan.json":
        raise ValueError("The only executable scientific declaration must be plan.json")
    if official_plan.exists():
        raise FileExistsError("Never overwrite an official plan.json")
    if role not in ROLES:
        raise ValueError("Scientific margin role not preregistered")
    rare = load_module(HERE / "v9-u-rare-mixture-plan.py", "v9_rare_official")
    historical = load_module(HERE / "v9-mu-historical-plan.py", "v9_hist_official")
    control = load_module(HERE / "v9-mu-tf0-controlled-plan.py", "v9_tf0_official")
    source_raw = source_plan.read_bytes()
    # Historical source and its effective TF options, 3/19-pass catalogue,
    # published IBGE reference, clean 30k/seed42 cap, split, FDR and full
    # 81-cell V2/V3 baseline are vetted BEFORE the new scientific plan exists.
    with tempfile.TemporaryDirectory(prefix="v9-official-build-") as temporary:
        intermediate = Path(temporary) / "technical-canonical.json"
        receipt = rare.freeze(source_plan, intermediate,
                              margin=rare.margin_for_role(role), weights=None)
        plan = read(intermediate)
    if len(plan["MuEstimationMethods"]) != 3 or len(plan["Calibration"]["ModelOptions"]) != 2:
        raise ValueError("V2/V3/U methods or canonical two TF options missing")
    methods = plan["MuEstimationMethods"]
    expected = [{"Method": "DEFAULT"}, {"Method": "CLASSWISE_SUPPORT_BACKOFF",
        "Parameters": {"ClassMassCoverage": [.99], "MinimumObservedMPerCell": [5],
                       "MinimumObservedUPerCell": [5], "RetainSupportedTail": [True]}}]
    if methods[:2] != expected or methods[2].get("Method") != rare.MIXED_FAMILY:
        raise ValueError("Frozen historical V2/V3 methods must not be reinterpreted")
    if methods[2]["Parameters"] != {**expected[1]["Parameters"],
                                  "UBackoffJointMixtureWeights": [0, .25, .5, 1]}:
        raise ValueError("U mixture weights not predeclared")
    methods[0] = {"Method": "DEFAULT", "Role": "WITNESS", "Anchor": True}
    methods[1]["Role"] = "CANDIDATE"
    methods[2]["Role"] = "CANDIDATE"
    opts = plan["Calibration"]["ModelOptions"]
    canonical = historical._helper().option_grid(alpha=.5, threshold=.95)
    by_id = {o["OptionId"]: o for o in canonical}
    if [o["OptionId"] for o in opts] != [control.TF0, control.ONE_MF]:
        raise ValueError("Historical V2 TF arm missing/reordered")
    historical_opts = [canonical_explicit_option(opt) for opt in opts]
    for i, opt in enumerate(opts):
        if opt != by_id[opt["OptionId"]]:
            raise ValueError("Historical V2 TF controls changed")
        opts[i] = canonical_explicit_option(opt)
        # Explicit NEW study policy: retain the historical source/control
        # provenance but use the agreed opt-in abstention for positive TF.
        # TF0 does not consult published nominal frequencies.
        if opt["OptionId"] != control.TF0:
            opts[i]["UnpublishedTermPolicy"] = 1
    if (len(plan["Calibration"]["BaselineRuleSets"]) != 1 or
        plan["Calibration"]["DeclaredSearchSpace"]["MinimumPasses"] != 19 or
        plan["Calibration"]["DeclaredSearchSpace"]["MaximumPasses"] != 19 or
        plan["Calibration"]["ConflictMargins"] != [rare.margin_for_role(role)] or
        plan["MaximumBatches"] != 1 or
        plan["MaximumSearchConfigurations"] != PHYSICAL_COUNT or
        plan["Calibration"]["MaximumComparedConfigurations"] != PHYSICAL_COUNT or
        plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"] != .01 or
        plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"] != .01):
        raise ValueError("Official V2/V3/mixture support, risk or physical count drifted")
    plan["PredeclaredCandidateCpMultiplicity"] = CANDIDATE_CP_COUNT
    # The scientific source digest changes BECAUSE this is a new role-tagged
    # study, not because the original historical V2 model/params changed.
    base_identity = plan["Calibration"]["StudyFingerprintSha256"]
    provenance = json.dumps({
        "schema": SCHEMA, "marginRole": role, "sourceBytesSha256": hashlib.sha256(
            source_raw).hexdigest(), "historicalBaseIdentity": base_identity,
        "muMethods": methods, "effectiveHistoricalModelOptions": historical_opts,
        "effectiveStudyModelOptions": opts,
        "cpOnlyCandidates": CANDIDATE_CP_COUNT,
    }, sort_keys=True, separators=(",", ":"), allow_nan=False)
    fingerprint = hashlib.sha256(provenance.encode()).hexdigest()
    plan["Calibration"]["StudyFingerprintSha256"] = fingerprint
    plan["Calibration"]["Budget"]["StudyFingerprintSha256"] = fingerprint
    # No second executable scientific plan or experiment.json on disk.
    official_plan.parent.mkdir(parents=True, exist_ok=True)
    raw = (json.dumps(plan, indent=2, allow_nan=False) + "\n").encode()
    with official_plan.open("xb") as stream:
        stream.write(raw)
    return {
        "schema": SCHEMA, "status": "OFFICIAL_PLAN_CREATED_NOT_PHYSICALLY_EXECUTED",
        "officialPlanSha256": hashlib.sha256(raw).hexdigest(),
        "sourcePlanSha256": hashlib.sha256(source_raw).hexdigest(),
        "studyFingerprintSha256": fingerprint,
        "V2historicalMethod": "DEFAULT", "V2role": "WITNESS", "V2anchor": True,
        "V2exactConfigurationCount": len(opts), "physicallyDeclared": PHYSICAL_COUNT,
        "initialWitnessHypotheses": WITNESS_COUNT,
        "predeclaredCandidateCpMultiplicity": CANDIDATE_CP_COUNT,
        "sameFrame": True, "marginRole": role, "fdrCeiling": .01,
        "roleProvenance": receipt["schema"], "legacyHistoricalParametersMutated": False,
        "testUsed": False, "goldUsed": False, "certified": False, "activated": False,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_nested_tf_plan", type=Path)
    parser.add_argument("official_plan_json", type=Path)
    parser.add_argument("--role", choices=ROLES, default="PRIMARY")
    args = parser.parse_args()
    print(json.dumps(freeze(args.source_nested_tf_plan, args.official_plan_json,
                            args.role), sort_keys=True))


if __name__ == "__main__":
    main()
