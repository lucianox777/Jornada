#!/usr/bin/env python3
"""Same-frame physical indexed SQLite A/B/C/D, E0, composition and adaptive audit.

TRAIN-only frequencies; VALIDATION replay of the SAME rules; TEST excluded.
The A-D historical arms are FIXED-PASS PROXIES, D0/D1 are NOT dynamic.
ADAPTIVE_FREQ is a *real* per-query frequency-conditioned indexed retrieval,
but not a calibrated FS model or operational SQL Server system.
"""
import argparse
import csv
import hashlib
import importlib.util
import json
import math
import sqlite3
from pathlib import Path

VERSION="V9_ABCD_E0_COMPOSITIONS_DYNAMIC_PHYSICAL_DIAGNOSTIC_V1"
SPLITS=("TRAIN","VALIDATION")
CAP_LIMIT=1000000
E0=("nome_normalizado","nome_mae_normalizado","data_nascimento")

def load(name,filename):
    path=Path(__file__).with_name(filename)
    spec=importlib.util.spec_from_file_location(name,path)
    obj=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj

def declare():
    abc=load("v9_abcd_config","v9-abc-paired-blocking.py")
    base=load("v9_abcd_physical_base","v9-sqlite-memory-cells.py")
    planner=load("v9_adaptive_train_planner","v9-adaptive-frequency-planner.py")
    return abc,base,planner

