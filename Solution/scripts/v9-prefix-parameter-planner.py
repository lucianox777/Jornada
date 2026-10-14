#!/usr/bin/env python3
"""TRAIN-only, budget-conditional prefix parameter discovery on physical SQLite pairs.

No arbitrary best first-name prefix. Produce a Pareto frontier from deduplicated
pairs added to the CURRENT C# 10-pass planner, then optionally freeze the optimal
single prefix variation under an externally supplied U/fanout budget. VALIDATION
and TEST cannot influence selection. This is a diagnostic, not runtime behavior.
"""
import math

VERSION = "V9_TRAIN_ONLY_PREFIX_PARAMETER_FRONTIER_V1"
SPLITS = ("TRAIN", "VALIDATION")
METRICS = ("max_incremental_u_train", "max_u_degree_p95_train",
           "max_u_degree_p99_train")


def validated_budget(policy, expected_sha):
    if policy is None:
        return None
    if not isinstance(policy, dict) or set(policy) != {
        "version", "canonical_sha256", "minimum_global_point_recall",
        *METRICS
    } or policy["version"] != VERSION or policy["canonical_sha256"] != expected_sha:
        raise ValueError("prefix-budget must be explicit, versioned, and SHA-bound")
    if type(policy["minimum_global_point_recall"]) not in (float,int) or (
        policy["minimum_global_point_recall"] != 0.95):
        raise ValueError("unapproved recall threshold")
    for key in METRICS:
        value = policy[key]
        if type(value) not in (float,int) or not math.isfinite(value) or value < 0:
            raise ValueError("invalid selected-prefix budget: "+key)
    return policy


def choose_under_budget(training, policy):
    """Select ONE add-on from TRAIN Pareto; maximize M, minimize U and fanout."""
    if policy is None:
        return None
    eligible = [
        (name, item) for name, item in training.items()
        if item["status"] == "MEASURED"
        and item["delta_M"] > 0
        and item["delta_U"] <= policy["max_incremental_u_train"]
        and item["u_degree_p95"] <= policy["max_u_degree_p95_train"]
        and item["u_degree_p99"] <= policy["max_u_degree_p99_train"]
        and item["global_point_recall"] >= policy["minimum_global_point_recall"]
    ]
    if not eligible:
        return None
    # Training evidence only, no Validation peeking.
    return min(eligible, key=lambda z: (-z[1]["M"], z[1]["U"],
                                        z[1]["u_degree_p99"],
                                        z[1]["u_degree_p95"], z[0]))[0]


def frontier(train):
    """Nondominated cost/recall frontier of ONE additional indexed key."""
    candidates = sorted(
        ((name, item) for name, item in train.items()
         if item["status"] == "MEASURED" and item["delta_M"] > 0),
        key=lambda z: (z[1]["delta_U"],-z[1]["delta_M"],z[0]))
    output=[]
    max_recall_gain=-1
    for name, item in candidates:
        gain=item["delta_M"]
        if gain > max_recall_gain:
            output.append(name)
            max_recall_gain = gain
    return output


