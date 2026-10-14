#!/usr/bin/env python3
"""Cross-seed out-of-sample replay of a seed-42 TRAIN-fitted blocking plan.

SEED 43/44 must NEVER fit or modify the source plan. Evaluate exactly the
same frozen SQL composite-key cells against the target's C# canonical CSV.
No TEST, no accepted-link FDR, no municipality extrapolation or promotion.
"""
import argparse
import csv
import importlib.util
import json
import sqlite3
from pathlib import Path

VERSION="V9_FROZEN_SEED42_PLAN_CROSS_SEED_SQL_REPLAY_V1"
PROFILES=("clean","correlated")
SPLITS=("TRAIN","VALIDATION")
CAP_LIMIT=1000000


def loader():
    file=Path(__file__).with_name("v9-sqlite-memory-cells.py")
    spec=importlib.util.spec_from_file_location("v9_transfer_baseline",file)
    v=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(v)
    return v


def physical_frozen_cells(rows,names,base,cap):
    """Index only previously frozen cells, SQL join under deterministic cap."""
    if type(cap) is not int or not 1<=cap<=CAP_LIMIT:
        raise ValueError("invalid candidate cap")
    base.validate_rows(rows)
    con=sqlite3.connect(":memory:")
    try:
        con.executescript("""
            CREATE TABLE obs(oid TEXT PRIMARY KEY COLLATE BINARY,
                             manager TEXT NOT NULL COLLATE BINARY);
            CREATE TABLE k(cell TEXT NOT NULL COLLATE BINARY,
                           part TEXT NOT NULL COLLATE BINARY,
                           composite TEXT NOT NULL COLLATE BINARY,
                           oid TEXT NOT NULL COLLATE BINARY,
                           PRIMARY KEY(cell,part,composite,oid)) WITHOUT ROWID;
        """)
        observations=[]
        indexed=[]
        for r in rows:
            if r["particao"] not in SPLITS or r["cpf"].strip():
                continue
            if not (base.meaningful(r["nome_normalizado"]) and
                    (base.meaningful(r["nome_mae_normalizado"]) or
                     base.usable_birth(r["data_nascimento"]) is not None)):
                continue
            oid=r["observacao_id"]
            observations.append((oid,r["gestor"]))
            keys=base.keys(r)
            for name in names:
                for val in keys[name]:
                    indexed.append((name,r["particao"],val,oid))
        con.executemany("INSERT INTO obs VALUES(?,?)",observations)
        con.executemany("INSERT INTO k VALUES(?,?,?,?)",indexed)
        query="""SELECT a.oid,b.oid FROM k a
                 JOIN k b ON b.cell=a.cell AND b.part=a.part
                    AND b.composite=a.composite AND b.oid>a.oid
                 JOIN obs oa ON oa.oid=a.oid
                 JOIN obs ob ON ob.oid=b.oid
                 WHERE a.cell=? AND a.part=? AND oa.manager<>ob.manager
                 LIMIT ?"""
        pairs={}
        for split in SPLITS:
            for name in names:
                candidate=set(con.execute(query,(name,split,cap+1)))
                if len(candidate)>cap:
                    raise ValueError("frozen cell exceeds physical cap; never truncate")
                pairs[split,name]=candidate
        return pairs
    finally:
        con.close()