def compare(rows,baseline,abc,base,planner,cap=CAP_LIMIT):
    if type(cap) is not int or not 1<=cap<=CAP_LIMIT:
        raise ValueError("invalid indexed candidate pair cap")
    base.validate_rows(rows)
    if set(abc.ARMS)!={a+str(n) for a in "ABCD" for n in (0,1)}:
        raise ValueError("ABCD strategies changed without new diagnostic version")
    if (baseline.get("selection_split")!="TRAIN_ONLY" or
        baseline.get("TEST_evaluated") is not False or
        baseline.get("diagnostic_only") is not True or
        baseline.get("may_promote_policy") is not False):
        raise ValueError("baseline TEST-blind frozen selection is mandatory")
    fit=baseline.get("minimum_U_95pct_diagnostic",{})
    if fit.get("trained_on")!="TRAIN_ONLY" or fit.get("validation_refit") is not False:
        raise ValueError("optimized physical composition not trained exclusively in TRAIN")
    selected=fit.get("plans",{}).get("greedy_pruned")
    chosen=(selected.get("selected_cells") if selected else None)
    if chosen is not None and (not isinstance(chosen,list) or not chosen
        or len(chosen)!=len(set(chosen)) or not set(chosen).issubset(base.EXPANDED)):
        raise ValueError("corrupt TRAIN-fitted composition")
    # Four fixed historical strategies; D is intentionally a proxy, not
    # the real frequency-based adaptive candidate retrieval below.
    passes={}
    for arm,fields_list in abc.ARMS.items():
        passes[arm]=tuple(tuple(fields) for fields in fields_list)
    raw_passes=set(sum((list(v) for v in passes.values()),[]))
    raw_passes.add(E0)
    for fields in raw_passes:
        if not set(fields).issubset(base.REQUIRED) or len(fields) not in (2,3):
            raise ValueError("noncanonical ABCD key")
    # Train identity-base frequencies, no TEST/VAL leakage by planner contract.
    train_counts=planner.train_counts(rows)
    observed_indices={}
    con=sqlite3.connect(":memory:")
    try:
        con.executescript("""
            CREATE TABLE o(
              oid TEXT PRIMARY KEY COLLATE BINARY,
              part TEXT NOT NULL COLLATE BINARY,
              manager TEXT NOT NULL COLLATE BINARY);
            CREATE TABLE keys(
              cell TEXT NOT NULL COLLATE BINARY,
              part TEXT NOT NULL COLLATE BINARY,
              composite TEXT NOT NULL COLLATE BINARY,
              oid TEXT NOT NULL COLLATE BINARY,
              PRIMARY KEY(cell,part,composite,oid)) WITHOUT ROWID;
        """)
        o,k=[],[]
        for row in rows:
            split=row["particao"]
            if split not in SPLITS or row["cpf"].strip():
                continue
            if not (base.meaningful(row["nome_normalizado"]) and
                   (base.meaningful(row["nome_mae_normalizado"]) or
                    base.usable_birth(row["data_nascimento"]) is not None)):
                continue
            oid=row["observacao_id"]
            o.append((oid,split,row["gestor"]))
            for cell,keys in base.keys(row).items():
                if cell in base.EXPANDED:
                    for value in keys:
                        k.append(("COMPOSE_"+cell,split,value,oid))
            for fields in raw_passes:
                key=tuple(row[field].strip() for field in fields)
                # Never use an invalid DOB sentinel as a real date.
                if "data_nascimento" in fields and base.usable_birth(
                        row["data_nascimento"]) is None:
                    continue
                if all(key):
                    k.append(("ABCD_"+":".join(fields),split,base.key(*key),oid))
        con.executemany("INSERT INTO o VALUES(?,?,?)",o)
        con.executemany("INSERT INTO keys VALUES(?,?,?,?)",k)
        join="""SELECT a.oid,b.oid
           FROM keys a JOIN keys b
             ON a.cell=b.cell AND a.part=b.part AND a.composite=b.composite
             AND b.oid>a.oid
           JOIN o oa ON oa.oid=a.oid JOIN o ob ON ob.oid=b.oid
           WHERE a.cell=? AND a.part=? AND oa.manager<>ob.manager
           LIMIT ?"""
        allnames=(["COMPOSE_"+c for c in base.EXPANDED]
            +["ABCD_"+":".join(p) for p in sorted(raw_passes)])
        for split in SPLITS:
            for name in allnames:
                pair=set(con.execute(join,(name,split,cap+1)))
                if len(pair)>cap:
                    raise ValueError("indexed cell exceeds cap; never truncate")
                observed_indices[split,name]=pair
        # Adaptive query-side ordering is based on TRAIN identity frequencies.
        # Each qualifying source searches up to 3 keys, allowing asymmetric
        # plans of each endpoint and de-duplicating unordered pairs.
        adaptive={}
        lookup="""SELECT k.oid FROM keys k JOIN o v ON k.oid=v.oid
                  WHERE k.cell=? AND k.part=? AND k.composite=?
                    AND v.manager<>? AND k.oid<>?
                  LIMIT ?"""
        for split in SPLITS:
            subset=[r for r in rows if r["particao"]==split]
            for n in (2,3):
                found=set()
                for row in subset:
                    if (row["cpf"].strip() or
                        not base.meaningful(row["nome_normalizado"]) or
                        not (base.meaningful(row["nome_mae_normalizado"]) or
                             base.usable_birth(row["data_nascimento"]) is not None)):
                        continue
                    for choice in planner.plan(row,train_counts,cap=30,max_passes=n):
                        fields=tuple(choice["fields"])
                        if ("data_nascimento" in fields and
                            base.usable_birth(row["data_nascimento"]) is None):
                            continue
                        col="ABCD_"+":".join(fields)
                        value=base.key(*choice["key"])
                        for (other,) in con.execute(lookup,(col,split,value,
                                 row["gestor"],row["observacao_id"],cap+1)):
                            a,b=sorted((row["observacao_id"],other))
                            found.add((a,b))
                            if len(found)>cap:
                                raise ValueError("adaptive index exceeds candidate cap")
                adaptive[split,n]=found
    finally:
        con.close()
    def union(split,selected):
        return set().union(*(observed_indices[split,s] for s in selected))
    results={}
    for split in SPLITS:
        subset=[r for r in rows if r["particao"]==split]
        all_truth=base.truth_for(subset,False)
        eligible_truth=base.truth_for(subset,True)
        obs=sum(not r["cpf"].strip() and base.meaningful(r["nome_normalizado"])
           and (base.meaningful(r["nome_mae_normalizado"]) or
                base.usable_birth(r["data_nascimento"]) is not None) for r in subset)
        if not all_truth or not eligible_truth or not obs:
            raise ValueError("missing positive truth/eligible frame in split")
        if (len(all_truth)!=baseline["results"][split]["all_no_cpf_truth_M"]
           or len(eligible_truth)!=baseline["results"][split]["name_eligible_truth_M"]
           or obs!=baseline["results"][split]["name_eligible_observations"]):
            raise ValueError("different M and eligible denominators; refuse comparison")
        # Reconcile each physical composition cell with same source run.
        for name in base.EXPANDED:
            pair=observed_indices[split,"COMPOSE_"+name]
            M=len(pair & eligible_truth)
            stored=baseline["results"][split]["cells"][name]
            if stored["M"]!=M or stored["U"]!=len(pair)-M:
                raise ValueError("canonical physical composition M/U diverges")
        arms={}
        def emit(label,pair,semantics):
            metrics=base.measure(pair,eligible_truth,obs)
            boot=base.cluster_bootstrap_global_recall(
                subset,all_truth,pair,rounds=200,
                seed=14000+(0 if split=="TRAIN" else 1))
            arms[label]={**metrics,
                "global_M_without_CPF":len(all_truth),
                "global_M_recall":metrics["M"]/len(all_truth),
                "lower_95_identity_cluster":boot["lower_one_sided_95"],
                "statistical_certification":False,
                "strategy_semantics":semantics}
        e0=observed_indices[split,"ABCD_"+":".join(E0)]
        for label,plist in passes.items():
            union_cells=union(split,["ABCD_"+":".join(p) for p in plist])
            emit(label,union_cells,"LEGACY_FIXED_PASS_PROXY_NOT_PRODUCTION_STRATEGY")
            emit(label+"+E0",union_cells|e0,
                 "LEGACY_FIXED_PASS_PROXY_PLUS_TRIPLE_EXACT")
            if arms[label+"+E0"]["M"]<arms[label]["M"]:
                raise ValueError("E0 cannot lose M")
        for n in (2,3):
            emit("ADAPTIVE_FREQ_TOP_"+str(n),adaptive[split,n],
                 "TRAIN_IDENTITY_FREQUENCY_PER_QUERY_INDEXED_NO_FS_CALIBRATION")
            emit("ADAPTIVE_FREQ_TOP_"+str(n)+"+E0",adaptive[split,n]|e0,
                 "TRAIN_IDENTITY_FREQUENCY_QUERY_PLUS_EXACT_E0")
        for label,pass_list in (
            ("COMPOSE_4",base.FOUR),
            ("COMPOSE_14",base.EXTENDED),
            ("COMPOSE_19",base.EXPANDED),
        ):
            emit(label,union(split,["COMPOSE_"+p for p in pass_list]),
                 "CANONICAL_CSHARP_19_CELL_SQLITE_COMPOSITION")
        if chosen is not None:
            frozen=union(split,["COMPOSE_"+p for p in chosen])
            emit("COMPOSE_FROZEN_MIN_U_95_TRAIN",frozen,
                 "TRAIN_SELECTED_GLOBAL_95_COST_PRUNED_UNCHANGED_IN_VAL")
            ref=selected["measured"][split]
            if arms["COMPOSE_FROZEN_MIN_U_95_TRAIN"]["M"]!=ref["M"] or (
                arms["COMPOSE_FROZEN_MIN_U_95_TRAIN"]["U"]!=ref["U"]):
                raise ValueError("frozen TRAIN-selected M/U failed physical replay")
        results[split]={"all_no_cpf_truth_M":len(all_truth),
            "name_eligible_truth_M":len(eligible_truth),
            "name_eligible_observations":obs,
            "arms":arms}
    # Ranking is explicitly descriptive: no model or operational policy is
    # selected without a cost/FDR policy and calibrated C# Rank/Resolve.
    return {"version":VERSION,
       "selection_split":"TRAIN_ONLY",
       "source_normalization":"CSHARP_CANONICAL_FROZEN",
       "abc_fixed_pass_proxy_not_production_equivalence":True,
       "D0_D1_are_not_adaptive":True,
       "adaptive_strategy_executed_using_indexed_sql":True,
       "no_calibrated_FS_or_Rank_Resolve":True,
       "original_95pct_train_optimized_feasible":selected is not None,
       "results":results,
       "TEST_evaluated":False,"fdr_measured":False,
       "may_promote_policy":False,"may_select_strategy":False}

