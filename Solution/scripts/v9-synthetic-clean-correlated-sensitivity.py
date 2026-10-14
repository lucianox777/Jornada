#!/usr/bin/env python3
"""Compare clean versus correlated on identical reference/seed, distinct samples.

CLI accepts only aggregate SQLite reports + signed C# generation manifests;
TEST remains excluded from selection; synthetic data cannot certify city scale.
"""
import argparse
import hashlib
import json
from pathlib import Path

VERSION="V9_SYNTHETIC_CLEAN_CORRELATED_SENSITIVITY_V1"
PROFILES=("clean","correlated")
PARTS=("TRAIN","VALIDATION")

def compare(reports,manifests):
    if set(reports)!=set(PROFILES) or set(manifests)!=set(PROFILES):
        raise ValueError("both predefined profiles required, no partial comparison")
    origin={}
    summaries={}
    for profile in PROFILES:
        report,manifest=reports[profile],manifests[profile]
        if manifest.get("error_profile")!=profile or (
            manifest.get("error_profile_provenance")!=
            "HYPOTHETICAL_UNCALIBRATED_SYNTHETIC_STRESS_PROFILE"):
            raise ValueError("synthetic profile not bound in C# manifest")
        if (report.get("selection_split")!="TRAIN_ONLY"
            or report.get("TEST_evaluated") is not False
            or report.get("fdr_measured") is not False
            or report.get("may_promote_policy") is not False
            or report.get("sql_server_p95_measured") is not False):
            raise ValueError("invalid TEST-blind, diagnostics-only SQL evidence")
        for k in ("seed","input_fingerprint_sha256","ruleset_version",
                  "rng_version","population_profile","demographic_primary"):
            if k not in manifest:
                raise ValueError("missing generation provenance "+k)
        origin[profile]={
            "seed":manifest["seed"],
            "reference_fingerprint":manifest["input_fingerprint_sha256"],
            "ruleset_version":manifest["ruleset_version"],
            "rng_version":manifest["rng_version"],
            "population_profile":manifest["population_profile"],
            "demographic_reference":manifest["demographic_primary"]
        }
        plan=report.get("minimum_U_95pct_diagnostic",{})
        if plan.get("trained_on")!="TRAIN_ONLY" or plan.get("validation_refit") is not False:
            raise ValueError("profile-specific TRAIN-only plan missing")
        per_split={}
        for part in PARTS:
            data=report["results"][part]
            total=data["all_no_cpf_truth_M"]
            if type(total) is not int or total<=0:
                raise ValueError("no no-CPF truth denominator")
            expanded=data["metrics"]["UNION_EXPANDED"]
            fit=plan["plans"]["greedy_pruned"]
            def summary(m):
                if m is None:
                    return None
                if not (0<=m["M"]<=total and m["U"]>=0):
                    raise ValueError("candidate M/U not valid")
                return {"M":m["M"],"U":m["U"],"global_recall":m["M"]/total,
                        "mean_u_degree":m["u_degree"]["mean"],
                        "p95_u_degree":m["u_degree"]["p95"],
                        "p99_u_degree":m["u_degree"]["p99"]}
            per_split[part]={
                "all_no_cpf_truth_M":total,
                "name_eligible_truth_M":data["name_eligible_truth_M"],
                "name_eligible_observations":data["name_eligible_observations"],
                "expanded_19_cells":summary(expanded),
                "TRAIN_selected_greedy_pruned":
                    summary(fit["measured"][part]) if fit else None,
                "bootstrap_lower_one_sided_95_greedy":
                    report["independent_base_identity_recall_confidence"][part]["greedy_pruned"]["lower_one_sided_95"]
                    if fit else None
            }
        summaries[profile]={
            "canonical_sha256":report["canonical_observations_sha256"],
            "generation_seed":manifest["seed"],
            "observed":per_split,
            "greedy_train_cells":
                plan["plans"]["greedy_pruned"]["selected_cells"]
                if plan["plans"]["greedy_pruned"] else None
        }
    if (origin["clean"]["seed"]!=origin["correlated"]["seed"]
        or origin["clean"]["reference_fingerprint"]!=origin["correlated"]["reference_fingerprint"]
        or origin["clean"]["ruleset_version"]!=origin["correlated"]["ruleset_version"]
        or origin["clean"]["rng_version"]!=origin["correlated"]["rng_version"]
        or origin["clean"]["demographic_reference"]!=origin["correlated"]["demographic_reference"]
        or origin["clean"]["population_profile"]!=origin["correlated"]["population_profile"]):
        raise ValueError("clean and correlated use different reference or seed")
    if summaries["clean"]["canonical_sha256"]==summaries["correlated"]["canonical_sha256"]:
        raise ValueError("clean and correlated cannot reuse same corpus")
    return {
        "version":VERSION,"seed":origin["clean"]["seed"],
        "source_reference_sha256":origin["clean"]["reference_fingerprint"],
        "profiles":summaries,
        "comparison_kind":"UNPAIRED_SAME_SEED_AND_DEMOGRAPHIC_REFERENCE",
        "WARNING":"RNG branch consumption differs; neither matched persons nor real municipal error rates.",
        "TEST_evaluated":False,"may_promote_v9":False,
        "statistically_certifies_95":False,"fdr_measured":False,
        "sql_server_latency_measured":False,"real_population_certified":False
    }

def run(root,out):
    root,out=Path(root).resolve(),Path(out).resolve()
    if out.exists() or root==out or root in out.parents:
        raise ValueError("report must be new and outside source directories")
    inputs={}
    manifests={}
    fingerprints={}
    for profile in PROFILES:
        base=root/profile
        b=base/"generation-manifest.json"
        r=base/"sqlite-report.json"
        manifests[profile]=json.loads(b.read_text(encoding="utf8"))
        inputs[profile]=json.loads(r.read_text(encoding="utf8"))
        fingerprints[profile]={
            "manifest_sha256":hashlib.sha256(b.read_bytes()).hexdigest(),
            "sqlite_report_sha256":hashlib.sha256(r.read_bytes()).hexdigest()}
    result=compare(inputs,manifests)
    result["input_file_sha256"]=fingerprints
    out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,indent=2,sort_keys=True,ensure_ascii=False)+"\n",
                   encoding="utf8")
    return result

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--profile-root",required=True)
    p.add_argument("--out",required=True)
    a=p.parse_args()
    result=run(a.profile_root,a.out)
    print(json.dumps({"version":result["version"],"seed":result["seed"],
                      "profiles":list(result["profiles"])},sort_keys=True))
