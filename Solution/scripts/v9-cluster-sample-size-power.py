#!/usr/bin/env python3
"""V9: independent-base sample size precision, not synthetic 'perfect sample'.

Evaluate frozen TRAIN-fitted blocking on MATCH-BEARING BASE-IDENTITY clusters
in TRAIN and VALIDATION separately. TEST never analyzed. Subsample clusters
WITHOUT selecting parameters again; do not mistake same-seed subsamples for
independent repetitions or source population representativity.
"""
import argparse
import csv
import hashlib
import importlib.util
import json
import math
import random
from pathlib import Path

VERSION = "V9_CLUSTER_SAMPLE_SIZE_SENSITIVITY_V1"
SPLITS = ("TRAIN", "VALIDATION")
N_GRID = (250, 500, 1000, 2000, 5000, 10000, 20000)
Z_95 = 1.64485362695147
Z_POWER80 = 0.841621233572914

def load_baseline_module():
    path = Path(__file__).with_name("v9-sqlite-memory-cells.py")
    spec = importlib.util.spec_from_file_location("v9_frozen_sql_for_sample", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def planned_effective_pairs(target=0.95, plausible=(0.955, 0.96, 0.97, 0.98)):
    """Approximate effective INDEPENDENT true pairs for one-sided 5% test,
    80% power to detect p_true>0.95. No cluster / municipal correction implied."""
    if target != 0.95:
        raise ValueError("unapproved diagnostic null hypothesis")
    result = {}
    for p in plausible:
        if not target < p < 1:
            raise ValueError("alternative must exceed null")
        n = ((Z_95*math.sqrt(target*(1-target)) +
              Z_POWER80*math.sqrt(p*(1-p)))/(p-target))**2
        result[str(p)] = math.ceil(n)
    return result

def bootstrap_lower(clusters, rounds, seed):
    if rounds < 200 or rounds > 2000 or not clusters:
        raise ValueError("bootstrap needs >=200 replicates and true-M bases")
    n = len(clusters)
    rng = random.Random(seed)
    outcomes = []
    for _ in range(rounds):
        # Resample BASE-person clusters, not correlated observation pairs.
        indexes = rng.choices(range(n), k=n)
        total = sum(clusters[j][0] for j in indexes)
        hit = sum(clusters[j][1] for j in indexes)
        if total <= 0 or hit > total:
            raise ValueError("bootstrap denominator or hits invalid")
        outcomes.append(hit/total)
    outcomes.sort()
    return outcomes[max(0, math.ceil(rounds*.05)-1)]

def delta_effective_pairs(clusters):
    n=len(clusters)
    totals=sum(x[0] for x in clusters)
    hits=sum(x[1] for x in clusters)
    p=hits/totals
    if n < 2 or not (0 < p < 1):
        return None
    # Delta-method cluster-robust SE of the ratio SUM hits / SUM true pairs.
    resid_sum=sum((hit-p*total)**2 for total,hit in clusters)
    variance=n/(n-1)*resid_sum/(totals**2)
    if variance <= 0:
        return None
    eff=p*(1-p)/variance
    return {"effective_independent_true_pairs":eff,
            "paired_truth_design_effect":totals/eff,
            "estimate_cluster_se":math.sqrt(variance)}

def summary(clusters, rounds, seed):
    pairs=sum(t for t,h in clusters)
    recovered=sum(h for t,h in clusters)
    if not pairs or recovered>pairs:
        raise ValueError("missing or inconsistent M denominators")
    lower=bootstrap_lower(clusters, rounds, seed)
    return {"match_bearing_base_identities":len(clusters),
            "true_M_pairs":pairs,"retrieved_M":recovered,
            "global_recall":recovered/pairs,
            "lower_one_sided_95_identity_cluster_bootstrap":lower,
            "point_at_least_95":bool(20*recovered>=19*pairs),
            "cluster_lower_at_least_95":bool(lower>=.95),
            "delta_method_precision":delta_effective_pairs(clusters)}

def discover_clusters(rows, frozen_report, baseline):
    if (frozen_report.get("TEST_evaluated") is not False
        or frozen_report.get("selection_split")!="TRAIN_ONLY"
        or frozen_report.get("diagnostic_only") is not True
        or frozen_report.get("may_promote_policy") is not False):
        raise ValueError("invalid frozen TEST-blind baseline")
    fit=frozen_report.get("minimum_U_95pct_diagnostic",{})
    if fit.get("trained_on")!="TRAIN_ONLY" or fit.get("validation_refit") is not False:
        raise ValueError("baseline not TRAIN fitted")
    chosen=fit.get("plans",{}).get("greedy_pruned")
    if not isinstance(chosen,dict):
        raise ValueError("cannot assess non-feasible frozen plan")
    names=chosen.get("selected_cells")
    if (not isinstance(names,list) or not names or len(set(names))!=len(names)
        or not set(names).issubset(baseline.EXPANDED)):
        raise ValueError("missing/invalid selected indexed cell IDs")
    baseline.validate_rows(rows)
    for r in rows:
        if r["particao"]=="TEST":
            continue
        # Do not reveal observations or ID values in report.
    outcome={}
    for split in SPLITS:
        subset=[r for r in rows if r["particao"]==split]
        identity={r["observacao_id"]:r for r in subset}
        truth=baseline.truth_for(subset,False)
        observed=chosen["measured"][split]
        if len(truth)!=observed["all_no_cpf_truth_M"]:
            raise ValueError("canonical truth mismatch with frozen SQL report")
        required={i for pair in truth for i in pair}
        projected={}
        for oid in required:
            r=identity[oid]
            if (not baseline.meaningful(r["nome_normalizado"])
                or not (baseline.meaningful(r["nome_mae_normalizado"])
                        or baseline.usable_birth(r["data_nascimento"]) is not None)):
                projected[oid]=None
            else:
                features=baseline.keys(r)
                projected[oid]={name:set(features[name]) for name in names}
        grouped={}
        for a,b in truth:
            ra,rb=identity[a],identity[b]
            if ra["base_person_id"]!=rb["base_person_id"] or ra["gestor"]==rb["gestor"]:
                raise ValueError("true-M pair misclassified")
            base_id=ra["base_person_id"]
            if base_id not in grouped:
                grouped[base_id]=[0,0]
            one,two=projected[a],projected[b]
            match=bool(one is not None and two is not None and
                       any(one[cell]&two[cell] for cell in names))
            grouped[base_id][0]+=1
            grouped[base_id][1]+=int(match)
        if sum(v[1] for v in grouped.values())!=observed["M"]:
            raise ValueError("truth recovery does not match actual frozen indexed SQL")
        outcome[split]=grouped
    return names,outcome

def analyze(rows, frozen_report, baseline, source_sha, seed=42, rounds=400):
    if (not isinstance(source_sha,str) or len(source_sha)!=64
        or source_sha!=frozen_report.get("canonical_observations_sha256")):
        raise ValueError("canonical source SHA mismatch")
    if type(seed) is not int or seed < 0 or seed > 100000:
        raise ValueError("invalid frozen generation seed")
    cells,clusters=discover_clusters(rows,frozen_report,baseline)
    results={}
    for split in SPLITS:
        group=clusters[split]
        # SHA ordering produces deterministic NESTED subsamples, no TEST
        # and no cherry-picking based on ground truth or candidate outcome.
        ranked=sorted(group,key=lambda base_id:
            hashlib.sha256(f"{seed}|{split}|{base_id}".encode()).digest())
        sizes=[n for n in N_GRID if n<len(ranked)]+[len(ranked)]
        samples=[]
        for index,n in enumerate(sizes):
            vectors=[tuple(group[k]) for k in ranked[:n]]
            measurements=summary(vectors,rounds,seed+index+(
                1 if split=="TRAIN" else 10001))
            measurements["population_sample_kind"]="MATCH_BEARING_BASE_IDENTITIES_NOT_OBSERVATIONS"
            measurements["within_same_corpus_not_independent_seed"]=True
            samples.append(measurements)
        results[split]={
            "total_match_bearing_identities":len(ranked),
            "sample_sizes":samples,
            "bootstrap_resamples":rounds,
            "TEST_used_for_selection_or_precision":False}
    return {
        "version":VERSION, "canonical_observations_sha256":source_sha,
        "generation_seed":seed,"frozen_selected_cells":cells,
        "split_results":results,
        "h0_global_recall_at_most_95":True,
        "alpha_one_sided":.05,"nominal_power":.80,
        "effective_independent_pairs_required_if_true_recall":
            planned_effective_pairs(),
        "independent_repetitions_measured":1,
        "sampling_scope":"WITHIN_FROZEN_SYNTHETIC_CORPUS",
        "not_population_representative":True,
        "TEST_evaluated":False,"fdr_measured":False,
        "may_promote_policy":False,
        "sql_server_latency_measured":False,
        "perfect_sample_claimed":False,
        "warning":"30k population identities are not 30k independent cross-manager matched clusters; subsamples share one synthetic seed and cannot certify São Paulo or true error distribution."
    }

def run(snapshot, report, output, seed=42, rounds=400):
    snapshot,report,output=map(lambda p:Path(p).resolve(),
                               (snapshot,report,output))
    if output.exists() or output==report or output==snapshot or snapshot in output.parents:
        raise ValueError("never overwrite source or evidence")
    frozen=json.loads((snapshot/"manifest.json").read_text(encoding="utf8"))
    from_baseline=load_baseline_module()
    if (frozen.get("version")!="V9_CANONICAL_NAME_FREQUENCY_SNAPSHOT_V1"
        or frozen.get("projection_version")!=from_baseline.PROJECTION
        or frozen.get("test_used_for_frequency") is not False):
        raise ValueError("invalid frozen TRAIN-sourced projection")
    source=snapshot/"canonical_observations.csv"
    if from_baseline.digest(source)!=frozen.get("frozen_source_sha256"):
        raise ValueError("source fingerprint mismatch")
    evidence=json.loads(report.read_text(encoding="utf8"))
    with source.open(encoding="utf-8-sig",newline="") as inp:
        rows=list(csv.DictReader(inp))
    result=analyze(rows,evidence,from_baseline,frozen["frozen_source_sha256"],
                   seed,rounds)
    result["frozen_manifest_sha256"]=from_baseline.digest(snapshot/"manifest.json")
    result["source_report_sha256"]=from_baseline.digest(report)
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,ensure_ascii=False,sort_keys=True,indent=2)+"\n",
                      encoding="utf8")
    return result

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--snapshot-dir",required=True)
    p.add_argument("--baseline-report",required=True)
    p.add_argument("--out",required=True)
    p.add_argument("--seed",type=int,required=True)
    p.add_argument("--bootstrap-replicates",type=int,default=400)
    a=p.parse_args()
    r=run(a.snapshot_dir,a.baseline_report,a.out,a.seed,a.bootstrap_replicates)
    print(json.dumps({k:r[k] for k in ("version","generation_seed",
                                     "frozen_selected_cells")},
                     sort_keys=True))
