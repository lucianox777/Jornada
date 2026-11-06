#!/usr/bin/env python3
"""Freeze an opt-in U rare-mixture TRAIN diagnostic from the existing TF0 V2/V3 frame.

Only offline plan creation. There is NO implicit physical dispatch, promotion,
Gold/TEST/VALIDATION access, optimization, or operational authorization.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile

SCRIPTS = Path(__file__).resolve().parent
VERSION = "V9_U_RARE_MIXTURE_PAIRED_TRAIN_PLAN_V1"
WEIGHTS = [0, .25, .5, 1]
FAMILY = "CLASSWISE_SUPPORT_BACKOFF"


def _load_control():
    spec = importlib.util.spec_from_file_location("v9_mu_tf0_controlled_rare", SCRIPTS / "v9-mu-tf0-controlled-plan.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def freeze(source_path, target_path):
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
    methods[1]["Parameters"]["UBackoffJointMixtureWeights"] = WEIGHTS
    # (one historical DEFAULT + four explicit V3 U-mixture weights)
    # x two frozen TF arms x two frozen blocking rule sets.
    declared = (1 + len(WEIGHTS)) * len(plan["Calibration"]["ModelOptions"]) * (1 + len(plan["Calibration"]["BaselineRuleSets"]))
    if declared != 20:
        raise ValueError("Unanticipated experimental cardinality")
    plan["MaximumSearchConfigurations"] = declared
    plan["Calibration"]["MaximumComparedConfigurations"] = declared
    basis = VERSION + "|" + hashlib.sha256(original).hexdigest() + "|" + json.dumps(methods, sort_keys=True, separators=(",", ":"))
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
        "estimatorMethods": 2,
        "estimatedVariants": 5,
        "tfArms": 2,
        "blockingArms": 2,
        "uJointWeights": WEIGHTS,
        "margin": .5,
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
    args = cli.parse_args()
    print(json.dumps(freeze(args.source_nested_tf_plan, args.output_plan), sort_keys=True))
