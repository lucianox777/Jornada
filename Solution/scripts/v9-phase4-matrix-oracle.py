#!/usr/bin/env python3
"""Independent aggregate scoring of the private phase4 proposals, not a scorer."""
import argparse
from collections import Counter,defaultdict
import hashlib
import importlib.util
from itertools import combinations
import json
import math
import os
from pathlib import Path

spec=importlib.util.spec_from_file_location("gate",Path(__file__).with_name("v9-phase4-1k-gate.py"))
G=importlib.util.module_from_spec(spec);spec.loader.exec_module(G)
I=G.I;require=I.require

def distribution(values):
    a=sorted(values)
    if not a:return dict(n=0,mean=None,p95=None,p99=None,max=None)
    return dict(n=len(a),mean=sum(a)/len(a),p95=a[math.ceil(.95*len(a))-1],p99=a[math.ceil(.99*len(a))-1],max=a[-1])

def assess(rows,truth,prior=None):
    # truth never reaches C# projection.
    require(len(rows)==len({x["Id"] for x in rows}) and set(x["Id"] for x in rows)<=set(truth),"MATRIX_CENSUS")
    actual={x["Id"]:x["Person"] for x in rows}
    true=defaultdict(list);pred=defaultdict(list)
    for k,v in actual.items():
        true[truth[k]["base"]].append(k)
        if v is not None:pred[v].append(k)
    true_pairs={tuple(sorted((a,b))) for ids in true.values() for a,b in combinations(ids,2)}
    pred_pairs={tuple(sorted((a,b))) for ids in pred.values() for a,b in combinations(ids,2)}
    tp=len(true_pairs&pred_pairs);fp=pred_pairs-true_pairs;fn=true_pairs-pred_pairs
    same=lambda pair:truth[pair[0]]["manager"]==truth[pair[1]]["manager"]
    require(all(not same(p) or truth[p[0]]["origin"]==truth[p[1]]["origin"] for p in true_pairs),"MATRIX_ORIGIN")
    old=prior["actual"] if prior else {}
    require(all(actual[k]==v for k,v in old.items() if v is not None),"MATRIX_EXISTING_CHANGED")
    oldfp=prior["fp"] if prior else set()
    oldpred=prior["pred"] if prior else {}
    oldtrue=prior["true"] if prior else {}
    before_duplicates={b for b,ids in oldtrue.items() if len({old[k] for k in ids if old[k] is not None})>1}
    duplicates={b for b,ids in true.items() if len({actual[k] for k in ids if actual[k] is not None})>1}
    methods=Counter(x["Method"] for x in rows)
    raw=Counter(x["RawReason"] for x in rows if x["Person"] is None)
    effective=Counter(x["EffectiveReason"] for x in rows if x["Person"] is None)
    fused=sum(len({truth[k]["base"] for k in ids})>1 for ids in pred.values())
    fused_people={truth[k]["base"] for ids in pred.values() if len({truth[j]["base"] for j in ids})>1 for k in ids}
    # Identity-level correctness: a link is correct if all present members
    # of destination correspond to its own truth base, with another origin.
    byid={x["Id"]:x for x in rows}
    out=dict(versions=len(rows),basePeople=len(true),origins=len({truth[k]["origin"] for k in actual}),
             tp=tp,fp=len(fp),fn=len(fn),truePairs=len(true_pairs),recall=tp/len(true_pairs) if true_pairs else None,
             fdr=len(fp)/len(pred_pairs) if pred_pairs else None,falseFusedIdentities=fused,falseFusedTruePeople=len(fused_people),
             splitTruePeople=len(duplicates),newlySplitTruePeople=len(duplicates-before_duplicates),
             fnSameManager=sum(map(same,fn)),fnCrossManager=sum(not same(p) for p in fn),
             fpThisRound=len(fp-oldfp),fpCumulative=len(fp),unresolvedVersions=sum(v is None for v in actual.values()),
             unresolvedOrigins=len({truth[k]["origin"] for k,v in actual.items() if v is None}),
             unresolvedRaw=dict(raw),unresolvedEffective=dict(effective),methods=dict(methods),
             resolvedLater=sum(old[k] is None and actual[k] is not None for k in old),
             previousNewIdentityNowDuplicate=sum(v not in oldpred or False for v in []),
             newIdentityCount=sum(any(byid[k]["Method"] in ("SINGLETON_FOUNDER","S4B_HYPOTHETICAL_FOUNDER") for k in ids) for ids in pred.values()),
             onlyViaNonsecureVersions=sum(x["OnlyViaNonsecure"] for x in rows),
             fsPerRecord=distribution([x["Fs"] for x in rows if not truth[x["Id"]]["cpf"]]),
             candidatesPerRecord=distribution([x["Candidates"] for x in rows if not truth[x["Id"]]["cpf"]]),
             priorCardinality=distribution([x["PriorCardinality"] for x in rows if not truth[x["Id"]]["cpf"]]))
    if prior:
        oldfounders={v for v,ids in oldpred.items() if any(prior["byid"][k]["Method"] in ("SINGLETON_FOUNDER","S4B_HYPOTHETICAL_FOUNDER") for k in ids)}
        out["previousNewIdentityNowDuplicate"]=sum(any(truth[k]["base"] in duplicates-before_duplicates for k in pred[v]) for v in oldfounders if v in pred)
    return out,dict(actual=actual,fp=fp,pred=pred,true=true,byid=byid)

