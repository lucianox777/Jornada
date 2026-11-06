#!/usr/bin/env python3
"""Recompute V2 vs V3 nominal log-likelihood shifts from 81 TRAIN counts.

Pure diagnostic on the frozen PHYSICAL SQL TRAIN_SELECTION report.
No oracle is involved in candidate selection. This is not per-candidate replay,
FDR certification, independent holdout, nor an operational model change.
"""
import argparse
from collections import Counter
from decimal import Decimal, getcontext
import json
import math
from pathlib import Path

getcontext().prec = 45
METHODS = ("JOINT_DIRICHLET", "JOINT_DIRICHLET_SUPPORTED_TAIL",
           "CONDITIONAL_PRODUCT_MARGINALS")
OPTION = "A0.5_T0.95_TF0"


def dec(value):
    return Decimal(str(value))


def compare_rows(default, v3):
    a = default["observedSupportAudit"]["Cells"]
    support = v3["estimatorJointSupport"]
    if not isinstance(support, dict):
        raise ValueError("V3 joint support provenance absent")
    b = support["Cells"]
    if (len(a) != 81 or len(b) != 81
            or {(c["Person"], c["Mother"]) for c in a} !=
               {(p, q) for p in range(9) for q in range(9)}
            or support["TotalM"] <= 1 or support["TotalU"] <= 1
            or dec(v3["smoothingAlpha"]) != dec(default["smoothingAlpha"])
            or support["Policy"] != v3["MuPolicy"]):
        raise ValueError("Different or invalid fixed joint support")
    left = {(c["Person"], c["Mother"]):
            (c["ObservedMPairs"], c["ObservedUPairs"]) for c in a}
    right = {(c["Person"], c["Mother"]):
             (c["ObservedM"], c["ObservedU"]) for c in b}
    if left != right or any(x < 0 or y < 0 for x, y in right.values()):
        raise ValueError("Not the same 81 training cells for both estimators")
    M, U = support["TotalM"], support["TotalU"]
    if sum(v[0] for v in right.values()) != M or sum(v[1] for v in right.values()) != U:
        raise ValueError("Wrong physical M/U totals")
    alpha = dec(v3["smoothingAlpha"])
    if alpha <= 0:
        raise ValueError("Smoothing alpha must be positive")
    den_m, den_u = dec(M)+81*alpha, dec(U)+81*alpha
    mrow, mcol, urow, ucol = ([0]*9 for _ in range(4))
    for (i, j), (m, u) in right.items():
        mrow[i] += m
        mcol[j] += m
        urow[i] += u
        ucol[j] += u
    shifts = []
    masses_m, masses_u = [], []
    for cell in sorted(b, key=lambda x:(x["Person"],x["Mother"])):
        i, j = cell["Person"], cell["Mother"]
        m, u = right[(i,j)]
        m_v2=(dec(m)+alpha)/den_m
        u_v2=(dec(u)+alpha)/den_u
        method_m,method_u = cell["MethodM"],cell["MethodU"]
        if method_m not in METHODS or method_u not in METHODS:
            raise ValueError("Unknown M/U class support mode")
        if method_m == "CONDITIONAL_PRODUCT_MARGINALS":
            m_v3=(dec(support["FallbackScaleM"])
                  *((dec(mrow[i])+9*alpha)/den_m)
                  *((dec(mcol[j])+9*alpha)/den_m))
        else:
            m_v3=m_v2
        if method_u == "CONDITIONAL_PRODUCT_MARGINALS":
            u_v3=(dec(support["FallbackScaleU"])
                  *((dec(urow[i])+9*alpha)/den_u)
                  *((dec(ucol[j])+9*alpha)/den_u))
        else:
            u_v3=u_v2
        if min(m_v3,u_v3)<=0:
            raise ValueError("Invalid zero probability in reconstructed V3")
        masses_m.append(m_v3)
        masses_u.append(u_v3)
        shift=math.log(float((m_v3/u_v3)/(m_v2/u_v2)))
        shifts.append({
            "nominalState":[i,j],"observedM":m,"observedU":u,
            "methodM":method_m,"methodU":method_u,
            "v2LogLikelihood":round(math.log(float(m_v2/u_v2)),8),
            "v3LogLikelihood":round(math.log(float(m_v3/u_v3)),8),
            "v3MinusV2LogOdds":round(shift,8),
            "factorV3OverV2M":round(float(m_v3/m_v2),8),
            "factorV3OverV2U":round(float(u_v3/u_v2),8),
        })
    if abs(sum(masses_m)-Decimal(1)) > Decimal("1e-10") or abs(sum(masses_u)-Decimal(1)) > Decimal("1e-10"):
        raise ValueError("V3 probabilities do not sum to one")
    return {
        "totalM":M,"totalU":U,
        "fallbackScaleM":support["FallbackScaleM"],
        "fallbackScaleU":support["FallbackScaleU"],
        "mMethodCounts":dict(Counter(x["methodM"] for x in shifts)),
        "uMethodCounts":dict(Counter(x["methodU"] for x in shifts)),
        "changedLikelihoodCells":sum(abs(x["v3MinusV2LogOdds"])>1e-6 for x in shifts),
        "largestShiftsWithObservedSupport":sorted(
            (x for x in shifts if x["observedM"]+x["observedU"]>0),
            key=lambda x:abs(x["v3MinusV2LogOdds"]),reverse=True)[:20],
        "cells":shifts,
    }


