#!/usr/bin/env python3
"""Fail-closed, physical, synthetic TRAIN-only V9-J acceptance audit.

Runs on the *official* role-tagged plan.json, never selects a production model.
Requires 24 measured initial physical hypotheses, 16 diagnostics (including
4 anchored V2, 4 lambda-zero, eight 19-pass CANDIDATE diagnostics), 8
eligible-for-selection by declaration, and V2 numerical parity with the
previous green master U-rare study in the exact same SQL frame.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

OPTIONS = ("A0.5_T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95")
METHODS = ("DEFAULT", "CLASSWISE_SUPPORT_BACKOFF",
           "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE")
WEIGHTS = (0, .25, .5, 1)
ROLES = {"PRIMARY": .5, "PREDECLARED_SENSITIVITY": .25}
EXPECTED_PHYSICAL = 24
EXPECTED_SELECTABLE = 8
FIELDS = ("CandidatePairsSha256", "fittedPairPrior", "eligibleSources",
          "trueAccepted", "falseAccepted", "recall", "conflicts", "unresolved")


def require(ok: bool, why: str) -> None:
    if not ok:
        raise ValueError("V9_J_PHYSICAL_FAIL_CLOSED: " + why)


def read(path: Path) -> dict:
    def no_duplicates(pairs):
        result = {}
        for k, v in pairs:
            require(k not in result, "duplicate JSON property: " + k)
            result[k] = v
        return result
    result = json.loads(path.read_bytes(), object_pairs_hook=no_duplicates)
    require(isinstance(result, dict), "invalid JSON root")
    return result


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def is_sha(x: object) -> bool:
    return isinstance(x, str) and len(x) == 64 and all(c in "0123456789abcdefABCDEF" for c in x)


def classify(row: dict) -> tuple:
    name = row.get("OptionId", "")
    require(isinstance(name, str), "missing OptionId")
    option = next((o for o in OPTIONS if name.startswith(o + "__MU_")), None)
    require(option is not None, "wrong historical TF arm")
    passes = row.get("blockingPasses")
    require(isinstance(passes, list) and len(passes) in (3, 19), "unexpected SQL blocking frame")
    method = row.get("MuEstimationMethod")
    require(method in METHODS, "unknown M/U method")
    policy = row.get("MuPolicy")
    weight = None
    if method == METHODS[0]:
        require(policy is None, "V2 must not carry M/U policy")
        require(name == option + "__MU_DEFAULT", "V2 option id drift")
    else:
        require(isinstance(policy, dict), "missing observed M/U policy")
        require(policy.get("ClassMassCoverage") == .99 and
                policy.get("MinimumM") == 5 and policy.get("MinimumU") == 5,
                "V3 policy drift")
        if method == METHODS[2]:
            weight = policy.get("UBackoffJointMixtureWeight")
            require(type(weight) in (float, int) and weight in WEIGHTS,
                    "undeclared lambda")
        else:
            require(policy.get("UBackoffJointMixtureWeight") is None,
                    "V3 base unexpectedly mixed")
    require(is_sha(row.get("CandidatePairsSha256")) and
            is_sha(row.get("RequestSha256")) and
            is_sha(row.get("fitSnapshotSha256")), "unbound frame/request/fit")
    return len(passes), option, method, weight


def audit(official_plan: Path, official_report: Path,
          official_receipt: Path, baseline_report: Path, role: str) -> dict:
    require(role in ROLES, "role undeclared")
    plan = read(official_plan)
    report = read(official_report)
    receipt = read(official_receipt)
    require(receipt.get("schema") == "V9_OFFICIAL_MULTIPARAM_V2_WITNESS_ANCHOR_V1"
            and receipt.get("marginRole") == role
            and receipt.get("physicallyDeclared") == EXPECTED_PHYSICAL
            and receipt.get("initialWitnessHypotheses") == 16
            and receipt.get("predeclaredCandidateCpMultiplicity") == EXPECTED_SELECTABLE
            and receipt.get("V2role") == "WITNESS"
            and receipt.get("V2anchor") is True
            and receipt.get("officialPlanSha256") == sha(official_plan),
            "official role-tagged plan not proven")
    m = plan["MuEstimationMethods"]
    require([x["Method"] for x in m] == list(METHODS) and
            m[0].get("Role") == "WITNESS" and m[0].get("Anchor") is True
            and all(x.get("Role") == "CANDIDATE" for x in m[1:]),
            "V2 WITNESS/ANCHOR or candidate roles drift")
    require(plan["PredeclaredCandidateCpMultiplicity"] == EXPECTED_SELECTABLE
            and plan["MaximumSearchConfigurations"] == EXPECTED_PHYSICAL
            and plan["Calibration"]["MaximumComparedConfigurations"] == EXPECTED_PHYSICAL
            and plan["Calibration"]["ConflictMargins"] == [ROLES[role]]
            and plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"] == .01
            and plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"] == .01,
            "candidate multiplicity, risk ceiling, or role margin drift")
    require(report.get("schema") == "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            and report.get("scope") == "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            and report.get("testUsed") is False and report.get("activated") is False
            and is_sha(report.get("PlanSha256")),
            "wrong analysis/plan digest or TRAIN scope")
    rows = report.get("rows")
    require(isinstance(rows, list) and len(rows) == EXPECTED_PHYSICAL
            and report.get("evaluations") == EXPECTED_PHYSICAL
            and report.get("measured") == EXPECTED_PHYSICAL,
            "not all 24 physical fits completed")
    actual = {}
    requests = set()
    frames = {}
    selectable = 0
    diagnostics = 0
    for row in rows:
        key = classify(row)
        passes, option, method, weight = key
        require(key not in actual, "duplicated physical fit")
        require(row["RequestSha256"] not in requests, "duplicated request SHA")
        requests.add(row["RequestSha256"])
        actual[key] = row
        require(row.get("Batch") == 1 and row.get("ConflictMargin") == ROLES[role],
                "physical fit in unexpected batch/margin")
        frame = (row["CandidatePairsSha256"], row["fittedPairPrior"],
                 row["eligibleSources"], row["allSelectionObservations"])
        require(all(x is not None for x in frame), "incomplete SQL candidate frame")
        if passes in frames:
            require(frame == frames[passes], "not the same SQL candidate/prior frame")
        frames[passes] = frame
        diagnostic = passes == 19 or method == METHODS[0] or (method == METHODS[2] and weight == 0)
        require(row.get("EligibleForSelection") is (not diagnostic),
                "19-pass / V2 / lambda0 eligibility leak")
        require(row.get("outcome", "").startswith("MEASURED_"), "missing physical measurement")
        require(row["allSelectionObservations"] ==
                row["trueAccepted"] + row["falseAccepted"] + row["conflicts"] + row["unresolved"],
                "source decision totals drift")
        if diagnostic:
            diagnostics += 1
        else:
            selectable += 1
    expected = {(passes, option, method, weight)
                for passes in (3, 19) for option in OPTIONS
                for method, weight in [(METHODS[0], None), (METHODS[1], None)] +
                [(METHODS[2], value) for value in WEIGHTS]}
    require(set(actual) == expected and diagnostics == 16 and selectable == 8,
            "incomplete 24/16/8 official partition")
    require(report.get("predeclaredCandidateCpMultiplicity") == EXPECTED_SELECTABLE,
            "report did not retain CP8 budget")
    for passes in (3, 19):
        for option in OPTIONS:
            zero = actual[passes, option, METHODS[2], 0]
            historic = actual[passes, option, METHODS[1], None]
            for field in FIELDS:
                require(zero[field] == historic[field],
                        "lambda-zero/V3 parity failure: " + field)
    before = read(baseline_report)
    require(before.get("schema") == report["schema"]
            and before.get("testUsed") is False
            and before.get("activated") is False,
            "previous master TRAIN proof missing")
    historical = {classify(row): row for row in before["rows"]}
    comparisons = []
    for passes in (3, 19):
        for option in OPTIONS:
            key = (passes, option, METHODS[0], None)
            require(key in historical, "previous master V2 observation absent")
            old = historical[key]
            new = actual[key]
            require(all(old[f] == new[f] for f in FIELDS),
                    "historical V2 numeric/SQL frame mismatch: " + option + "/" + str(passes))
            require(new.get("EligibleForSelection") is False,
                    "V2 anchor selectable")
            comparisons.append({"option": option, "blockingPassCount": passes,
                                "historicalRequestSha256": old["RequestSha256"],
                                "currentRequestSha256": new["RequestSha256"],
                                "candidateFrameSha256": new["CandidatePairsSha256"],
                                "tp": new["trueAccepted"], "fp": new["falseAccepted"],
                                "status": "SAME_PHYSICAL_FRAME_SAME_V2_NUMERICS"})
    old_source = before.get("sourceObservationsSha256")
    new_source = report.get("sourceObservationsSha256")
    if old_source is not None:
        require(old_source == new_source, "different frozen source CSV")
    selections = report.get("duplicateCandidatePolicySelections") or []
    require(len(selections) == 2 and
            [s.get("Policy") for s in selections] == ["COUNT_ALL", "COUNT_UNIQUE"]
            and all(s.get("PhysicalHypotheses") == EXPECTED_PHYSICAL
                    and isinstance(s.get("EligibleHypotheses"), int)
                    and s["EligibleHypotheses"] <= EXPECTED_SELECTABLE
                    and s.get("IndependentlyCertified") is False
                    for s in selections),
            "incorrect duplicate policy/CP accounting")
    return {"schema": "V9_J_OFFICIAL_PHYSICAL_TECHNICAL_AUDIT_V1",
            "headPlanSha256": sha(official_plan), "analysisSha256": sha(official_report),
            "baselineAnalysisSha256": sha(baseline_report),
            "receiptSha256": sha(official_receipt),
            "role": role, "conflictMargin": ROLES[role],
            "evaluations": EXPECTED_PHYSICAL, "diagnosticWitnesses": diagnostics,
            "predeclaredCpCandidates": EXPECTED_SELECTABLE,
            "sameSqlFrameForAllMethods": True,
            "historicalV2PhysicalParity": comparisons,
            "lambdaZeroV3PhysicalParity": True,
            "certified": False, "testUsed": False, "goldUsed": False,
            "activated": False,
            "scope": "FROZEN_SEED42_SYNTHETIC_TRAIN_REGRESSION_NOT_SCIENTIFIC_CAMPAIGN"}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for flag in ("plan", "analysis", "receipt", "baseline-analysis", "role", "out"):
        parser.add_argument("--" + flag, required=True)
    args = parser.parse_args()
    result = audit(Path(args.plan), Path(args.analysis), Path(args.receipt),
                   Path(args.baseline_analysis), args.role)
    with Path(args.out).open("x", encoding="utf8") as handle:
        json.dump(result, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print(json.dumps({"status": "OFFICIAL_J_FROZEN_TRAIN_PHYSICAL_PASS",
                      "role": args.role, "measured": 24, "cpCandidates": 8,
                      "v2Parity": 4, "certified": False}))


if __name__ == "__main__":
    main()
