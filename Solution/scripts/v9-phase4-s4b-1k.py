#!/usr/bin/env python3
"""S4b counterfactual on the authenticated DEV baseline. No DB access or writes."""
import argparse
from collections import Counter, defaultdict
from decimal import Decimal
import importlib.util
from itertools import combinations
import json
import os
from pathlib import Path

SPEC=importlib.util.spec_from_file_location("identity",Path(__file__).with_name("v9-e2e-1k-identity-census.py"))
I=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(I)
require=I.require
SEM="V9_R1_INDEFINIDO_SEM_INDICIO"

def band(value):
    x=Decimal(str(value))
    require(x.is_finite() and 0<=x<=1,"S4B_POSTERIOR")
    if x<Decimal(".99"): return "LT_0.99"
    if x<Decimal(".999"): return "0.99_TO_0.999"
    if x<Decimal(".9999"): return "0.999_TO_0.9999"
    return "0.9999_TO_1"

def prepare(observed, diagnostic):
    # No truth in this function. Experimental IDs cannot be operational UUIDs.
    rows={x["observationId"]:x for x in observed}
    d={x["observationId"]:x for x in diagnostic}
    require(len(rows)==len(observed) and len(d)==len(diagnostic)
            and set(d)=={k for k,v in rows.items() if not v["hasCpf"]},"S4B_UNIVERSE")
    actual={k:I.canonical_uuid(v["currentUuid"]) if v["currentStatus"]=="RESOLVIDO" else None for k,v in rows.items()}
    groups=defaultdict(list); origins=defaultdict(list)
    for k,v in rows.items(): origins[v["originId"]].append(k)
    for k,v in d.items(): groups[v["groupRoot"]].append(k)
    strata={}; eligible={}; excluded=Counter(); excluded_versions=Counter()
    for g,ids in groups.items():
        size=len(ids)
        require(all(d[k]["groupSize"]==size for k in ids),"S4B_GROUP_SIZE")
        if size==1:
            require(d[ids[0]]["groupMinPosterior"] is None,"S4B_SINGLETON_POSTERIOR")
            continue
        require(len({rows[k]["manager"] for k in ids})==size
                and all(set(ids)-{k}<=set(d[k]["silverNeighbors"]) for k in ids),"S4B_COMPLETE_LINK")
        p=d[ids[0]]["groupMinPosterior"]
        require(p is not None and all(d[k]["groupMinPosterior"]==p for k in ids),"S4B_GROUP_POSTERIOR_DRIFT")
        strata[g]=f"{size}|{band(p)}"
        if not any(d[k]["rankReason"]==SEM for k in ids): continue
        reason=None
        if any(actual[k] is not None or rows[k]["currentMethod"] in I.PROTECTED for k in ids): reason="EXISTING_OR_PROTECTED"
        elif any(d[k]["candidates"] for k in ids): reason="GOLD_CANDIDATE"
        elif not all(d[k]["rankReason"]==SEM and rows[k]["runResult"]=="INDEFINIDA" for k in ids): reason="MIXED_REASON"
        if reason:
            excluded[reason]+=1;excluded_versions[reason]+=size;continue
        eligible[g]=ids
    gid={k:g for g,ids in eligible.items() for k in ids}
    def variant(mode,origin=False):
        pred=dict(actual); provenance={}
        if mode=="official_indefinite": return pred,provenance
        for k,g in gid.items():
            target=f"experiment:{mode}:{g if mode=='one_new_per_group' else k}"
            pred[k]=target; provenance[target]=strata[g]
        if origin:
            for ids in origins.values():
                if not any(k in gid for k in ids): continue
                if any(actual[k] is not None or rows[k]["currentMethod"] in I.PROTECTED for k in ids):
                    for k in ids: pred[k]=actual[k]
                    continue
                leader=max(ids,key=lambda k:(rows[k]["version"],k))
                for k in ids: pred[k]=pred[leader]
        require(all(pred[k]==v for k,v in actual.items() if v is not None),"S4B_EXISTING_MUTATION")
        return pred,provenance
    return rows,actual,eligible,strata,excluded,excluded_versions,variant