def audit(report):
    if (report.get("schema")!="V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            or report.get("scope")!="TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            or report.get("testUsed") is not False or report.get("activated") is not False):
        raise ValueError("Only pinned technical TRAIN_SELECTION reports are allowed")
    r=report["rows"]
    frames=[]
    for passes in (3,19):
        def unique(method):
            arr=[x for x in r if x["OptionId"].startswith(OPTION+"__MU_")
                 and x["MuEstimationMethod"]==method
                 and len(x["blockingPasses"])==passes
                 and x["ConflictMargin"]==.5]
            if len(arr)!=1:raise ValueError("Expected exactly one TF0 per M/U/blocking")
            return arr[0]
        v2,v3=unique("DEFAULT"),unique("CLASSWISE_SUPPORT_BACKOFF")
        if (v2["CandidatePairsSha256"]!=v3["CandidatePairsSha256"]
                or v2["fittedPairPrior"]!=v3["fittedPairPrior"]
                or v2["eligibleSources"]!=v3["eligibleSources"]):
            raise ValueError("The physical candidate SQL frames are not identical")
        calculated=compare_rows(v2,v3)
        frames.append({"blockingPasses":passes,
                       "candidatePairsSha256":v2["CandidatePairsSha256"],
                       "v2Tp":v2["trueAccepted"],"v3Tp":v3["trueAccepted"],
                       "v2Conflicts":v2["conflicts"],"v3Conflicts":v3["conflicts"],
                       "comparison":calculated})
    return {"schema":"V9_TF0_81_CELL_V2_V3_ABLATION_V1",
            "scope":"KNOWN_TRAIN_SEED42_DIAGNOSTIC_NOT_CAUSAL_REPLAY",
            "frames":frames,"perCandidateAttributionEstablished":False,
            "iidValidated":False,"fdrCertified":False,
            "testUsed":False,"activated":False}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--analysis",type=Path,required=True)
    p.add_argument("--out",type=Path,required=True)
    a=p.parse_args()
    result=audit(json.loads(a.analysis.read_bytes()))
    with a.out.open("x",encoding="utf8") as f:
        json.dump(result,f,indent=2,ensure_ascii=False,allow_nan=False)
        f.write("\n")
    print(json.dumps({"frames":[{
        "blockingPasses":f["blockingPasses"],
        "changedLikelihoodCells":f["comparison"]["changedLikelihoodCells"],
        "v2Tp":f["v2Tp"],"v3Tp":f["v3Tp"]
    } for f in result["frames"]], "certified":False},sort_keys=True))


if __name__=="__main__":
    main()
