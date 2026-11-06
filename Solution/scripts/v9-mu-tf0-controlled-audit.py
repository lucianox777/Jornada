#!/usr/bin/env python3
"""Audit V2xV3 crossed with TF0xONE_MF on identical physical SQL frame.

Only TRAIN_SELECTION; grouped identity differences are descriptive, not
independent confidence intervals, not an IID/FDR certification.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

METHODS = ("DEFAULT", "CLASSWISE_SUPPORT_BACKOFF")
OPTIONS = ("A0.5_T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95")
POLICY = {"ClassMassCoverage": .99, "MinimumM": 5, "MinimumU": 5,
          "SupportedTailRule": "RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2"}
VERSION = "V9_MU_TF0_V2_V3_SAME_SQL_FRAME_AUDIT_V1"


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def audit(plan_path, report_path, selection_path):
    plan = json.loads(Path(plan_path).read_bytes())
    report = json.loads(Path(report_path).read_bytes())
    envelope = json.loads(Path(selection_path).read_bytes())
    calibration = plan["Calibration"]
    if (report["schema"] != "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            or report["scope"] != "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            or report["testUsed"] is not False or report["activated"] is not False
            or not (plan["MaximumSearchConfigurations"] == calibration["MaximumComparedConfigurations"] == 8)
            or plan["MaximumBatches"] != 1
            or [o["OptionId"] for o in calibration["ModelOptions"]] != list(OPTIONS)
            or calibration["ConflictMargins"] != [.5]
            or calibration["Budget"]["MaximumObservedAcceptedFdrPoint"] != .01
            or calibration["SelectionPolicy"]["MaximumAcceptedFdrUpper"] != .01
            or [x["Method"] for x in plan["MuEstimationMethods"]] != list(METHODS)
            or report["originalMethodsAndParameterArrays"] != plan["MuEstimationMethods"]
            or report["evaluations"] != 8 or report["measured"] != 8
            or len(report["rows"]) != 8):
        raise ValueError("Missing or changed fixed 8-arm TRAIN experiment")
    rows = {}
    by_key = {}
    frames = {}
    for row in report["rows"]:
        name = next((name for name in OPTIONS if row["OptionId"].startswith(name+"__MU_")), None)
        method = row["MuEstimationMethod"]
        passes = len(row["blockingPasses"]) if row["blockingPasses"] else 0
        key = (passes, method, name)
        if (name is None or method not in METHODS or passes not in (3, 19)
                or key in rows or row["ConflictMargin"] != .5
                or row["OptionId"] != name + "__MU_" +
                   ("DEFAULT" if method == "DEFAULT" else "CLASSWISE_SUPPORT_BACKOFF_C0.99_M5_U5_T1")
                or not row["fitSnapshotSha256"]
                or len(row["fitSnapshotSha256"]) != 64
                or row["MuPolicy"] != (POLICY if method == "CLASSWISE_SUPPORT_BACKOFF" else None)
                or (row["estimatorJointSupport"] is None) != (method == "DEFAULT")
                or len(row["observedSupportAudit"]["Cells"]) != 81
                or (method == "CLASSWISE_SUPPORT_BACKOFF"
                    and len(row["estimatorJointSupport"]["Cells"]) != 81)
                or row["candidatePriorTrainingAudit"] is None
                or row["fittedPairPrior"] is None
                or row["clusterIidIndependentlyValidated"] is not False):
            raise ValueError("Unpaired, missing or changed V2/V3 TF0/ONE_MF physical rows")
        tp, fp, eligible = row["trueAccepted"], row["falseAccepted"], row["eligibleSources"]
        if (not all(type(x) is int for x in (tp, fp, eligible)) or eligible < 1
                or not 0 <= tp <= eligible or fp < 0
                or abs(row["recall"]-tp/eligible) > 1e-11
                or row["fdrObserved"] is None or tp+fp == 0
                or abs(row["fdrObserved"]-fp/(tp+fp)) > 1e-11):
            raise ValueError("Incorrect FDR or recall denominator")
        prior = row["candidatePriorTrainingAudit"]
        m, u = prior["TrainTrueObservationPairs"], prior["TrainFalseObservationPairs"]
        if (m < 1 or u < 1 or m+u != prior["TrainCandidateObservationPairs"]
                or row["CandidatePairsSha256"] != prior["PhysicalCandidatePairsSha256"]
                or abs(row["fittedPairPrior"]-(m+.5)/(m+u+1)) > 1e-11):
            raise ValueError("Inconsistent physical SQL candidate prior")
        value = (row["CandidatePairsSha256"], m, u, eligible, row["fittedPairPrior"])
        if passes in frames and frames[passes] != value:
            raise ValueError("V2 and V3 or TF0 and ONE_MF have DIFFERENT physical SQL frame or prior")
        frames[passes] = value
        rows[key] = row
        request = row["RequestSha256"]
        if not isinstance(request, str) or len(request) != 64 or request in by_key:
            raise ValueError("Missing or duplicate physical RequestSha256")
        by_key[request] = key
    if len(rows) != 8 or {k[0] for k in rows} != {3, 19}:
        raise ValueError("Not all four arms measured on both SQL rulesets")
    # The original already-measured ONE_MF rows are fixed regression sentinels,
    # not evidence that V3 beats V2 on a new cohort.
    if (rows[3,"DEFAULT",OPTIONS[1]]["trueAccepted"] != 6702
            or rows[3,"CLASSWISE_SUPPORT_BACKOFF",OPTIONS[1]]["trueAccepted"] != 6771
            or rows[3,"CLASSWISE_SUPPORT_BACKOFF",OPTIONS[0]]["trueAccepted"] != 6306
            or any(rows[3,m,n]["eligibleSources"] != 7011 for m in METHODS for n in OPTIONS)):
        raise ValueError("Known seed42 V3/ONE_MF/TF0 physical regression changed; investigate")
    # Read the actual internal Calibrador envelope: each option contains
    # GROUPED decisions for the exact same base identities. Never expose IDs.
    selections = envelope["result"]["Evaluations"]
    if len(selections) != 8:
        raise ValueError("Selection envelope is incomplete")
    grouped = {}
    for evaluation in selections:
        # OptionId repeats across rule sets. Only the unique physical request
        # digest identifies an evaluation; matching by OptionId silently swaps
        # blocking frames and makes the per-identity audit meaningless.
        k = by_key.get(evaluation["RequestSha256"])
        if k is None or k in grouped or evaluation["OptionId"] != rows[k]["OptionId"]:
            raise ValueError("Selection envelope contains unknown/duplicate option")
        identities = evaluation["Candidate"]["Metric"]["IdentityOutcomes"]
        if not isinstance(identities, list) or len(identities) < 2:
            raise ValueError("Identity-level outcome evidence missing")
        by_id = {x["BaseIdentity"]: x for x in identities}
        if (len(by_id) != len(identities)
                or sum(x["TrueAccepted"] for x in identities) != rows[k]["trueAccepted"]
                or sum(x["FalseAccepted"] for x in identities) != rows[k]["falseAccepted"]):
            raise ValueError("Identity-level results differ from source metrics")
        grouped[k] = by_id
    paired = []
    for passes in (3, 19):
        for option in OPTIONS:
            left=rows[passes,"DEFAULT",option]
            right=rows[passes,"CLASSWISE_SUPPORT_BACKOFF",option]
            a,b=grouped[passes,"DEFAULT",option],grouped[passes,"CLASSWISE_SUPPORT_BACKOFF",option]
            if set(a) != set(b):
                raise ValueError("Different base-identities evaluated")
            differences=[(b[k]["TrueAccepted"]-a[k]["TrueAccepted"],
                          b[k]["FalseAccepted"]-a[k]["FalseAccepted"]) for k in a]
            positives=sum(1 for d,_ in differences if d>0)
            negatives=sum(1 for d,_ in differences if d<0)
            if sum(x for x,_ in differences)!=right["trueAccepted"]-left["trueAccepted"]:
                raise ValueError("Nonreconciled per-base TP difference")
            paired.append({
                "blockingPasses":passes,"optionId":option,
                "v2TrueAccepted":left["trueAccepted"],"v3TrueAccepted":right["trueAccepted"],
                "v2FalseAccepted":left["falseAccepted"],"v3FalseAccepted":right["falseAccepted"],
                "v2Recall":left["recall"],"v3Recall":right["recall"],
                "deltaTrueAccepted":right["trueAccepted"]-left["trueAccepted"],
                "deltaFalseAccepted":right["falseAccepted"]-left["falseAccepted"],
                "deltaRecallPercentagePoints":100*(right["recall"]-left["recall"]),
                "baseIdentities":len(a),"identitiesImproved":positives,
                "identitiesWorsened":negatives,
                "identitiesUnchanged":len(a)-positives-negatives,
                "identitiesWithChangedFalseAcceptances":sum(1 for _,f in differences if f),
                "v2Rejections":left["Rejections"],"v3Rejections":right["Rejections"],
                "sourceFdrConditionalNotCertified":right["conditionalClusterCpFdrUpper"]
            })
    return {
        "schema":VERSION,
        "scope":"KNOWN_SEED42_TRAIN_SELECTION_TECHNICAL_NOT_INDEPENDENT",
        "planSha256":sha(plan_path),"analysisSha256":sha(report_path),
        "selectionEnvelopeSha256":sha(selection_path),
        "sourceSqlFrames":[{"passes":n,"candidatePairsSha256":v[0],
                            "trainM":v[1],"trainU":v[2],"eligible":v[3],
                            "fittedPrior":v[4]} for n,v in sorted(frames.items())],
        "physicalConfigurations":8,"identityPairedComparisons":paired,
        "certified":False,"statisticalIidValidated":False,"testUsed":False,
        "activated":False,
        "interpretation":"SAME_SQL_TRAIN_FRAME_ONLY_NO_EXTERNAL_CERTIFICATION"
    }


if __name__ == "__main__":
    a=argparse.ArgumentParser(description=__doc__)
    a.add_argument("--plan",type=Path,required=True)
    a.add_argument("--analysis",type=Path,required=True)
    a.add_argument("--selection",type=Path,required=True)
    a.add_argument("--out",type=Path,required=True)
    v=a.parse_args()
    result=audit(v.plan,v.analysis,v.selection)
    with v.out.open("x",encoding="utf-8") as f:
        json.dump(result,f,indent=2,ensure_ascii=False,allow_nan=False)
        f.write("\n")
    print(json.dumps({"schema":VERSION,"comparisons":result["identityPairedComparisons"],
                      "certified":False},sort_keys=True))
