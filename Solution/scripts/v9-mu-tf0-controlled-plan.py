#!/usr/bin/env python3
"""Known-seed-42 physical control: V2 vs V3 x TF0 vs ONE_MF on the SAME SQL frame.

Reuses the validated historical V2/V3 plan builder. Only adds a canonical TF0
arm; no optimizer, no new holdout, no REF/Gold/TEST, no operational activation.
"""
import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile

ROOT = Path(__file__).resolve().parent
VERSION = "V9_MU_TF0_V2_V3_ONE_FRAME_TECHNICAL_PLAN_V1"
TF0 = "A0.5_T0.95_TF0"
ONE_MF = "TF_ONE_MF_W1_M0_A0.5_T0.95"
LEGACY_TF0_ALIAS = "T0.95_TF0"


def _load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mu = _load_module(ROOT / "v9-mu-historical-plan.py", "historical_mu_control")


def freeze(source_path, target_path):
    source_path, target_path = Path(source_path), Path(target_path)
    if target_path.exists():
        raise FileExistsError(target_path)
    original = source_path.read_bytes()
    source = mu._read_json(source_path)
    canonical_tf0 = mu._helper().option_grid(alpha=.5, threshold=.95)[0]
    assert canonical_tf0["OptionId"] == TF0
    assert canonical_tf0 == {
        "OptionId": TF0, "SmoothingAlpha": .5,
        "DecisionThreshold": .95, "TfWeight": 0
    }, "TF0 has acquired a TF unit or an undeclared parameter"
    options = source.get("Calibration", {}).get("ModelOptions", [])
    matches = [x for x in options if x.get("OptionId") == TF0]
    if len(matches) != 1 or matches[0] != canonical_tf0:
        raise ValueError("TF0 ID is not its exact canonical alpha=.5/threshold=.95/weight=0")
    one_mf = [x for x in options if x.get("OptionId") == ONE_MF]
    if len(one_mf) != 1 or one_mf[0] != next(
        x for x in mu._helper().option_grid(.5, .95) if x["OptionId"] == ONE_MF):
        raise ValueError("ONE_MF is not the frozen canonical active TF arm")
    # No copy-pasted historical plan semantics: take the fully checked builder
    # and modify only ModelOptions/search cardinality and study fingerprint.
    with tempfile.TemporaryDirectory(prefix="v9-tf0-v2-v3-") as d:
        tmp = Path(d) / "historical-plan.json"
        mu.freeze(source_path, tmp)
        plan = mu._read_json(tmp)
    plan["Calibration"]["ModelOptions"] = [copy.deepcopy(matches[0]), copy.deepcopy(one_mf[0])]
    plan["MaximumSearchConfigurations"] = 8
    plan["Calibration"]["MaximumComparedConfigurations"] = 8
    assert len(plan["MuEstimationMethods"]) == 2
    assert plan["Calibration"]["ConflictMargins"] == [.5]
    assert len(plan["Calibration"]["BaselineRuleSets"]) == 1
    assert plan["MaximumBatches"] == 1
    assert plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"] == .01
    assert plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"] == .01
    basis = (VERSION + "|" + hashlib.sha256(original).hexdigest()
             + "|" + json.dumps(plan["MuEstimationMethods"], sort_keys=True,
                                separators=(",", ":")) + "|TF0+ONE_MF|0.5|3+19")
    study = hashlib.sha256(basis.encode()).hexdigest()
    plan["Calibration"]["StudyFingerprintSha256"] = study
    plan["Calibration"]["Budget"]["StudyFingerprintSha256"] = study
    raw = (json.dumps(plan, indent=2, allow_nan=False) + "\n").encode()
    # Fail before touching the destination whenever provenance or option
    # semantics differ. No deletion/overwrite of existing evidence.
    with target_path.open("xb") as stream:
        stream.write(raw)
    return {
        "schema": VERSION,
        "sourcePlanSha256": hashlib.sha256(original).hexdigest(),
        "outputPlanSha256": hashlib.sha256(raw).hexdigest(),
        "studyFingerprintSha256": study,
        "canonicalTf0OptionId": TF0,
        "historicalTf0LabelOnlyNotASeparateOption": LEGACY_TF0_ALIAS,
        "comparisons": 8,
        "methodCount": 2,
        "tfArmCount": 2,
        "ruleSetCount": 2,
        "scope": "KNOWN_SEED42_TRAIN_SELECTION_ONLY_NOT_HOLDOUT",
        "testUsed": False, "certified": False, "activated": False,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_nested_tf_plan", type=Path)
    parser.add_argument("new_mu_tf0_plan", type=Path)
    a = parser.parse_args()
    print(json.dumps(freeze(a.source_nested_tf_plan, a.new_mu_tf0_plan),
                     sort_keys=True))