def scoreband(x):
    if x<.9:return "LT_0.9"
    if x<.99:return "0.9_TO_0.99"
    if x<.999:return "0.99_TO_0.999"
    if x<.9999:return "0.999_TO_0.9999"
    return "0.9999_TO_1"

def compare_exclusive(a,b,truth):
    # a=reference arm; b=contrast; do not compare UUIDs across namespaces.
    counts=defaultdict(Counter)
    for k,v in b["actual"].items():
        if v is None or a["actual"][k] is not None:continue
        row=b["byid"][k]
        if row["Method"] not in ("FS_ASSOCIATION","R2_INHERITANCE"):continue
        others=[j for j in b["pred"][v] if truth[j]["origin"]!=truth[k]["origin"]]
        correct=bool(others) and all(truth[j]["base"]==truth[k]["base"] for j in others)
        counts[scoreband(row["Score"])]["correct" if correct else "incorrect_or_unconfirmed"]+=1
    return {k:dict(v) for k,v in counts.items()}

def partition(s):
    return sorted(tuple(sorted(v)) for v in s["pred"].values()),sorted(k for k,v in s["actual"].items() if v is None)

def run(root):
    read=lambda n:I.load(I.read(root/n))
    plan=read("phase4-matrix-bound-plan.json");base=read("identity-receipt.json");s4b=read("s4b-receipt.json")
    census=read("sql-census.json");physical=read("matrix-physical-resource.json")
    require(plan["firstContrast"]["status"]=="MEASURED" and len(plan["cells"])==48,"MATRIX_GATE")
    require(plan["firstContrast"]["receiptSha256"]==hashlib.sha256(I.read(root/"s4b-receipt.json")).hexdigest(),"MATRIX_S4B_SEAL")
    truth_keys={}
    waves=[]
    for n in (1,2):
        folder=root/"fixture"/"ingestion"/f"wave-{n:02}"/"delivery"
        raw=I.read(folder/"bridge-truth.jsonl");manifest=I.load(I.read(folder/"bridge-manifest.json"))
        require(hashlib.sha256(raw).hexdigest().upper()==base["truthSha256ByWave"][n-1],"MATRIX_TRUTH_SEAL")
        waves.append((manifest,raw))
        for line in raw.splitlines():
            x=I.load(line);key=I.version_key(x["GestorCodigo"],x["PackageFileName"],x["OpaquePersonId"])
            require(key not in truth_keys,"MATRIX_TRUTH_DUPLICATE");truth_keys[key]=(x["BasePersonId"],n)
    require(I.compare(waves,census,read("runner-receipt.json"))==base,"MATRIX_BASELINE")
    truth={}
    for x in census:
        k=x["observationId"];t,n=truth_keys[I.version_key(x["manager"],x["package"],x["opaque"])]
        truth[k]=dict(base=t,wave=n,manager=x["manager"],origin=x["originId"],cpf=x["hasCpf"])
    expected={}
    for c in plan["cells"]:
        for t in c["trajectories"]:
            for s in t["rounds"]:
                expected[s["stateId"]]=(c,t,s)
    results={};states={};bykey={}
    path=root/"matrix-private.jsonl"
    require(path.is_file() and not path.is_symlink() and 0<path.stat().st_size<256*1024*1024,"MATRIX_PRIVATE_TYPE")
    with path.open("rb") as stream:
        for line in stream:
            require(len(line)<4*1024*1024,"MATRIX_LINE")
            x=I.load(line);sid=x["stateId"]
            require(sid in expected and sid not in results,"MATRIX_STATE_DUPLICATE")
            c,t,s=expected[sid]
            require((x["cell"],x["schedule"],x["round"],x["throughWave"],x["previousStateId"])==
                    (c["cell"],t["schedule"],s["round"],s["throughWave"],s["previousStateId"]),"MATRIX_STATE_BINDING")
            require({r["Id"] for r in x["rows"]}=={k for k,v in truth.items() if v["wave"]<=s["throughWave"]},"MATRIX_WAVE")
            prior=states.get(s["previousStateId"])
            require(s["previousStateId"] is None or prior is not None,"MATRIX_PREDECESSOR")
            stats,state=assess(x["rows"],truth,prior)
            res=x["resources"]
            require(res["variantSqlExecuted"] is False and res["fsCompared"]==sum(r["Fs"] for r in x["rows"]),"MATRIX_RESOURCE")
            res["secureRecordsPerPerson"]=distribution(res["secureRecordsPerPerson"])
            res["keysPerPerson"]=distribution(res["keysPerPerson"])
            result=dict(cell=c["cell"],factors=c["factors"],schedule=t["schedule"],round=s["round"],throughWave=s["throughWave"],
                        stateId=sid,metrics=stats,resources=res,baselineExactParity=x["baselineExactParity"])
            states[sid]=state;results[sid]=result;bykey[(c["cell"],t["schedule"],s["round"])]=sid
    require(set(results)==set(expected) and len(results)==144,"MATRIX_COMPLETE")
    baseline=results[bykey[("i-b1-records-r1-broad","single",1)]]
    require(baseline["baselineExactParity"] and all(baseline["metrics"][k]==base["pairMetrics"][k] for k in ("tp","fp","fn","truePairs")),"MATRIX_EXACT_PARITY")
    alt=results[bykey[("ii-b1-records-r1-broad","single",1)]]
    ref=s4b["variants"]["one_new_per_group_origin_continuity"]
    require(all(alt["metrics"][k]==ref[k] for k in ("tp","fp","fn","truePairs")),"MATRIX_S4B_PARITY")
    contrasts=[]
    for sid,x in results.items():
        f=x["factors"];schedule=x["schedule"];rnd=x["round"];m=x["metrics"]
        if f["prior"]=="people":
            name=x["cell"].replace("-people-","-records-")
            other=bykey[(name,schedule,rnd)]
            require(partition(states[sid])==partition(states[other]),"MATRIX_FROZEN_PRIOR_DRIFT")
        if f["evidence"]=="b2":
            other=bykey[(x["cell"].replace("-b2-","-b1-"),schedule,rnd)]
            x["b2ExclusiveAssociationsByScore"]=compare_exclusive(states[other],states[sid],truth)
        if f["blocking"]=="secure":
            other=bykey[(x["cell"].replace("-secure","-broad"),schedule,rnd)]
            a,b=states[other],states[sid]
            x["broadIndefiniteSecureNew"]=sum(a["actual"][k] is None and b["actual"][k] is not None
                and b["byid"][k]["Method"] in ("SINGLETON_FOUNDER","S4B_HYPOTHETICAL_FOUNDER","S4B_GROUP") for k in b["actual"])
        if f["s4b"]=="ii":
            other=results[bykey[("i-"+x["cell"][3:],schedule,rnd)]]
            contrasts.append(dict(cell=x["cell"],schedule=schedule,round=rnd,
                deltaTp=m["tp"]-other["metrics"]["tp"],deltaFp=m["fp"]-other["metrics"]["fp"],
                deltaFn=m["fn"]-other["metrics"]["fn"],deltaFused=m["falseFusedIdentities"]-other["metrics"]["falseFusedIdentities"],
                deltaSplit=m["splitTruePeople"]-other["metrics"]["splitTruePeople"]))
    require(len(contrasts)==72,"MATRIX_CONTRAST_CENSUS")
    return dict(schema="V9_PHASE4_1K_MATRIX_MEASURED_V1",status="PASS",commit=os.environ["JORNADA_E2E_EXACT_SHA"],
                winnerSha256=G.P.WINNER,fixtureBinding=plan["fixtureBinding"],s4bReceiptSha256=plan["firstContrast"]["receiptSha256"],
                measuredCells=48,measuredTrajectories=96,measuredStates=144,officialPolicyUnchanged=True,publication=False,
                scientificTest=False,activation=False,physicalResources=physical,baselineExactParity=True,
                s4bParity=True,frozenPriorParity=True,states=list(results.values()),primaryS4bContrasts=contrasts)

def main():
    p=argparse.ArgumentParser();p.add_argument("--root",type=Path,required=True);a=p.parse_args()
    try:
        require(os.environ.get("GITHUB_ACTIONS")=="true" and os.environ.get("JORNADA_RUNTIME_MODE")=="DEV" and os.environ.get("JORNADA_E2E_HTTP_VOLUME_COUNT")=="1000","MATRIX_DEV_ONLY")
        result=run(a.root)
        with (a.root/"matrix-receipt.json").open("x",encoding="utf-8") as f:json.dump(result,f,sort_keys=True,indent=2,allow_nan=False)
    except (ValueError,OSError,KeyError,TypeError,AttributeError,ArithmeticError):
        raise SystemExit("V9_PHASE4_MATRIX_ORACLE_REJECTED") from None
    print("V9_PHASE4_MATRIX_48_CELLS_144_STATES_MEASURED_PASS")
if __name__=="__main__":main()