def evaluate(rows, base_module, selected_base_cells, physical_base,
             physical_prefix, prefix_names, source_sha, policy=None):
    """Train on actual measured candidate pair SETS; evaluate VAL without refit.

    Args physical_* maps (split, name) -> set of deduplicated SQL pair IDs.
    The base selected pass list must have been fitted and frozen on TRAIN in the
    original corpus reporter. No learning on VALIDATION or reference TEST.
    """
    policy=validated_budget(policy,source_sha)
    if not isinstance(source_sha,str) or len(source_sha)!=64:
        raise ValueError("missing original frozen canonical SHA")
    if (not selected_base_cells or not isinstance(selected_base_cells,list)
        or len(set(selected_base_cells))!=len(selected_base_cells)
        or not set(selected_base_cells).issubset(base_module.EXPANDED)):
        raise ValueError("invalid frozen original 10-pass TRAIN plan")
    if len(set(prefix_names))!=len(prefix_names) or not prefix_names:
        raise ValueError("invalid preregistered prefix variants")
    by_split, baseline_sets, truth_sets = {}, {}, {}
    for split in SPLITS:
        subset=[r for r in rows if r["particao"]==split]
        if not subset:
            raise ValueError("missing split "+split)
        all_truth=base_module.truth_for(subset,False)
        eligible=base_module.truth_for(subset,True)
        obs=sum(not r["cpf"].strip() and
                base_module.meaningful(r["nome_normalizado"]) and
                (base_module.meaningful(r["nome_mae_normalizado"]) or
                 base_module.usable_birth(r["data_nascimento"]) is not None)
                for r in subset)
        if not all_truth or not eligible or not obs:
            raise ValueError("missing truth/frame")
        original=[physical_base[split,c] for c in selected_base_cells]
        anchor=set().union(*original)
        if not anchor.issubset(set().union(
                *(physical_base[split,c] for c in base_module.EXPANDED))):
            raise ValueError("base plan outside original indexed cells")
        baseline_sets[split]=anchor
        truth_sets[split]=(all_truth,eligible,obs,subset)
        base_stats=base_module.measure(anchor,eligible,obs)
        prefix={}
        for name in prefix_names:
            cell=physical_prefix.get((split,name))
            if cell is None:
                prefix[name]={"status":"CAP_EXCEEDED_UNMEASURED","M":None,"U":None,
                              "delta_M":None,"delta_U":None}
                continue
            union=anchor|cell
            stats=base_module.measure(union,eligible,obs)
            extra=cell-anchor
            dm=len(extra & eligible)
            du=len(extra)-dm
            if (stats["M"]!=base_stats["M"]+dm
                or stats["U"]!=base_stats["U"]+du):
                raise ValueError("nonreconciling marginal SQL candidate pairs")
            prefix[name]={
                "status":"MEASURED", "M":stats["M"],"U":stats["U"],
                "delta_M":dm,"delta_U":du,
                "global_point_recall":stats["M"]/len(all_truth),
                "global_M_without_CPF":len(all_truth),
                "u_degree_p95":stats["u_degree"]["p95"],
                "u_degree_p99":stats["u_degree"]["p99"],
                "u_degree_mean":stats["u_degree"]["mean"],
                "eligible_observations":obs
            }
        by_split[split]={
            "all_no_cpf_truth_M":len(all_truth),
            "name_eligible_truth_M":len(eligible),
            "original_baseline_M":base_stats["M"],
            "original_baseline_U":base_stats["U"],
            "variants":prefix
        }
    train=by_split["TRAIN"]["variants"]
    pareto=frontier(train)
    selected=choose_under_budget(train,policy)
    # Training-only frontier is frozen; VALIDATION metrics are displayed without
    # allowing the optimizer to change the selected configurations.
    frozen_candidates={
        name:{"TRAIN":train[name],"VALIDATION":by_split["VALIDATION"]["variants"][name]}
        for name in pareto
    }
    selected_validation=(by_split["VALIDATION"]["variants"].get(selected)
                         if selected is not None else None)
    return {
        "version":VERSION,"canonical_observations_sha256":source_sha,
        "base_plan_selected_from":"ORIGINAL_BASELINE_TRAIN_ONLY",
        "baseline_cells_frozen":selected_base_cells,
        "selection_split":"TRAIN_ONLY","validation_refit":False,
        "TRAIN_single_add_on_pareto":pareto,
        "frozen_frontier_evidence":frozen_candidates,
        "result_by_split":by_split,
        "budget_policy_supplied":policy is not None,
        "selected_add_on":selected,
        "selected_validation_frozen":selected_validation,
        "status":("NO_APPROVED_BUDGET_NO_SELECTION" if policy is None
                  else ("TRAIN_FEASIBLE_CANDIDATE" if selected
                        else "NO_TRAIN_FEASIBLE_ADD_ON")),
        "optimization_scope":"SINGLE_NEW_CELL_ON_TOP_OF_FROZEN_ORIGINAL_BASELINE",
        "multicell_optimum_claimed":False,
        "TEST_evaluated":False,"may_promote_policy":False,
        "fdr_measured":False,"sql_server_latency_measured":False,
        "city_scale_certified":False,
    }
