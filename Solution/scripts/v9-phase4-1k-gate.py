#!/usr/bin/env python3
"""Bind the 24-cell plan to a verified S4b measurement in the same private attempt.

No scorer, SQL or publication here. The resulting plan is NOT an executor.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re

def module(name, filename):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(filename))
    mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
    return mod

P=module("plan","v9-phase4-1k-plan.py")
I=module("identity","v9-e2e-1k-identity-census.py")
require=I.require
VARIANTS={"official_indefinite","one_new_per_group","one_new_per_record",
          "one_new_per_group_origin_continuity","one_new_per_record_origin_continuity"}
BASE_SCOPE={"cross_manager":1598,"same_manager_same_origin":244}

def validate(s4b, nucleus, identity, graph, lineage, sha, attempt):
    require(re.fullmatch("[0-9a-f]{40}",sha) and s4b["commit"]==nucleus["commit"]==sha
            and s4b["runId"]==nucleus["runId"]==identity["runId"]==graph["runId"]
            and I.canonical_uuid(s4b["runId"]) is not None and lineage["attempt"]==attempt,
            "PHASE4_ATTEMPT_BINDING")
    require(s4b["schema"]=="V9_PHASE4_S4B_1K_V1" and s4b["status"]=="PASS"
            and s4b["winnerSha256"]==identity["winnerSha256"]==P.WINNER
            and s4b["baselineIdentityCounts"]=={"people":1000,"versions":2349,"origins":1957}
            and s4b["officialPolicyUnchanged"] is True and s4b["alternativePublication"] is False
            and s4b["scientificTest"] is False and s4b["activated"] is False
            and s4b["factorialExecuted"] is False
            and nucleus["E2E_NUCLEO_PASS"] is True and nucleus["E2E_INTEGRAL_PASS"] is False
            and identity["status"]=="PASS" and lineage["status"]=="PASS"
            and all(identity[k]==n for k,n in P.COUNTS.items())
            and all(graph[k] is True for k in ("actualConsumedSilverEdgesVerified","actualConsumedScoresVerified",
                "actualConsumedGroupsVerified","actualGoldRecordScoresVerified")),"PHASE4_S4B_RECEIPT")
    require(set(s4b["variants"])==VARIANTS,"PHASE4_VARIANTS")
    base=s4b["variants"]["official_indefinite"]
    require((base["tp"],base["fp"],base["fn"],base["truePairs"])==(563,0,1842,2405)
            and base["fnByScope"]==BASE_SCOPE
            and all(identity["pairMetrics"][k]==base[k] for k in ("tp","fp","fn","truePairs")),
            "PHASE4_BASELINE_PARITY")
    for v in s4b["variants"].values():
        require(all(type(v[k]) is int and v[k]>=0 for k in ("tp","fp","fn","truePairs","fnRecovered",
                "fpIntroduced","falseFusedIdentitiesIntroduced","newIdentities"))
                and v["tp"]+v["fn"]==v["truePairs"]==2405
                and v["fn"]+v["fnRecovered"]==1842 and v["fp"]==v["fpIntroduced"]
                and sum(v["fnByScope"].values())==v["fn"]
                and set(v["fnByScope"])==set(v["recoveredByScope"])==set(BASE_SCOPE)
                and all(v["fnByScope"][k]+v["recoveredByScope"][k]==n for k,n in BASE_SCOPE.items()),
                "PHASE4_S4B_METRIC_DRIFT")
    return base

def build(root, sha, attempt):
    raw=lambda n:I.read(root/n)
    read=lambda n:I.load(raw(n))
    s4b=read("s4b-receipt.json");nucleus=read("nucleus-receipt.json")
    identity=read("identity-receipt.json");graph=read("actual-graph-receipt.json")
    lineage=read("http-lineage.json");base=validate(s4b,nucleus,identity,graph,lineage,sha,attempt)
    delivery=read("wave-preflight-receipt.json")
    require(hashlib.sha256(raw("wave-preflight-receipt.json")).hexdigest()==lineage["preflightSha256"]
            and delivery["status"]=="PASS" and all(delivery[k]==n for k,n in P.COUNTS.items())
            and [x["wave"] for x in delivery["packagesByWave"]]==[1,2]
            and all(len(x["packages"])==4 for x in delivery["packagesByWave"]),"PHASE4_DELIVERY_BINDING")
    original=P.bind_fixture(root/"fixture")
    # Bind actual derived packages delivered by HTTP and their external truth,
    # in addition to the original corpus proven by the private preflight.
    delivered_truth=[]
    for n in (1,2):
        folder=root/"fixture"/"ingestion"/f"wave-{n:02}"/"delivery"
        delivered_truth.append(hashlib.sha256(I.read(folder/"bridge-truth.jsonl")).hexdigest().upper())
    require(delivered_truth==identity["truthSha256ByWave"],"PHASE4_TRUTH_BINDING")
    fixture={**original,"originalOrderedPackagesSha256":original["orderedPackagesSha256"],
             "originalTruthSha256ByWave":original["truthSha256ByWave"],
             "orderedPackagesSha256":P.sha(P.canonical(delivery["packagesByWave"])),
             "truthSha256ByWave":delivered_truth,
             "preflightSha256":lineage["preflightSha256"]}
    baseline={"commit":P.BASE_SHA,"artifactId":P.BASE_ARTIFACT,"zipSha256":P.BASE_ZIP_SHA,
              "winnerSha256":P.WINNER,"counts":P.COUNTS.copy(),"pairMetrics":identity["pairMetrics"]}
    plan=P.build_plan(baseline,attempt,fixture)
    plan["status"]="S4B_VERIFIED_MATRIX_PLANNED_ONLY"
    plan["firstContrast"].update(status="MEASURED",receiptSha256=hashlib.sha256(raw("s4b-receipt.json")).hexdigest(),
                                commit=sha,runId=nucleus["runId"])
    plan["factorialGate"]={"s4bReceiptVerified":True,"samePhysicalAttempt":True,
                          "scoringExecutorImplemented":False,"executionAuthorizedByThisPlan":False}
    return plan

def r2_indication_hypothesis(rows):
    """Proposed narrow nonpublishing R2 experiment: only COM_INDICIO.

    Return extra proposals, not decisions or links. No truth, new identities,
    scores or persistence. Caller still needs origin-leader projection and
    independent state per cell. Other abstentions/conflicts stay as R1.
    """
    by_id={x["id"]:x for x in rows}
    require(len(by_id)==len(rows),"PHASE4_R2_DUPLICATE")
    groups={}
    for x in rows:
        require(x["status"] in ("RESOLVIDO","NAO_RESOLVIDO","CONFLITO")
                and type(x["protected"]) is bool
                and (x["resolved"] is not None)==(x["status"]=="RESOLVIDO"),"PHASE4_R2_INPUT")
        groups.setdefault(x["group"],[]).append(x)
    out={}
    for members in groups.values():
        targets={v for x in members for v in (x["resolved"],x["established"]) if v is not None}
        if len(targets)!=1 or any(x["reason"]=="V9_R1_GRUPO_GOLD_CONFLITANTE" for x in members):
            continue
        target=next(iter(targets))
        for x in members:
            if (len(members)>1 and x["status"]=="NAO_RESOLVIDO"
                    and x["reason"]=="V9_R1_INDEFINIDO_COM_INDICIO"
                    and not x["protected"] and x["established"] is None):
                out[x["id"]]=target
    return out

def main():
    p=argparse.ArgumentParser();p.add_argument("--root",type=Path,required=True);a=p.parse_args()
    try:
        require(os.environ.get("GITHUB_ACTIONS")=="true" and os.environ.get("JORNADA_RUNTIME_MODE")=="DEV"
                and os.environ.get("JORNADA_E2E_HTTP_VOLUME_COUNT")=="1000","PHASE4_DEV_ONLY")
        plan=build(a.root,os.environ["JORNADA_E2E_EXACT_SHA"],os.environ["JORNADA_V9_DEV_ATTEMPT"])
        with (a.root/"phase4-matrix-bound-plan.json").open("x",encoding="utf-8") as f:
            json.dump(plan,f,sort_keys=True,indent=2)
    except (OSError,ValueError,KeyError,TypeError,AttributeError):
        raise SystemExit("V9_PHASE4_MATRIX_GATE_REJECTED") from None
    print("V9_PHASE4_S4B_GATE_PASS_24_CELLS_PLANNED_ZERO_EXECUTED")
if __name__=="__main__": main()
