#!/usr/bin/env python3
"""Independent TRAIN/VALIDATION diagnostics for two SQL prefix3 passes."""
import json
from pathlib import Path

SEEDS=(42,43,44)
PARTS=("TRAIN","VALIDATION")
PASSES=("MOTHER_PREFIX3_PLUS_EXACT_BIRTH","PERSON_PREFIX3_PLUS_MOTHER_PREFIX3")

def require(ok):
    if not ok: raise ValueError("invalid physical SQL evidence")

def fdr(m,u):
    return .01*u/(.90*m+.01*u) if m+u else None

def evaluate(data,seed):
    require(data["Seed"]==seed and data["CorpusPeople"]==30000)
    report=data["OtherPrefix3PairsTrainValidation"]
    require(report["Version"]=="V9_SQL_TWO_MORE_PREFIX3_FAMILIES_TRAIN_VALIDATION_V1")
    require(report["Status"]=="DIAGNOSTIC_ONLY_TRAIN_VALIDATION_NO_TEST"
            and report["MayPromotePolicy"] is False)
    splits=report["Splits"]
    passes=report["Passes"]
    require(len(splits)==2 and {x["Partition"] for x in splits}==set(PARTS))
    require(len(passes)==4 and {(x["Partition"],x["Name"]) for x in passes}
            =={(p,n) for p in PARTS for n in PASSES})
    result={}
    for s in splits:
        p=s["Partition"]; t=s["EligibleM"]; n=s["EligibleObservations"]
        m=s["FourteenM"]; u=s["FourteenU"]
        require(t>0 and n>0 and 0<=m<=t and u>=0)
        require((m-s["TwelveM"],u-s["TwelveU"])==(s["AddedM"],s["AddedU"]))
        require(m+u<=data["CandidatePairHardCap"])
        require(abs(s["Recall"]-m/t)<1e-12)
        require(s["MoreMNeededFor95Percent"]==max(0,(19*t+19)//20-m))
        require(0<=s["P95Degree"]<=s["P99Degree"]<=s["MaxDegree"])
        require(0<=s["P95UDegree"]<=s["P99UDegree"]<=s["MaxUDegree"])
        require(s["P99UDegree"]<=s["P99Degree"])
        result[p]={"M":m,"U":u,"eligible_M":t,"eligible_observations":n,
                   "recall":m/t,"hypothetical_fdr":fdr(m,u),
                   "mean_degree":2*(m+u)/n,
                   "P95Degree":s["P95Degree"],"P99Degree":s["P99Degree"],
                   "new_M":s["AddedM"],"new_U":s["AddedU"],
                   "passes":{x["Name"]:{"M":x["M"],"U":x["U"],
                     "new_M":x["NewMVsTwelve"],"new_U":x["NewUVsTwelve"],
                     "hypothetical_fdr":fdr(x["M"],x["U"]),
                     "mean_degree":2*(x["M"]+x["U"])/n}
                     for x in passes if x["Partition"]==p}}
    for part in PARTS:
        require(sum(x["new_M"] for x in result[part]["passes"].values())>=result[part]["new_M"])
        require(sum(x["new_U"] for x in result[part]["passes"].values())>=result[part]["new_U"])
    return result

if __name__=="__main__":
    root=Path(".local")
    results={str(seed):evaluate(json.loads((root/f"v9-sql-physical-seed-{seed}.json").read_text()),seed) for seed in SEEDS}
    out={"scope":"TRAIN_VALIDATION_ONLY_TEST_BLIND","measured_fdr":False,
         "hypothetical_M_accept":.90,"hypothetical_U_accept":.01,
         "marginals_are_not_joint":True,"by_seed":results}
    (root/"v9-prefix3-independent-metrics.json").write_text(json.dumps(out,indent=2)+"\n")
