#!/usr/bin/env python3
"""Compare clean versus correlated on identical reference/seed, distinct samples.

CLI accepts only aggregate SQLite reports + signed C# generation manifests;
TEST remains excluded from selection; synthetic data cannot certify city scale.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

VERSION="V9_SYNTHETIC_CLEAN_CORRELATED_SENSITIVITY_V1"
PROFILES=("clean","correlated")
PARTS=("TRAIN","VALIDATION")

# Seed-neutral source identity: input_fingerprint_sha256 intentionally embeds
# generator SEED and MUST NEVER be equated across seeds 42/43/44.
DEMOGRAPHIC_ID_KEYS=(
    "mode","person_geography",
    "person_source_physical_sha256","person_source_canonical_sha256",
    "mother_first_geography",
    "mother_first_source_physical_sha256","mother_first_source_canonical_sha256",
    "mother_surname_geography",
    "mother_surname_source_physical_sha256","mother_surname_source_canonical_sha256",
    "birth_schema_version","birth_source","birth_reference_period",
    "birth_geography","birth_sha256","birth_row_count"
)

def demographic_reference_identity(manifest):
    source=manifest.get("demographic_primary")
    if not isinstance(source,dict) or any(k not in source or source[k] is None
                                           for k in DEMOGRAPHIC_ID_KEYS):
        raise ValueError("incomplete seed-neutral demographic input references")
    if source.get("mode")!="DEMOGRAPHIC_PRIMARY_V1":
        raise ValueError("unsupported demographic source type")
    return {k:source[k] for k in DEMOGRAPHIC_ID_KEYS}


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
            eligible=data["name_eligible_truth_M"]
            observations=data["name_eligible_observations"]
            if (type(eligible) is not int or not 0<=eligible<=total
                or type(observations) is not int or observations<0):
                raise ValueError("invalid name eligibility denominator")
            expanded=data["metrics"]["UNION_EXPANDED"]
            fit=plan["plans"]["greedy_pruned"]
            def summary(m):
                if m is None:
                    return None
                if (type(m["M"]) is not int or type(m["U"]) is not int
                    or not 0<=m["M"]<=total or m["U"]<0):
                    raise ValueError("candidate M/U not valid")
                degree=m["u_degree"]
                values=(degree["mean"],degree["p95"],degree["p99"])
                if (any(type(v) not in (int,float) or not math.isfinite(v)
                        or v<0 for v in values)
                    or not values[1]<=values[2]):
                    raise ValueError("invalid U fanout distribution")
                return {"M":m["M"],"U":m["U"],"global_recall":m["M"]/total,
                        "mean_u_degree":values[0],
                        "p95_u_degree":values[1],
                        "p99_u_degree":values[2]}
            expanded_summary=summary(expanded)
            greedy_summary=summary(fit["measured"][part]) if fit else None
            confidence=(report["independent_base_identity_recall_confidence"]
                        [part]["greedy_pruned"]["lower_one_sided_95"]
                        if fit else None)
            if confidence is not None and (
                type(confidence) not in (int,float)
                or not math.isfinite(confidence)
                or not 0<=confidence<=1
                or confidence>greedy_summary["global_recall"]):
                raise ValueError("invalid bootstrap recall confidence bound")
            per_split[part]={
                "all_no_cpf_truth_M":total,
                "name_eligible_truth_M":data["name_eligible_truth_M"],
                "name_eligible_observations":data["name_eligible_observations"],
                "expanded_19_cells":expanded_summary,
                "TRAIN_selected_greedy_pruned":
                    greedy_summary,
                "bootstrap_lower_one_sided_95_greedy":
                    confidence
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
    source_demographic=demographic_reference_identity(manifests["clean"])
    if demographic_reference_identity(manifests["correlated"])!=source_demographic:
        raise ValueError("different seed-neutral demographic references")
    if summaries["clean"]["canonical_sha256"]==summaries["correlated"]["canonical_sha256"]:
        raise ValueError("clean and correlated cannot reuse same corpus")
    return {
        "version":VERSION,"seed":origin["clean"]["seed"],
        "source_reference_sha256":origin["clean"]["reference_fingerprint"],
        "seed_neutral_demographic_reference":source_demographic,
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
