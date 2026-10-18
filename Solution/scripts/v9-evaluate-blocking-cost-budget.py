#!/usr/bin/env python3
"""Fail-closed V9 observed candidate-cost budget over aggregate SQLite evidence.

This is NOT a deployment, FDR, scale-extrapolation or SQL Server gate.
Budgets are externally supplied, explicit and source-SHA-bound; no implicit
permissive threshold. TEST must be blind. Only TRAIN-fitted fixed plans qualify.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

VERSION = "V9_OBSERVED_COST_BUDGET_DIAGNOSTIC_V1"
PLAN_NAMES = ("greedy_pruned", "exact_targeted")
SPLITS = ("TRAIN", "VALIDATION")
BUDGETS = ("max_u_degree_mean", "max_u_degree_p95", "max_u_degree_p99")

def number(value, label):
    if type(value) not in (float, int) or not math.isfinite(value) or value < 0:
        raise ValueError("invalid nonnegative budget/measurement: " + label)
    return value

def assess(report, policy):
    if not isinstance(report, dict) or not isinstance(policy, dict):
        raise ValueError("invalid JSON objects")
    if (report.get("engine") != "sqlite3_memory_real_indexed_sql"
        or not report.get("diagnostic_only")
        or report.get("may_promote_policy") is not False
        or report.get("TEST_evaluated") is not False
        or report.get("fdr_measured") is not False
        or report.get("sql_server_p95_measured") is not False
        or report.get("selection_split") != "TRAIN_ONLY"):
        raise ValueError("unverified diagnostic-only, TEST-blind SQLite provenance")
    if (policy.get("version") != VERSION
        or (policy.get("minimum_global_M_recall") is not None
            and (type(policy["minimum_global_M_recall"]) not in (float, int)
                 or not 0 <= policy["minimum_global_M_recall"] <= 1))
        or policy.get("source_sha256") != report.get("canonical_observations_sha256")
        or not isinstance(policy.get("source_sha256"), str)
        or len(policy["source_sha256"]) != 64):
        raise ValueError("missing explicit versioned budget or canonical SHA binding")
    if set(policy) not in ({"version", "source_sha256", *BUDGETS},
                           {"version", "minimum_global_M_recall", "source_sha256", *BUDGETS}):
        raise ValueError("unknown or missing budget fields")
    for name in BUDGETS:
        number(policy[name], name)
    plans = report.get("minimum_U_95pct_diagnostic")
    confidence = report.get("independent_base_identity_recall_confidence")
    by_part = report.get("results")
    if (not isinstance(plans, dict)
        or plans.get("trained_on") != "TRAIN_ONLY"
        or plans.get("validation_refit") is not False
        or plans.get("target_is_diagnostic_not_promotion") is not True
        or not isinstance(confidence, dict)
        or not isinstance(by_part, dict)
        or set(by_part) != set(SPLITS)
        or set(confidence) != set(SPLITS)):
        raise ValueError("missing frozen TRAIN-only split contract")
    measured = plans.get("plans")
    if not isinstance(measured, dict) or not all(n in measured for n in PLAN_NAMES):
        raise ValueError("missing TRAIN-selected plans")
    output = {}
    for plan in PLAN_NAMES:
        item = measured[plan]
        if item is None:
            output[plan] = {"status": "NO_TRAIN_FEASIBLE_PLAN", "passes": None}
            continue
        selected = item.get("selected_cells")
        if (not isinstance(selected, list) or not selected
            or len(set(selected)) != len(selected)):
            raise ValueError("invalid frozen pass list")
        splits = {}
        for split in SPLITS:
            entry = item["measured"][split]
            ci = confidence[split][plan]
            scope = by_part[split]
            all_m = scope.get("all_no_cpf_truth_M")
            eligible = scope.get("name_eligible_truth_M")
            count = scope.get("name_eligible_observations")
            if not all(type(k) is int and k > 0 for k in (all_m, eligible, count)):
                raise ValueError("missing total truth or eligible denominator: " + split)
            if eligible > all_m or type(entry["M"]) is not int or type(entry["U"]) is not int:
                raise ValueError("invalid M/U frame")
            m, u = entry["M"], entry["U"]
            if not (0 <= m <= eligible and u >= 0):
                raise ValueError("invalid retrieved pairs")
            if entry.get("all_no_cpf_truth_M") != all_m:
                raise ValueError("global 95% denominator mismatch")
            if entry.get("global_recall_target_met") != (20*m >= 19*all_m):
                raise ValueError("point recall status inconsistent")
            if (not isinstance(ci, dict)
                or ci.get("method") != "NONPARAMETRIC_IDENTITY_CLUSTER_BOOTSTRAP_V1"
                or ci.get("population_or_fdr_certification") is not False):
                raise ValueError("missing independent-base uncertainty")
            point = number(ci.get("point_recall"), "cluster point recall")
            lower = number(ci.get("lower_one_sided_95"), "cluster lower bound")
            if point > 1 or lower > 1 or abs(point - m/all_m) > 1e-10:
                raise ValueError("confidence point estimate does not reconcile")
            degree = entry.get("u_degree", {})
            mean = number(degree.get("mean"), "u degree mean")
            p95 = number(degree.get("p95"), "u degree p95")
            p99 = number(degree.get("p99"), "u degree p99")
            if (abs(mean - 2*u/count) > 1e-9
                or p95 > p99 or p99 > number(degree.get("max"), "u degree max")):
                raise ValueError("U degree must count BOTH endpoints and include zero degree")
            failed = []
            # Optional, versioned policy floor; reported legacy 95% flags remain
            # verified for arithmetic consistency, but are not universal gates.
            recall_floor = policy.get("minimum_global_M_recall")
            if recall_floor is not None:
                if m / all_m < recall_floor:
                    failed.append("GLOBAL_POINT_RECALL_BELOW_POLICY")
                if lower < recall_floor:
                    failed.append("CLUSTER_LOWER_RECALL_BELOW_POLICY")
            if ci.get("statistically_supported_global_95") != (lower >= 0.95):
                raise ValueError("bootstrap support flag inconsistent")
            for name, value in (("max_u_degree_mean",mean),
                                ("max_u_degree_p95",p95),
                                ("max_u_degree_p99",p99)):
                if value > policy[name]:
                    failed.append(name.upper() + "_EXCEEDED")
            splits[split] = {
                "status": "OBSERVED_DIAGNOSTIC_PASS" if not failed else "OBSERVED_DIAGNOSTIC_FAIL",
                "reasons": failed, "M": m, "all_no_cpf_truth_M": all_m,
                "U": u, "global_M_recall": m/all_m,
                "cluster_lower_one_sided_95": lower,
                "u_degree_mean": mean, "u_degree_p95": p95,
                "u_degree_p99": p99
            }
        success = all(x["status"] == "OBSERVED_DIAGNOSTIC_PASS" for x in splits.values())
        output[plan] = {
            "status": ("OBSERVED_DIAGNOSTIC_BUDGET_MET" if success
                       else "OBSERVED_DIAGNOSTIC_BUDGET_UNMET"),
            "passes": selected, "splits": splits
        }
    return {
        "version": VERSION, "budget_scope": "OBSERVED_SYNTHETIC_TRAIN_VALIDATION_ONLY",
        "source_sha256": policy["source_sha256"],
        "budget": {name: policy[name] for name in BUDGETS},
        "optional_recall_floor": policy.get("minimum_global_M_recall"),
        "plans": output, "TEST_evaluated": False,
        "may_promote_policy": False, "fdr_measured": False,
        "sql_server_latency_measured": False, "city_scale_certified": False,
        "warning": "No policy promotion even if diagnostic budgets are met; SQL Server and independent real-city evidence are required."
    }

def run(report_path, budget_path, output_path):
    report_path = Path(report_path).resolve()
    budget_path = Path(budget_path).resolve()
    output_path = Path(output_path).resolve()
    if output_path in (report_path, budget_path) or output_path.exists():
        raise ValueError("never overwrite evidence, budget or existing diagnostic")
    report = json.loads(report_path.read_text(encoding="utf-8"))
    policy = json.loads(budget_path.read_text(encoding="utf-8"))
    result = assess(report, policy)
    result["report_sha256"] = hashlib.sha256(report_path.read_bytes()).hexdigest()
    result["budget_sha256"] = hashlib.sha256(budget_path.read_bytes()).hexdigest()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(result, sort_keys=True, indent=2)+"
",
                           encoding="utf-8")
    return result

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--report", required=True, help="aggregate, frozen SQLite V9 report")
    p.add_argument("--budget", required=True, help="explicit SHA-bound experimental budget JSON")
    p.add_argument("--out", required=True)
    a = p.parse_args()
    print(json.dumps(run(a.report, a.budget, a.out), sort_keys=True))