def metrics(rows, truth, actual, pred, provenance):
    require(set(rows)==set(truth)==set(actual)==set(pred),"S4B_TRUTH_CENSUS")
    base=defaultdict(list); predicted=defaultdict(list)
    for k,t in truth.items(): base[t].append(k)
    for k,v in pred.items():
        if v is not None: predicted[v].append(k)
    def match(p,a,b): return p[a] is not None and p[a]==p[b]
    def scope(a,b):
        if rows[a]["manager"]==rows[b]["manager"]:
            require(rows[a]["originId"]==rows[b]["originId"],"S4B_INTRA_ORIGIN_DRIFT")
            return "same_manager_same_origin"
        return "cross_manager"
    fn=Counter(); old_fn=Counter(); recovered=Counter(); by_true_size=Counter(); strata=defaultdict(Counter)
    tp=0; true_pairs=0; lost=0
    for ids in base.values():
        for a,b in combinations(ids,2):
            true_pairs+=1; s=scope(a,b)
            before=match(actual,a,b); after=match(pred,a,b)
            if not before: old_fn[s]+=1
            if after: tp+=1
            else: fn[s]+=1
            if before and not after: lost+=1
            if not before and after:
                recovered[s]+=1;by_true_size[str(len(ids))]+=1
                require(pred[a] in provenance,"S4B_RECOVERY_WITHOUT_NEW_ID")
                strata[provenance[pred[a]]]["fnRecovered"]+=1
                strata[provenance[pred[a]]]["fnRecovered_"+s]+=1
    fp=0;introduced=0;fused=0;new_fused=0; new_ids=0
    for v,ids in predicted.items():
        distinct={truth[k] for k in ids}
        new=v in provenance
        if new:
            new_ids+=1
            st=strata[provenance[v]]
            st["newIdentities"]+=1;st["versionsAssigned"]+=len(ids)
            if len(distinct)>1: st["falseFusedIdentities"]+=1;new_fused+=1
        if len(distinct)>1: fused+=1
        for a,b in combinations(ids,2):
            if truth[a]!=truth[b]:
                fp+=1
                if not match(actual,a,b):
                    introduced+=1
                    require(new,"S4B_FP_WITHOUT_NEW_ID")
                    strata[provenance[v]]["fpIntroduced"]+=1
    split=sum(len({pred[k] for k in ids if pred[k] is not None})>1 for ids in base.values())
    nfn=sum(fn.values()); rec=sum(recovered.values())
    require(lost==0 and sum(old_fn.values())==nfn+rec and tp+nfn==true_pairs,"S4B_PAIR_RECONCILIATION")
    return {"tp":tp,"fp":fp,"fn":nfn,"truePairs":true_pairs,"fnRecovered":rec,
            "fpIntroduced":introduced,"falseFusedIdentities":fused,"falseFusedIdentitiesIntroduced":new_fused,
            "splitTruePersons":split,"newIdentities":new_ids,"resolvedVersions":sum(x is not None for x in pred.values()),
            "recall":tp/true_pairs if true_pairs else None,"fdr":fp/(tp+fp) if tp+fp else None,
            "fnByScope":{k:fn[k] for k in ("cross_manager","same_manager_same_origin")},
            "recoveredByScope":{k:recovered[k] for k in ("cross_manager","same_manager_same_origin")},
            "recoveredByTrueGroupSize":dict(by_true_size),
            "newIdentityMetricsBySilverSizeAndMinPosterior":{k:dict(v) for k,v in strata.items()}}

def contrast(observed, diagnostic, truth):
    rows,actual,eligible,strata,excluded,excluded_versions,variant=prepare(observed,diagnostic)
    results={}
    for mode in ("official_indefinite","one_new_per_group","one_new_per_record"):
        p,prov=variant(mode)
        results[mode]=metrics(rows,truth,actual,p,prov)
        if mode!="official_indefinite":
            p,prov=variant(mode,True)
            results[mode+"_origin_continuity"]=metrics(rows,truth,actual,p,prov)
    counts=defaultdict(Counter)
    for g,ids in eligible.items():
        c=counts[strata[g]];c["groups"]+=1;c["versions"]+=len(ids)
        c["internalTruePairs"]+=sum(truth[a]==truth[b] for a,b in combinations(ids,2))
        c["internalFalsePairs"]+=sum(truth[a]!=truth[b] for a,b in combinations(ids,2))
    require(results["one_new_per_group"]["fnRecovered"]==sum(c["internalTruePairs"] for c in counts.values())
            and results["one_new_per_group"]["fpIntroduced"]==sum(c["internalFalsePairs"] for c in counts.values())
            and results["one_new_per_record"]["fnRecovered"]==results["one_new_per_record"]["fpIntroduced"]==0,
            "S4B_DIRECT_CONTRAST_RECONCILIATION")
    return {"eligibleGroups":len(eligible),"eligibleVersions":sum(map(len,eligible.values())),
            "excludedGroups":dict(excluded),"excludedVersions":dict(excluded_versions),
            "eligibleBySilverSizeAndMinPosterior":{k:dict(v) for k,v in counts.items()},
            "variants":results}

