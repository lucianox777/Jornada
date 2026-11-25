#!/usr/bin/env python3
"""Private pair-level FN census and bounded nonpublishing retry contrast."""
import argparse
from collections import Counter, defaultdict
import importlib.util
from itertools import combinations
import json
from pathlib import Path

SPEC=importlib.util.spec_from_file_location("identity",Path(__file__).with_name("v9-e2e-1k-identity-census.py"))
I=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(I)
require=I.require

def breakdown(waves, observed, diagnostic, retry, expected_fn):
    truth={}
    for manifest,raw in waves:
        require(I.hashlib.sha256(raw).hexdigest().upper()==manifest["truthSidecar"]["sha256"],"FN_TRUTH_SEAL")
        for line in raw.splitlines():
            row=I.load(line)
            key=I.version_key(row["GestorCodigo"],row["PackageFileName"],row["OpaquePersonId"])
            require(key not in truth,"FN_DUPLICATE_TRUTH")
            truth[key]=row["BasePersonId"]
    rows={}
    true_groups=defaultdict(list)
    seen=set()
    for row in observed:
        key=I.version_key(row["manager"],row["package"],row["opaque"])
        oid=row["observationId"]
        require(type(oid) is int and oid>0 and oid not in rows and key in truth and key not in seen,"FN_ROW_CENSUS")
        seen.add(key)
        rows[oid]=row
        true_groups[truth[key]].append(oid)
    require(seen==set(truth),"FN_TRUTH_CENSUS")
    pending={k for k,v in rows.items() if not v["hasCpf"]}
    d={x["observationId"]:x for x in diagnostic}
    require(set(d)==pending and len(d)==len(diagnostic),"FN_DIAGNOSTIC_CENSUS")
    for x in d.values():
        require(type(x["groupSize"]) is int and x["groupSize"]>0 and
                len(set(x["candidates"]))==len(x["candidates"]),"FN_DIAGNOSTIC_INVALID")
    group_sizes=Counter(x["groupRoot"] for x in d.values())
    require(all(group_sizes[x["groupRoot"]]==x["groupSize"] for x in d.values()),"FN_GROUP_SIZE_DRIFT")
    def resolved(row):
        return I.canonical_uuid(row["currentUuid"]) if row["currentStatus"]=="RESOLVIDO" else None
    actual={k:resolved(v) for k,v in rows.items()}
    origin_members=defaultdict(list)
    for k,v in rows.items(): origin_members[v["originId"]].append(k)
    leaders={o:max((k for k in ids if k in pending),key=lambda k:(rows[k]["version"],k))
             for o,ids in origin_members.items() if any(k in pending for k in ids)}
    def reason(k):
        row=rows[k]
        if actual[k] is not None: return "RESOLVIDO"
        if k not in pending: return "CPF_NAO_RESOLVIDO"
        lead=leaders[row["originId"]]
        return d[lead]["rankReason"] or "SEM_MOTIVO_RAW"
    refs={x for x in actual.values() if x is not None}
    newly_founded={actual[k] for k,v in rows.items() if v["runResult"]=="NOVA_IDENTIDADE" and actual[k] is not None}
    prediction=dict(actual)
    rr={x["ObservationId"]:x for x in retry}
    require(len(rr)==len(retry),"FN_RETRY_DUPLICATE")
    for k,x in rr.items():
        require(k in pending and actual[k] is None and x["OriginId"]==rows[k]["originId"],"FN_RETRY_SCOPE")
        target=I.canonical_uuid(x["ProposedUuid"])
        require(target is None or target in refs,"FN_RETRY_NEW_REFERENCE")
        prediction[k]=target
    for o,ids in origin_members.items():
        selected=[k for k in ids if k in rr]
        if selected:
            require(set(selected)==set(ids) and len({prediction[k] for k in selected})==1,"FN_RETRY_ORIGIN_DRIFT")
    def label(a,b): return " + ".join(sorted((str(a),str(b))))
    by_reason=Counter(); by_raw=Counter(); by_publication=Counter(); by_cpf=Counter()
    by_truth_size=Counter(); by_inferred_size=Counter(); by_search=Counter(); by_base_cpf=Counter()
    opportunity=0; recovered=0; recovered_new=0; old_tp=0
    for ids in true_groups.values():
        anchors={actual[k] for k in ids if rows[k]["hasCpf"] and actual[k] is not None}
        group_new={actual[k] for k in ids if actual[k] in newly_founded}
        def search(k):
            if k not in pending: return "CPF_FORA_DO_BLOCKING"
            if not anchors: return "SEM_REFERENCIA_CPF_PRE_FULL"
            return "REFERENCIA_VERDADEIRA_ENCONTRADA" if anchors.intersection(d[k]["candidates"]) else "REFERENCIA_VERDADEIRA_NAO_ENCONTRADA"
        for a,b in combinations(ids,2):
            if actual[a] is not None and actual[a]==actual[b]:
                old_tp+=1
                continue
            by_reason[label(reason(a),reason(b))]+=1
            by_raw[label((d[a]["rankReason"] or "SEM_MOTIVO_RAW") if a in d else "CPF",
                         (d[b]["rankReason"] or "SEM_MOTIVO_RAW") if b in d else "CPF")]+=1
            by_publication[label(rows[a]["publicationReason"] or "CPF",rows[b]["publicationReason"] or "CPF")]+=1
            by_cpf[label("CPF" if rows[a]["hasCpf"] else "SEM_CPF","CPF" if rows[b]["hasCpf"] else "SEM_CPF")]+=1
            by_base_cpf["COM_CPF_NA_PESSOA_VERDADEIRA" if anchors else "SEM_CPF_NA_PESSOA_VERDADEIRA"]+=1
            by_truth_size[str(len(ids))]+=1
            by_inferred_size[label(d[a]["groupSize"] if a in d else 0,d[b]["groupSize"] if b in d else 0)]+=1
            by_search[label(search(a),search(b))]+=1
            if group_new and (actual[a] is None or actual[b] is None):
                opportunity+=1
            if prediction[a] is not None and prediction[a]==prediction[b]:
                recovered+=1
                recovered_new+=prediction[a] in newly_founded
    pred_sizes=Counter(v for v in prediction.values() if v is not None)
    joint=Counter((base,prediction[k]) for base,ids in true_groups.items() for k in ids if prediction[k] is not None)
    new_fp=sum(I.comb(n,2) for n in pred_sizes.values())-sum(I.comb(n,2) for n in joint.values())
    buckets={"byLeaderReasonPair":by_reason,"byOwnRawReasonPair":by_raw,"byPublicationReasonPair":by_publication,
             "byCpfPair":by_cpf,"byTrueGroupCpf":by_base_cpf,"byTrueGroupSize":by_truth_size,
             "byInferredSilverGroupSizePair":by_inferred_size,"byTrueCpfReferenceSearchPair":by_search}
    require(all(sum(x.values())==expected_fn for x in buckets.values()),"FN_PAIR_TOTAL")
    return {"schema":"V9_DEV_FN_PAIR_BREAKDOWN_V1","status":"PASS","fn":expected_fn,"tp":old_tp,
            **{k:dict(v) for k,v in buckets.items()},
            "singleFullContrast":{"mode":"POST_FULL_READ_ONLY_RETRY_UNRESOLVED_ORIGINS_B1_R1_BROAD",
            "eligibleVersions":len(rr),"opportunityPairsWithNewFounderInTrueGroup":opportunity,
            "recoveredFnPairs":recovered,"recoveredViaNewFounderUuid":recovered_new,
            "remainingFnPairs":expected_fn-recovered,"counterfactualFpPairs":new_fp,
            "publicationSimulated":False,"secondPhysicalFullExecuted":False,"causalGeneralization":False}}

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--root",required=True,type=Path)
    a=p.parse_args()
    try:
        r=a.root
        waves=[]
        for n in (1,2):
            path=r/"fixture"/"ingestion"/f"wave-{n:02}"/"delivery"
            waves.append((I.load(I.read(path/"bridge-manifest.json")),I.read(path/"bridge-truth.jsonl")))
        read=lambda name:I.load(I.read(r/name))
        observed=read("sql-census.json");receipt=read("identity-receipt.json")
        require(receipt["status"]=="PASS","FN_IDENTITY_GATE")
        report=breakdown(waves,observed,read("fn-private.json"),read("retry-private.json"),receipt["pairMetrics"]["fn"])
        require(report["tp"]==receipt["pairMetrics"]["tp"],"FN_TP_RECONCILIATION")
        with (r/"fn-receipt.json").open("x",encoding="utf-8") as f: json.dump(report,f,sort_keys=True,indent=2)
    except (ValueError,OSError,KeyError,TypeError,AttributeError):
        raise SystemExit("V9_E2E_FN_BREAKDOWN_REJECTED") from None
    print("V9_E2E_FN_BREAKDOWN_AND_READ_ONLY_RETRY_PASS")
if __name__=="__main__": main()
