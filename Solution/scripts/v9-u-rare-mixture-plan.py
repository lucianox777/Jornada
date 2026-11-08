#!/usr/bin/env python3
"""Freeze an opt-in U rare-mixture TRAIN diagnostic from the existing TF0 V2/V3 frame.

Only offline plan creation. Preserves V2 and V3 baseline beside the mixed arm.
There is NO implicit physical dispatch, promotion,
Gold/TEST/VALIDATION access, optimization, or operational authorization.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile

SCRIPTS = Path(__file__).resolve().parent
VERSION = "V9_U_RARE_MIXTURE_THREE_METHOD_TRAIN_PLAN_V2"
LEGACY_WEIGHTS = [0, .25, .5, 1]  # frozen historical experiment only
FAMILY = "CLASSWISE_SUPPORT_BACKOFF"
MIXED_FAMILY = "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"
SELECTION_POLICIES = ("COUNT_ALL", "COUNT_UNIQUE")


# Declared by this versioned scientific protocol, never injected from environment.
# The generated executable plan.json contains the selected margin, all M/U
# methods, mixture weights, selection policies, provenance and own SHA.
MARGINS_BY_ROLE = {"PRIMARY": .5, "PREDECLARED_SENSITIVITY": .25}


def margin_for_role(role):
    if role not in MARGINS_BY_ROLE:
        raise ValueError("Undeclared scientific margin role")
    return MARGINS_BY_ROLE[role]


def _load_control():
    spec = importlib.util.spec_from_file_location("v9_mu_tf0_controlled_rare", SCRIPTS / "v9-mu-tf0-controlled-plan.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def freeze(source_path, target_path, margin=.5, weights=None, duplicate_policies=None):
    # The .25 sensitivity is a distinct, predeclared TRAIN experiment.
    # The historical .5 plan remains byte-identical unless opted in.
    if type(margin) not in (int, float) or not 0 <= margin <= 1:
        raise ValueError('Margin must be finite in [0,1]')
    # Zero is diagnostic only; do not turn this study into a selectable arm.
    if margin == 0:
        raise ValueError('Zero margin is diagnostic-only; use the dedicated diagnostic plan')
    weights = list(LEGACY_WEIGHTS if weights is None else weights)
    duplicate_policies = list(SELECTION_POLICIES if duplicate_policies is None else duplicate_policies)
    if (not duplicate_policies or len(set(duplicate_policies)) != len(duplicate_policies)
            or any(x not in SELECTION_POLICIES for x in duplicate_policies)):
        raise ValueError("DuplicateCandidatePolicies must be a nonempty unique subset of COUNT_ALL, COUNT_UNIQUE")
    if not weights or any(type(w) not in (int, float) or not 0 <= w <= 1 for w in weights) or len(set(weights)) != len(weights):
        raise ValueError('Weights must be a nonempty, unique, finite list in [0,1]')
    source_path, target_path = Path(source_path), Path(target_path)
    if target_path.exists():
        raise FileExistsError(target_path)
    control = _load_control()
    with tempfile.TemporaryDirectory(prefix="v9-u-rare-offline-") as temporary:
        controlled = Path(temporary) / "controlled.json"
        control.freeze(source_path, controlled)
        plan = json.loads(controlled.read_bytes())
    original = source_path.read_bytes()
    methods = plan["MuEstimationMethods"]
    expected = [
        {"Method": "DEFAULT"},
        {"Method": FAMILY, "Parameters": {
            "ClassMassCoverage": [.99],
            "MinimumObservedMPerCell": [5],
            "MinimumObservedUPerCell": [5],
            "RetainSupportedTail": [True],
        }},
    ]
    if methods != expected or plan["Calibration"]["ConflictMargins"] != [.5]:
        raise ValueError("Historical V2/V3 frozen control or margin drifted")
    if [o["OptionId"] for o in plan["Calibration"]["ModelOptions"]] != [control.TF0, control.ONE_MF]:
        raise ValueError("Both frozen TF0 and ONE_MF options are required")
    if len(plan["Calibration"]["BaselineRuleSets"]) != 1 or plan["MaximumBatches"] != 1:
        raise ValueError("Expected only the frozen 3/19 blocking study")
    if (plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"] != .01
            or plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"] != .01):
        raise ValueError("Risk limit must remain fixed at the source 1% guard")
    # Preserve the two historical methods byte-for-byte in their method slots.
    # The third is a separately identifiable opt-in estimator family.
    if margin != .5:
        plan["Calibration"]["ConflictMargins"] = [margin]
    plan["SelectionDuplicatePolicies"] = duplicate_policies
    methods.append({"Method": MIXED_FAMILY, "Parameters": {
        **methods[1]["Parameters"], "UBackoffJointMixtureWeights": weights
    }})
    # (one historical DEFAULT + one historical V3 + four explicit U-mixture weights)
    # x two frozen TF arms x two frozen blocking rule sets.
    declared = (2 + len(weights)) * len(plan["Calibration"]["ModelOptions"]) * (1 + len(plan["Calibration"]["BaselineRuleSets"]))
    if declared > 256:
        raise ValueError("Experimental cardinality exceeds explicit safety cap")
    plan["MaximumSearchConfigurations"] = declared
    plan["Calibration"]["MaximumComparedConfigurations"] = declared
    basis = VERSION + "|" + hashlib.sha256(original).hexdigest() + "|" + json.dumps(methods, sort_keys=True, separators=(",", ":"))
    if margin != .5:
        basis += "|EXPLICIT_MARGIN_" + str(margin)
    basis += "|SELECTION_DUPLICATE_POLICIES_" + ",".join(duplicate_policies)
    digest = hashlib.sha256(basis.encode()).hexdigest()
    plan["Calibration"]["StudyFingerprintSha256"] = digest
    plan["Calibration"]["Budget"]["StudyFingerprintSha256"] = digest
    raw = (json.dumps(plan, indent=2, allow_nan=False) + "\n").encode()
    with target_path.open("xb") as out:
        out.write(raw)
    return {
        "schema": VERSION,
        "sourceSha256": hashlib.sha256(original).hexdigest(),
        "planSha256": hashlib.sha256(raw).hexdigest(),
        "studyFingerprintSha256": digest,
        "comparisons": declared,
        "estimatorMethods": 3,
        "estimatedVariants": 2 + len(weights),
        "historicalV3Preserved": True,
        "tfArms": 2,
        "blockingArms": 2,
        "uJointWeights": weights,
        "selectionDuplicatePolicies": duplicate_policies,
        "margin": margin,
        "scope": "KNOWN_SYNTHETIC_TRAIN_SELECTION_ONLY_NOT_INDEPENDENT",
        "physicalExecuted": False,
        "testUsed": False,
        "goldUsed": False,
        "certified": False,
        "activated": False,
    }


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("source_nested_tf_plan", type=Path)
    cli.add_argument("output_plan", type=Path)
    cli.add_argument("--margin", type=float, default=None,
                     help="Legacy offline-only study margin; disallowed with --role")
    cli.add_argument("--role", choices=tuple(MARGINS_BY_ROLE),
                     help="Select one fixed, predeclared role; frozen into resulting plan")
    cli.add_argument("--weights", type=str, default=None,
                     help="Legacy offline-only mixture weights; disallowed with --role")
    args = cli.parse_args()
    if args.role is not None:
        if args.margin is not None or args.weights is not None:
            cli.error("--role forbids --margin and --weights overrides")
        margin, weights = margin_for_role(args.role), None
    else:
        margin = .5 if args.margin is None else args.margin
        weights = None if args.weights is None else [float(x) for x in args.weights.split(",")]
    receipt = freeze(args.source_nested_tf_plan, args.output_plan,
                     margin=margin, weights=weights)
    if args.role is not None:
        receipt["marginRole"] = args.role
        receipt["declaredMarginsByRole"] = dict(MARGINS_BY_ROLE)
    print(json.dumps(receipt, sort_keys=True))