def main():
    p=argparse.ArgumentParser();p.add_argument("--root",required=True,type=Path);a=p.parse_args()
    try:
        root=a.root; read=lambda n:I.load(I.read(root/n))
        require(os.environ.get("GITHUB_ACTIONS")=="true" and os.environ.get("JORNADA_RUNTIME_MODE")=="DEV"
                and os.environ.get("JORNADA_E2E_HTTP_VOLUME_COUNT")=="1000","S4B_DEV_ONLY")
        receipt=read("nucleus-receipt.json");identity=read("identity-receipt.json");graph=read("actual-graph-receipt.json")
        require(receipt["commit"]==os.environ["JORNADA_E2E_EXACT_SHA"] and receipt["E2E_NUCLEO_PASS"] is True
                and receipt["E2E_INTEGRAL_PASS"] is False and receipt["runId"]==identity["runId"]==graph["runId"]
                and all(graph[k] is True for k in ("actualConsumedSilverEdgesVerified","actualConsumedScoresVerified","actualConsumedGroupsVerified","actualGoldRecordScoresVerified")),
                "S4B_PHYSICAL_GATE")
        rows=read("sql-census.json"); diagnostic=read("fn-private.json")
        waves=[]
        for n in (1,2):
            path=root/"fixture"/"ingestion"/f"wave-{n:02}"/"delivery"
            waves.append((I.load(I.read(path/"bridge-manifest.json")),I.read(path/"bridge-truth.jsonl")))
        check=I.compare(waves,rows,read("runner-receipt.json"))
        require(check==identity and check["winnerSha256"]==I.WINNER and check["sourceVersions"]==2349
                and check["persistentOrigins"]==1957 and check["cpfVersions"]==315,"S4B_BASELINE_DRIFT")
        truth_by_key={}
        for _,raw in waves:
            for line in raw.splitlines():
                x=I.load(line); key=I.version_key(x["GestorCodigo"],x["PackageFileName"],x["OpaquePersonId"])
                truth_by_key[key]=x["BasePersonId"]
        truth={x["observationId"]:truth_by_key[I.version_key(x["manager"],x["package"],x["opaque"])] for x in rows}
        report=contrast(rows,diagnostic,truth)
        baseline=report["variants"]["official_indefinite"]
        require((baseline["tp"],baseline["fp"],baseline["fn"],baseline["truePairs"])==(563,0,1842,2405)
                and baseline["fnByScope"]=={"cross_manager":1598,"same_manager_same_origin":244},"S4B_BASELINE_METRICS")
        fn=read("fn-receipt.json")
        require(fn["byLeaderReasonPair"].get(SEM+" + "+SEM)==1692
                and fn["byTrueDirectSilverSearch"].get("MESMO_GESTOR_FORA_GRAFO")==244,"S4B_REASON_BASELINE")
        report.update(schema="V9_PHASE4_S4B_1K_V1",status="PASS",commit=receipt["commit"],runId=receipt["runId"],
                      winnerSha256=I.WINNER,baselineIdentityCounts={"people":1000,"versions":2349,"origins":1957},
                      posteriorMeasure="MIN_INTERNAL_DIRECTIONAL_POSTERIOR_NOT_JOINT",officialPolicyUnchanged=True,
                      alternativePublication=False,scientificTest=False,activated=False,factorialExecuted=False)
        with (root/"s4b-receipt.json").open("x",encoding="utf-8") as f: json.dump(report,f,sort_keys=True,indent=2)
    except (ValueError,OSError,KeyError,TypeError,AttributeError,ArithmeticError):
        raise SystemExit("V9_PHASE4_S4B_REJECTED") from None
    print("V9_PHASE4_S4B_1K_CONTRAST_PASS_NO_ALTERNATIVE_PUBLICATION")
if __name__=="__main__":main()