def compare(source, target_report, target_manifest, target_frozen,
            rows, profile, seed, base, cap=CAP_LIMIT):
    if (profile not in PROFILES or type(seed) is not int
            or seed not in (43,44) or set(source.get("profiles",{}))!=set(PROFILES)
            or source.get("seed")!=42 or source.get("TEST_evaluated") is not False
            or source.get("may_promote_v9") is not False):
        raise ValueError("invalid source TRAIN configuration or source seed")
    origin=source["profiles"][profile]
    names=origin.get("greedy_train_cells")
    if not (isinstance(names,list) and names and len(names)==len(set(names))
            and set(names).issubset(base.EXPANDED)):
        raise ValueError("missing valid 42-TRAIN frozen selected plan")
    if (target_manifest.get("error_profile")!=profile
            or target_manifest.get("seed")!=seed
            or target_manifest.get("input_fingerprint_sha256")!=source.get("source_reference_sha256")
            or target_manifest.get("error_profile_provenance")!=
                "HYPOTHETICAL_UNCALIBRATED_SYNTHETIC_STRESS_PROFILE"):
        raise ValueError("wrong profile, seed or IBGE demographic source")
    if (target_report.get("TEST_evaluated") is not False
            or target_report.get("selection_split")!="TRAIN_ONLY"
            or target_report.get("may_promote_policy") is not False
            or target_report.get("fdr_measured") is not False
            or target_frozen.get("frozen_source_sha256") !=
               target_report.get("canonical_observations_sha256")
            or target_frozen.get("test_used_for_frequency") is not False
            or target_frozen.get("projection_version")!=base.PROJECTION
            or origin.get("canonical_sha256")==target_report.get("canonical_observations_sha256")):
        raise ValueError("source/target identity, partition or snapshot contract invalid")
    cells=physical_frozen_cells(rows,names,base,cap)
    measures={}
    for split in SPLITS:
        part_rows=[r for r in rows if r["particao"]==split]
        full=base.truth_for(part_rows,False)
        eligible=base.truth_for(part_rows,True)
        eligible_obs=sum(not r["cpf"].strip()
                          and base.meaningful(r["nome_normalizado"])
                          and (base.meaningful(r["nome_mae_normalizado"])
                               or base.usable_birth(r["data_nascimento"]) is not None)
                          for r in part_rows)
        frozen_pairs=set().union(*(cells[split,n] for n in names))
        if (len(full)!=target_report["results"][split]["all_no_cpf_truth_M"]
            or len(eligible)!=target_report["results"][split]["name_eligible_truth_M"]
            or eligible_obs!=target_report["results"][split]["name_eligible_observations"]):
            raise ValueError("target M / eligible frame disagrees with original indexed SQL")
        for cell in names:
            # Independent recomputation of each physical indexed pass MUST agree
            # with the target's full baseline SQL diagnostic (including U).
            stats=target_report["results"][split]["cells"][cell]
            physical=cells[split,cell]
            m=len(physical & eligible)
            if stats["M"]!=m or stats["U"]!=len(physical)-m:
                raise ValueError("frozen replay diverges from indexed SQL target cell")
        metrics=base.measure(frozen_pairs,eligible,eligible_obs)
        confidence=base.cluster_bootstrap_global_recall(
            part_rows,full,frozen_pairs,seed=12000+seed*10+(1 if split=="VALIDATION" else 0))
        measures[split]={
            "all_no_cpf_truth_M":len(full),
            "name_eligible_truth_M":len(eligible),
            "name_eligible_observations":eligible_obs,
            "M":metrics["M"],"U":metrics["U"],
            "global_M_recall":metrics["M"]/len(full),
            "u_degree":metrics["u_degree"],
            "cluster_bootstrap":confidence,
            "target_train_fitted_plan_reference_only":{
                "M":(target_report["minimum_U_95pct_diagnostic"]["plans"]["greedy_pruned"]
                     ["measured"][split]["M"]
                     if target_report["minimum_U_95pct_diagnostic"]["plans"].get("greedy_pruned")
                     else None),
                "feasible_in_target_train":(
                    target_report["minimum_U_95pct_diagnostic"]["plans"].get("greedy_pruned")
                    is not None)}
        }
    return {"version":VERSION,"seed_source":42,"seed_target":seed,
            "profile":profile,"original_source_canonical_sha256":origin["canonical_sha256"],
            "target_canonical_sha256":target_frozen["frozen_source_sha256"],
            "ibge_reference_sha256":source["source_reference_sha256"],
            "frozen_SOURCE_TRAIN_selected_cells":names,
            "target_fit_used_to_select_cells":False,
            "separate_target_train_and_validation_measurements":measures,
            "TEST_evaluated":False,
            "accepted_links_or_fdr_measured":False,
            "sql_server_latency_measured":False,
            "no_real_municipal_error_rate_claim":True,
            "may_promote_policy":False}


def run(source_file,profile_root,out,seed,cap=CAP_LIMIT):
    source_file=Path(source_file).resolve()
    profile_root=Path(profile_root).resolve()
    out=Path(out).resolve()
    if out.exists() or out==source_file or out==profile_root or profile_root in out.parents:
        raise ValueError("never overwrite immutable inputs")
    base=loader()
    source=json.loads(source_file.read_text(encoding="utf8"))
    result={}
    for profile in PROFILES:
        root=profile_root/profile
        report_file=root/"sqlite-report.json"
        frozen=root/"frozen"/"manifest.json"
        canonical=root/"frozen"/"canonical_observations.csv"
        generation=root/"generation-manifest.json"
        baseline=json.loads(report_file.read_text(encoding="utf8"))
        manifest=json.loads(frozen.read_text(encoding="utf8"))
        if base.digest(canonical)!=manifest.get("frozen_source_sha256"):
            raise ValueError("target frozen observations changed")
        with canonical.open(encoding="utf-8-sig",newline="") as f:
            rows=list(csv.DictReader(f))
        result[profile]=compare(
            source,baseline,json.loads(generation.read_text(encoding="utf8")),
            manifest,rows,profile,seed,base,cap=cap)
    output={"version":VERSION,"seed_target":seed,
            "comparison_kind":"42_FROZEN_TRAIN_SELECTED_PLAN_REPLAYED_ON_INDEPENDENT_SEEDS",
            "profiles":result,"TEST_evaluated":False,
            "recalibration_or_activation":False}
    out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(output,sort_keys=True,indent=2,ensure_ascii=False)+"\n",
                   encoding="utf8")
    return output


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--seed42-aggregate",required=True)
    parser.add_argument("--target-profile-root",required=True)
    parser.add_argument("--target-seed",type=int,required=True)
    parser.add_argument("--cap",type=int,default=CAP_LIMIT)
    parser.add_argument("--out",required=True)
    args=parser.parse_args()
    report=run(args.seed42_aggregate,args.target_profile_root,args.out,args.target_seed,args.cap)
    print(json.dumps({"version":VERSION,"seed_target":report["seed_target"],
                      "profiles":list(report["profiles"])},sort_keys=True))