def run(snapshot,baseline_file,out,cap=CAP_LIMIT):
    snapshot,baseline_file,out=(Path(p).resolve()
                                 for p in (snapshot,baseline_file,out))
    if out.exists() or snapshot in out.parents or baseline_file==out:
        raise ValueError("cannot overwrite immutable source or report")
    abc,base,planner=declare()
    frozen=json.loads((snapshot/"manifest.json").read_text(encoding="utf8"))
    if (frozen.get("version")!="V9_CANONICAL_NAME_FREQUENCY_SNAPSHOT_V1"
       or frozen.get("projection_version")!=base.PROJECTION
       or frozen.get("frozen") is not True
       or frozen.get("test_used_for_frequency") is not False):
        raise ValueError("unknown C# frozen snapshot provenance")
    for name,field in (("canonical_observations.csv","frozen_source_sha256"),
        ("train_name_frequencies.json","frequency_sha256"),
        ("train_joint_name_frequencies.json","joint_frequency_sha256")):
        if base.digest(snapshot/name)!=frozen.get(field):
            raise ValueError("corrupted TRAIN-only immutable snapshot")
    baseline=json.loads(baseline_file.read_text(encoding="utf8"))
    if baseline.get("canonical_observations_sha256")!=frozen["frozen_source_sha256"]:
        raise ValueError("baseline source SHA differs from canonical snapshot")
    with (snapshot/"canonical_observations.csv").open(encoding="utf-8-sig",newline="") as f:
        rows=list(csv.DictReader(f))
    result=compare(rows,baseline,abc,base,planner,cap)
    result["canonical_observations_sha256"]=frozen["frozen_source_sha256"]
    result["baseline_report_sha256"]=base.digest(baseline_file)
    out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,indent=2,sort_keys=True,ensure_ascii=False)+"\n",
                   encoding="utf8")
    return result

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--snapshot-dir",required=True)
    p.add_argument("--baseline-report",required=True)
    p.add_argument("--out",required=True)
    p.add_argument("--cap",type=int,default=CAP_LIMIT)
    a=p.parse_args()
    r=run(a.snapshot_dir,a.baseline_report,a.out,a.cap)
    print(json.dumps({"version":r["version"],
           "arms":len(r["results"]["TRAIN"]["arms"])},sort_keys=True))
