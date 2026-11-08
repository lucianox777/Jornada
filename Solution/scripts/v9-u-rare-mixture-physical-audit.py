#!/usr/bin/env python3
"""Independent audit of the frozen 24-arm U-rare physical TRAIN campaign.

Exactly 2 TF x 2 SQL frames x (V2 + V3 + four U mixtures).
No TEST/Gold, no independent FDR certificate, no causal conclusion.
Only aggregated differences are written; never export source identities.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import re

SCHEMA = "V9_U_RARE_MIXTURE_PHYSICAL_TRAIN_AUDIT_V1"
OPTIONS = ("A0.5_T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95")
V2 = "DEFAULT"
V3 = "CLASSWISE_SUPPORT_BACKOFF"
MIX = "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"
WEIGHTS = (0, .25, .5, 1)  # historical test fixture only
POLICY = {"ClassMassCoverage": .99, "MinimumM": 5, "MinimumU": 5,
          "SupportedTailRule": "RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2"}
HEX64 = re.compile(r"^[0-9a-f]{64}$")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require(test, reason):
    if not test:
        raise ValueError(reason)


def _safe_number(value):
    return type(value) in (float, int) and math.isfinite(value)


def _method_key(row, weights):
    method = row["MuEstimationMethod"]
    policy = row["MuPolicy"]
    if method == V2:
        require(policy is None, "V2 cannot have V3 U policy")
        return (V2, None)
    require(isinstance(policy, dict), "U policy missing")
    key = {k: policy.get(k) for k in POLICY}
    require(key == POLICY, "Historical U baseline drifted")
    if method == V3:
        require(policy.get("UBackoffJointMixtureWeight") is None,
                "Historical V3 must not silently take a mixture weight")
        return (V3, None)
    require(method == MIX, "Unknown M/U method")
    weight = policy.get("UBackoffJointMixtureWeight")
    require(type(weight) in (float, int) and weight in weights,
            "Undeclared U weight")
    return (MIX, weight)


def audit(plan_file, report_file, selection_file):
    plan = json.loads(Path(plan_file).read_bytes())
    report = json.loads(Path(report_file).read_bytes())
    envelope = json.loads(Path(selection_file).read_bytes())
    calibration = plan["Calibration"]
    margins = calibration["ConflictMargins"]
    require(len(margins) == 1 and _safe_number(margins[0]) and 0 < margins[0] <= 1,
            "Unexpected margin or merged hypotheses")
    margin = margins[0]
    policies = plan.get("SelectionDuplicatePolicies", ["COUNT_ALL"])
    require(isinstance(policies, list) and policies and len(set(policies)) == len(policies)
            and all(x in ("COUNT_ALL", "COUNT_UNIQUE") for x in policies),
            "Invalid DuplicateCandidatePolicies")
    methods = plan["MuEstimationMethods"]
    weights = methods[2]["Parameters"]["UBackoffJointMixtureWeights"]
    require(isinstance(weights, list) and len(weights) > 0 and
            all(_safe_number(w) and 0 <= w <= 1 for w in weights) and
            len(set(weights)) == len(weights), "Invalid declared mixture grid")
    expected_count = (2 + len(weights)) * len(OPTIONS) * 2
    require(plan["MaximumBatches"] == 1
            and plan["MaximumSearchConfigurations"] == expected_count
            and calibration["MaximumComparedConfigurations"] == expected_count
            and calibration["Budget"]["MaximumObservedAcceptedFdrPoint"] == .01
            and calibration["SelectionPolicy"]["MaximumAcceptedFdrUpper"] == .01
            and [x["OptionId"] for x in calibration["ModelOptions"]] == list(OPTIONS)
            and len(calibration["BaselineRuleSets"]) == 1,
            "Missing or modified frozen 24-arm plan")
    require([m["Method"] for m in methods] == [V2, V3, MIX]
            and methods[0].get("Parameters") is None
            and methods[1]["Parameters"] == {
                "ClassMassCoverage": [.99],
                "MinimumObservedMPerCell": [5],
                "MinimumObservedUPerCell": [5],
                "RetainSupportedTail": [True]
            }
            and methods[2]["Parameters"] == {
                **methods[1]["Parameters"],
                "UBackoffJointMixtureWeights": weights
            }, "Precommitted mixture grid changed")
    require(report["schema"] == "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            and report["scope"] == "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            and report["testUsed"] is False
            and report["activated"] is False
            and report["originalMethodsAndParameterArrays"] == methods
            and report["declaredVariantCount"] == 2 + len(weights)
            and report["evaluations"] == report["measured"] == expected_count
            and len(report["rows"]) == expected_count,
            "Missing physical evaluations or changed scientific scope")
    require(envelope["result"]["PlanSha256"] == report["PlanSha256"]
            and envelope["statisticalCertification"] is False
            and envelope["operationalActivation"] is False
            and envelope["result"]["TestUsed"] is False
            and envelope["result"]["Activated"] is False,
            "Selection evidence is not the same frozen TRAIN plan")
    require(len(envelope["result"]["Evaluations"]) == expected_count,
            "Selection did not retain all 24 physical evaluations")
    rows = {}
    frames = {}
    physical_reqs = set()
    for row in report["rows"]:
        require(row["Batch"] == 1
                and row["ConflictMargin"] == margin
                and len(row["blockingPasses"] or []) in (3, 19)
                and isinstance(row["OptionId"], str)
                and row["CandidatePairsSha256"] is not None
                and HEX64.fullmatch(row["CandidatePairsSha256"]) is not None
                and HEX64.fullmatch(row["fitSnapshotSha256"] or "") is not None
                and HEX64.fullmatch(row["RequestSha256"] or "") is not None
                and row["RequestSha256"] not in physical_reqs,
                "Invalid physical fit/frame/request binding")
        physical_reqs.add(row["RequestSha256"])
        option = next((x for x in OPTIONS if row["OptionId"].startswith(x + "__MU_")), None)
        require(option is not None, "Unknown TF arm")
        method, weight = _method_key(row, weights)
        suffix = ("DEFAULT" if method == V2 else
                  method + "_C0.99_M5_U5_T1" +
                  ("" if weight is None else "_UJW" + format(weight, "g")))
        require(row["OptionId"] == option + "__MU_" + suffix,
                "Estimator provenance or OptionId changed")
        passes = len(row["blockingPasses"])
        key = (passes, option, method, weight)
        require(key not in rows, "Duplicated estimator hypothesis")
        require(len(row["observedSupportAudit"]["Cells"]) == 81,
                "Incomplete observed 81-cell support")
        if method != V2:
            require(len(row["estimatorJointSupport"]["Cells"]) == 81,
                    "Incomplete fitted 81-cell M/U distribution")
        else:
            require(row["estimatorJointSupport"] is None, "V2 cannot store V3 support")
        tp, fp, eligible = (row["trueAccepted"], row["falseAccepted"], row["eligibleSources"])
        require(all(type(v) is int for v in (tp, fp, eligible))
                and eligible > 0 and 0 <= tp <= eligible and fp >= 0
                and _safe_number(row["recall"])
                and abs(row["recall"] - tp / eligible) < 1e-10
                and tp + fp > 0
                and _safe_number(row["fdrObserved"])
                and abs(row["fdrObserved"] - fp/(tp+fp)) < 1e-10
                and row["clusterIidIndependentlyValidated"] is False
                and row["outcome"].startswith("MEASURED_"),
                "Incorrect denominators/FDR, or artificial certification")
        prior = row["candidatePriorTrainingAudit"]
        require(isinstance(prior, dict) and
                prior["PhysicalCandidatePairsSha256"] == row["CandidatePairsSha256"],
                "Candidate pair prior not bound to SQL frame")
        frame = (row["CandidatePairsSha256"], row["fittedPairPrior"],
                 row["eligibleSources"], row["allSelectionObservations"])
        require(all(v is not None for v in frame), "Missing frame/prior")
        if passes in frames:
            require(frames[passes] == frame, "Hypotheses use different SQL frames")
        frames[passes] = frame
        require(tp + fp + row["conflicts"] + row["unresolved"] ==
                row["allSelectionObservations"], "Source decisions do not reconcile")
        rows[key] = row

    require(len(rows) == expected_count and set(frames) == {3, 19},
            "Not exactly 24 measured strata on two SQL frames")
    expected = {(passes, option, method, weight)
                for passes in (3, 19) for option in OPTIONS
                for method, weight in [(V2, None), (V3, None)] +
                [(MIX, x) for x in weights]}
    require(set(rows) == expected, "Missing TF x SQL x mixture cells")
    # The canonical historical 0.5-margin figures are regression sentinels.
    if margin == .5:
        require((rows[3, OPTIONS[0], V2, None]["trueAccepted"],
                 rows[3, OPTIONS[0], V3, None]["trueAccepted"],
                 rows[3, OPTIONS[1], V2, None]["trueAccepted"],
                 rows[3, OPTIONS[1], V3, None]["trueAccepted"])
                == (6758, 6306, 6702, 6771),
                "Historical V2/V3 reference changed under 0.5 margin")
    # Original and lambda=0 are two declared hypotheses (count both in
    # multiplicity), but exact scores and Resolve outcomes must agree.
    comparisons = []
    for passes in (3, 19):
        for option in OPTIONS:
            historical = rows[passes, option, V3, None]
            zero = rows.get((passes, option, MIX, 0))
            unchanged = ("trueAccepted", "falseAccepted", "eligibleSources",
                         "recall", "fdrObserved", "conflicts", "unresolved",
                         "conditionalClusterCpFdrUpper", "resolveReasonCounts",
                         "CandidatePairsSha256", "fittedPairPrior")
            require(zero is None or all(historical[k] == zero[k] for k in unchanged),
                    "lambda=0 failed historical V3 numerical parity")
            reference = rows[passes, option, V2, None]
            series = []
            for method, weight in [(V3, None)] + [(MIX, w) for w in weights]:
                row = rows[passes, option, method, weight]
                series.append({
                    "method": method, "lambda": weight,
                    "tp": row["trueAccepted"], "fp": row["falseAccepted"],
                    "recall": row["recall"], "fdrPoint": row["fdrObserved"],
                    "conditionalFdrUpperNotCertified": row["conditionalClusterCpFdrUpper"],
                    "conflicts": row["conflicts"], "unresolved": row["unresolved"],
                    "deltaTpVsV2": row["trueAccepted"] - reference["trueAccepted"],
                    "deltaTpVsV3": row["trueAccepted"] - historical["trueAccepted"],
                    "deltaFpVsV3": row["falseAccepted"] - historical["falseAccepted"],
                    "fanoutRejected": any("FANOUT" in r for r in row["Rejections"]),
                    "anyRejections": len(row["Rejections"]) > 0,
                })
            comparisons.append({
                "passes": passes, "tfArm": option,
                "sameSqlCandidatePairsSha256": frames[passes][0],
                "eligibleSources": frames[passes][2],
                "historicalV2": {"tp": reference["trueAccepted"],
                                 "fp": reference["falseAccepted"],
                                 "recall": reference["recall"]},
                "historicalV3": {"tp": historical["trueAccepted"],
                                 "fp": historical["falseAccepted"],
                                 "recall": historical["recall"]},
                "alternatives": series
            })
    actual_selections = report.get("duplicateCandidatePolicySelections")
    require(isinstance(actual_selections, list) and
            [x.get("Policy") for x in actual_selections] == policies and
            all(isinstance(x.get("FrontierSnapshotShas"), list) and
                isinstance(x.get("FinalistSnapshotShas"), list) and
                x.get("IndependentlyCertified") is False and
                isinstance(x.get("EligibleHypotheses"), int) and
                isinstance(x.get("PhysicalHypotheses"), int) and
                isinstance(x.get("DescriptiveMultiplicityHypotheses"), int)
                for x in actual_selections),
            "Calibrator did not execute both declared selection policies")
    # Fail closed when the numeric lambda=0 witness appears in any selected set.
    zero_shas = {x["fitSnapshotSha256"] for x in report["rows"]
                 if x["MuEstimationMethod"] == MIX and
                 x["MuPolicy"]["UBackoffJointMixtureWeight"] == 0}
    selection_count = len(rows) - (4 if 0 in weights else 0)
    for selected in actual_selections:
        require(not zero_shas.intersection(selected["FrontierSnapshotShas"])
                and not zero_shas.intersection(selected["FinalistSnapshotShas"]),
                "lambda=0 witness cannot be selected")
        require(selected["EligibleHypotheses"] == selection_count and
                selected["PhysicalHypotheses"] == len(rows) and
                selected["DescriptiveMultiplicityHypotheses"] ==
                (len(rows) if selected["Policy"] == "COUNT_ALL" else selection_count),
                "Calibrator selection/physical/multiplicity counts disagree")

    # Compare predeclared candidate accounting policies on the same physical evidence.
    # Only lambda=0 / historical V3 parity is proven; no outcome-based clustering.
    policy_comparisons = []
    for policy_name in policies:
        eligible = []
        groups = []
        for passes in (3, 19):
            for option in OPTIONS:
                same = [rows[passes, option, V3, None]]
                if (passes, option, MIX, 0) in rows:
                    same.append(rows[passes, option, MIX, 0])
                if len(same) == 2:
                    groups.append({"passes": passes, "tfArm": option,
                                   "requestSha256": [x["RequestSha256"] for x in same],
                                   "equivalence": "HISTORICAL_V3_LAMBDA_ZERO_PARITY"})
        for row in report["rows"]:
            if (row["MuEstimationMethod"] == MIX
                    and row["MuPolicy"]["UBackoffJointMixtureWeight"] == 0):
                continue
            eligible.append(row)
        # Descriptive ranking, NOT an independent certified FDR/multiplicity correction.
        ranked = sorted(eligible, key=lambda x: (-x["recall"], x["fdrObserved"], x["RequestSha256"]))
        policy_comparisons.append({
            "policy": policy_name, "physicalEvaluations": len(rows),
            "eligibleHypotheses": len(eligible),
            "physicalHypotheses": len(rows),
            "descriptiveMultiplicityHypotheses": (
                len(rows) if policy_name == "COUNT_ALL" else len(eligible)),
            "equivalentGroups": groups,
            "descriptiveTopRequestSha256": ranked[0]["RequestSha256"] if ranked else None,
            "descriptiveTopRecall": ranked[0]["recall"] if ranked else None,
            "descriptiveTopFdrObserved": ranked[0]["fdrObserved"] if ranked else None,
            "statisticalSelectionCertified": False,
            "multiplicityCorrectionApplied": False})
    # Do not ship any private per-source records in the aggregate report.
    text = json.dumps(report)
    require(not any(token in text for token in
                    ('"SourceRankInternal"', '"SourceObservationId"',
                     '"FirstObservationId"', '"SecondObservationId"')),
            "Raw per-source identifiers were exported")
    return {
        "schema": SCHEMA, "sha256": {
            "plan": digest(plan_file), "analysis": digest(report_file),
            "selectionEnvelope": digest(selection_file)},
        "margin": margin, "evaluations": len(rows), "physicalComparisons": expected_count,
        "multiplicityIncludesLambdaZeroTwice": 0 in weights,
        "historicalLambdaZeroParity": 0 in weights,
        "scope": "KNOWN_30K_SYNTHETIC_TRAIN_SELECTION_NOT_INDEPENDENT",
        "testUsed": False, "goldUsed": False, "certified": False, "activated": False,
        "uFallbackCausalityEstablished": False,
        "comparisons": comparisons,
        "duplicateCandidatePolicyComparisons": policy_comparisons,
        "calibratorPolicySelections": actual_selections,
        "conclusion": "DESCRIPTIVE_ONLY_NO_PARAM_SELECTION_OR_OPERATIONAL_AUTHORIZATION"
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("plan", "analysis", "selection", "out"):
        parser.add_argument("--" + key, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.plan, args.analysis, args.selection)
    with args.out.open("x", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False, allow_nan=False)
        f.write("\n")
    print(json.dumps({"status": "TRAIN_ONLY_VERIFIED", "evaluations": result["evaluations"],
                      "margin": result["margin"], "certified": False},
                     sort_keys=True))
