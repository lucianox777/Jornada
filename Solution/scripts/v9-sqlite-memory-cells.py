#!/usr/bin/env python3
"""SQLite :memory: indexed candidate diagnostics over frozen C# V9 projections.

Exploration ONLY: never estimates accepted-link FDR or SQL Server performance.
Surname tokens remain explicit PROXIES pending BrazilianNameComponents parity.
"""
import argparse
import csv
import hashlib
import json
import math
import sqlite3
import time
from collections import defaultdict
from datetime import date
from pathlib import Path

VERSION = "V9_SQLITE_MEMORY_CELLS_DIAGNOSTIC_V2"
POLICY = "V9_PERSON_REQUIRED_PLUS_MOTHER_OR_BIRTH_DIAGNOSTIC_V1"
PROJECTION = "BLOCKING_PROJECTION_KEY_PROJECTOR_V4"
REQUIRED = {"observacao_id", "base_person_id", "particao", "gestor", "cpf",
            "nome_normalizado", "nome_fonetico", "nome_mae_normalizado",
            "nome_mae_fonetico", "data_nascimento"}
MISSING = {"NAO INFORMADO", "NÃO INFORMADO", "IGNORADO", "DESCONHECIDO",
           "SEM INFORMACAO", "SEM INFORMAÇÃO", "NULL", "NULO", "N/A",
           "NA", "NAO CONSTA", "NÃO CONSTA", "SEM NOME", "INDEFINIDO"}
PARTICLES = {"DA", "DAS", "DE", "DO", "DOS", "DI", "DU", "DEL", "LA", "LAS", "LOS"}
AGNOMES = {"FILHO", "FILHA", "NETO", "NETA", "JUNIOR", "JÚNIOR", "SOBRINHO", "SOBRINHA"}
PASS_ORDER = (
    "BASE_PERSON_DOB", "BASE_MOTHER_DOB",
    "P_PHON_M_LAST", "P_PHON_BIRTH_YEAR", "P_LAST_M_PHON", "P_PHON_M_ANY",
    "P_ANY_M_PHON", "P_LAST_M_ANY",
    "P_FIRST_M_FIRST", "P_FIRST_M_LAST", "P_FIRST_M_PHON",
    "P_PHON_M_FIRST", "P_FIRST_BIRTH_YEAR",
    "P_PREFIX3_DOB", "P_PREFIX3_M_PREFIX3", "P_PREFIX3_M_LAST",
    # New, explicitly targeted after inspecting aggregate missed-M signatures
    # from the original seed-42 frozen TRAIN/VALIDATION diagnostic.
    "P_FIRST_DOB_MONTHDAY", "P_ANY_BIRTH_YEAR",
    "P_ANY_DOB_MONTHDAY", "P_PREFIX3_BIRTH_YEAR", "P_LAST_M_FIRST",
)
BASE = PASS_ORDER[:2]
FOUR = PASS_ORDER[2:6]
EXTENDED = PASS_ORDER[2:16]  # V1 immutable 14-cell experimental union
TARGETED = PASS_ORDER[16:]
EXPANDED = PASS_ORDER[2:]
CAP_LIMIT = 1000000


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def meaningful(value):
    value = " ".join(value.strip().upper().split())
    return bool(value) and value not in MISSING and any(c.isalpha() for c in value) \
        and not any(c.isdigit() for c in value)


def usable_birth(value):
    if not value.strip():
        return None
    try:
        birth = date.fromisoformat(value.strip())
    except ValueError as ex:
        raise ValueError("invalid canonical ISO birth date") from ex
    if birth.year < 1850 or birth > date.today() or birth == date(1900, 1, 1):
        return None
    return birth


def parts(value):
    return value.strip().upper().split()


def surname_keys(value):
    # Diagnostic proxy; canonical Portuguese particles/agnomes must be checked
    # against BrazilianNameComponents before SQL Server promotion.
    words = parts(value)
    if len(words) < 2:
        return ()
    content = words[1:]
    if content and content[-1] in AGNOMES:
        content = content[:-1]
    content = [s for s in content if s not in PARTICLES]
    if not content:
        return ()
    return (content[-1], *sorted(set(content[:-1]) - {content[-1]}))


def key(*values):
    if not all(values):
        return None
    # Unambiguous composite encoding. No concatenation delimiter collisions.
    return json.dumps(values, separators=(",", ":"), ensure_ascii=False)


def keys(row):
    p = parts(row["nome_normalizado"])
    m = parts(row["nome_mae_normalizado"])
    pf = row["nome_fonetico"].strip().upper()
    mf = row["nome_mae_fonetico"].strip().upper()
    birth = usable_birth(row["data_nascimento"])
    d = birth.isoformat() if birth else ""
    y = str(birth.year) if birth else ""
    monthday = f"{birth.month:02d}-{birth.day:02d}" if birth else ""
    ps = surname_keys(row["nome_normalizado"])
    ms = surname_keys(row["nome_mae_normalizado"])
    last_p = ps[0] if ps else ""
    last_m = ms[0] if ms else ""
    first_p = p[0] if p else ""
    first_m = m[0] if m else ""
    prefix_p = first_p[:3] if len(first_p) >= 3 else ""
    prefix_m = first_m[:3] if len(first_m) >= 3 else ""
    one = {
        "BASE_PERSON_DOB": [key(row["nome_normalizado"], d)],
        "BASE_MOTHER_DOB": [key(row["nome_mae_normalizado"], d)],
        "P_PHON_M_LAST": [key(pf, last_m)],
        "P_PHON_BIRTH_YEAR": [key(pf, y)],
        "P_LAST_M_PHON": [key(last_p, mf)],
        "P_PHON_M_ANY": [key(pf, surname) for surname in ms],
        "P_ANY_M_PHON": [key(surname, mf) for surname in ps],
        "P_LAST_M_ANY": [key(last_p, surname) for surname in ms],
        "P_FIRST_M_FIRST": [key(first_p, first_m)],
        "P_FIRST_M_LAST": [key(first_p, last_m)],
        "P_FIRST_M_PHON": [key(first_p, mf)],
        "P_PHON_M_FIRST": [key(pf, first_m)],
        "P_FIRST_BIRTH_YEAR": [key(first_p, y)],
        "P_PREFIX3_DOB": [key(prefix_p, d)],
        "P_PREFIX3_M_PREFIX3": [key(prefix_p, prefix_m)],
        "P_PREFIX3_M_LAST": [key(prefix_p, last_m)],
        # These new cells ALWAYS include a person-name representation and
        # at least a maternal name or usable date feature.
        "P_FIRST_DOB_MONTHDAY": [key(first_p, monthday)],
        "P_ANY_BIRTH_YEAR": [key(surname, y) for surname in ps],
        "P_ANY_DOB_MONTHDAY": [key(surname, monthday) for surname in ps],
        "P_PREFIX3_BIRTH_YEAR": [key(prefix_p, y)],
        "P_LAST_M_FIRST": [key(last_p, first_m)],
    }
    return {cell: list(dict.fromkeys(k for k in items if k))
            for cell, items in one.items()}


def validate_rows(rows):
    if not rows:
        raise ValueError("empty frozen corpus")
    ids, owners = set(), {}
    for r in rows:
        if not REQUIRED.issubset(r):
            raise ValueError("missing canonical C# projection columns")
        oid, base, part, manager = (r[x].strip() for x in (
            "observacao_id", "base_person_id", "particao", "gestor"))
        if not oid or oid in ids or not base or not manager or part not in (
                "TRAIN", "VALIDATION", "TEST"):
            raise ValueError("duplicate/invalid observation or partition")
        ids.add(oid)
        if base in owners and owners[base] != part:
            raise ValueError("identity leakage between TRAIN/VALIDATION/TEST")
        owners[base] = part
        usable_birth(r["data_nascimento"])


def truth_for(rows, require_name):
    groups = defaultdict(list)
    for r in rows:
        if r["cpf"].strip():
            continue
        if require_name and not (meaningful(r["nome_normalizado"]) and
                                 (meaningful(r["nome_mae_normalizado"]) or
                                  usable_birth(r["data_nascimento"]) is not None)):
            continue
        groups[r["base_person_id"]].append(r)
    return {(min(a["observacao_id"], b["observacao_id"]),
             max(a["observacao_id"], b["observacao_id"]))
            for group in groups.values()
            for i, a in enumerate(group) for b in group[i+1:]
            if a["gestor"] != b["gestor"]}


def indexed_candidates(rows, cap):
    if cap < 1 or cap > CAP_LIMIT:
        raise ValueError("invalid per-cell cap; no silent truncation")
    connection = sqlite3.connect(":memory:")
    try:
        connection.executescript("""
            PRAGMA temp_store=MEMORY;
            CREATE TABLE observations(
                oid TEXT PRIMARY KEY COLLATE BINARY,
                part TEXT NOT NULL COLLATE BINARY,
                manager TEXT NOT NULL COLLATE BINARY
            );
            CREATE TABLE cellkeys(
                cell TEXT NOT NULL COLLATE BINARY,
                part TEXT NOT NULL COLLATE BINARY,
                composite TEXT NOT NULL COLLATE BINARY,
                oid TEXT NOT NULL COLLATE BINARY,
                PRIMARY KEY(cell,part,composite,oid)
            ) WITHOUT ROWID;
        """)
        obs = []
        keys_to_insert = []
        for r in rows:
            if r["particao"] not in ("TRAIN", "VALIDATION") or r["cpf"].strip():
                continue
            if not (meaningful(r["nome_normalizado"]) and
                    (meaningful(r["nome_mae_normalizado"]) or
                     usable_birth(r["data_nascimento"]) is not None)):
                continue
            oid = r["observacao_id"]
            obs.append((oid, r["particao"], r["gestor"]))
            for cell, values in keys(r).items():
                for value in values:
                    keys_to_insert.append((cell, r["particao"], value, oid))
        connection.executemany(
            "INSERT INTO observations(oid,part,manager) VALUES(?,?,?)", obs)
        connection.executemany(
            "INSERT INTO cellkeys(cell,part,composite,oid) VALUES(?,?,?,?)",
            keys_to_insert)
        sql = """
            SELECT DISTINCT a.oid,b.oid
              FROM cellkeys a
              JOIN cellkeys b ON b.cell=a.cell
                 AND b.part=a.part AND b.composite=a.composite
                 AND b.oid>a.oid
              JOIN observations oa ON oa.oid=a.oid
              JOIN observations ob ON ob.oid=b.oid
             WHERE a.cell=? AND a.part=? AND oa.manager<>ob.manager
             LIMIT ?
        """
        report = {}
        for part in ("TRAIN", "VALIDATION"):
            for cell in PASS_ORDER:
                started = time.perf_counter()
                pairs = set(connection.execute(sql, (cell, part, cap + 1)))
                if len(pairs) > cap:
                    raise ValueError(
                        f"cell {cell}/{part} exceeds physical-pair cap={cap}; never truncate")
                report[(part, cell)] = (pairs, round(time.perf_counter()-started, 6))
        return report
    finally:
        connection.close()


def measure(pairs, true, count):
    # A candidate degree is the number of DISTINCT returned neighbor records
    # per eligible observation. Include zero-degree records in every quantile.
    # Wrong candidates are nonmatches U, not FDR/accepted false links.
    from collections import Counter
    m = len(pairs & true)
    degrees, u_degrees = Counter(), Counter()
    for a, b in pairs:
        degrees[a] += 1
        degrees[b] += 1
        if (a, b) not in true:
            u_degrees[a] += 1
            u_degrees[b] += 1
    if len(degrees) > count or len(u_degrees) > count:
        raise ValueError("candidate set has endpoint outside eligible frame")

    def distribution(values):
        zero = count - len(values)
        ordered = sorted(values.values())
        def quantile(p):
            if count == 0:
                return None
            rank = max(0, math.ceil(count*p)-1)
            return 0 if rank < zero else ordered[rank-zero]
        return {
            "mean": (sum(values.values())/count if count else None),
            "p50": quantile(0.50), "p95": quantile(0.95),
            "p99": quantile(0.99), "max": max(ordered, default=0),
            "zero_degree_observations": zero
        }
    return {"M": m, "U": len(pairs)-m, "recall": m/len(true) if true else None,
            "candidate_pairs": len(pairs), "degree": distribution(degrees),
            "u_degree": distribution(u_degrees)}



def profile_unrecovered_truth(rows, truth, candidates):
    """Explain synthetic M missed by the COMPLETE explored union, without PII.

    Hypothesis support counts only known true M. They are NOT incremental
    blocking measurements: running a new SQL pass is necessary to measure U,
    fanout, caps, time and final gain on the unchanged TRAIN/VALIDATION frame.
    """
    by_id = {r["observacao_id"]: r for r in rows}
    missing = truth - candidates
    signatures, reasons = defaultdict(int), defaultdict(int)
    signals = defaultdict(int)
    hypotheses = defaultdict(int)

    def shared(a, b):
        return bool(a) and a == b

    def intersect(a, b):
        return bool(set(a) & set(b))

    def features(r):
        person = parts(r["nome_normalizado"])
        mother = parts(r["nome_mae_normalizado"])
        p_surnames = surname_keys(r["nome_normalizado"])
        m_surnames = surname_keys(r["nome_mae_normalizado"])
        dob = usable_birth(r["data_nascimento"])
        return {
            "person_full": r["nome_normalizado"].strip().upper(),
            "person_phon": r["nome_fonetico"].strip().upper(),
            "person_first": person[0] if person else "",
            "person_prefix3": person[0][:3] if person and len(person[0]) >= 3 else "",
            "person_last": p_surnames[0] if p_surnames else "",
            "person_surnames": p_surnames,
            "mother_full": r["nome_mae_normalizado"].strip().upper(),
            "mother_phon": r["nome_mae_fonetico"].strip().upper(),
            "mother_first": mother[0] if mother else "",
            "mother_prefix3": mother[0][:3] if mother and len(mother[0]) >= 3 else "",
            "mother_last": m_surnames[0] if m_surnames else "",
            "mother_surnames": m_surnames,
            "dob": dob
        }

    cache = {}
    def cached(oid):
        if oid not in cache:
            cache[oid] = features(by_id[oid])
        return cache[oid]

    for aid, bid in missing:
        if aid not in by_id or bid not in by_id:
            raise ValueError("M pair is outside frozen TRAIN/VALIDATION frame")
        a, b = cached(aid), cached(bid)
        matched = {
            "P_FULL": shared(a["person_full"], b["person_full"]),
            "P_PHON": shared(a["person_phon"], b["person_phon"]),
            "P_FIRST": shared(a["person_first"], b["person_first"]),
            "P_PREFIX3": shared(a["person_prefix3"], b["person_prefix3"]),
            "P_LAST": shared(a["person_last"], b["person_last"]),
            "P_ANY": intersect(a["person_surnames"], b["person_surnames"]),
            "M_FULL": shared(a["mother_full"], b["mother_full"]),
            "M_PHON": shared(a["mother_phon"], b["mother_phon"]),
            "M_FIRST": shared(a["mother_first"], b["mother_first"]),
            "M_PREFIX3": shared(a["mother_prefix3"], b["mother_prefix3"]),
            "M_LAST": shared(a["mother_last"], b["mother_last"]),
            "M_ANY": intersect(a["mother_surnames"], b["mother_surnames"]),
            "DOB_EXACT": bool(a["dob"]) and a["dob"] == b["dob"],
            "DOB_YEAR": bool(a["dob"] and b["dob"]) and a["dob"].year == b["dob"].year,
            "DOB_MONTHDAY": bool(a["dob"] and b["dob"]) and
                (a["dob"].month, a["dob"].day) == (b["dob"].month, b["dob"].day),
            "DOB_ONE_YEAR_APART": bool(a["dob"] and b["dob"]) and
                abs(a["dob"].year - b["dob"].year) == 1,
        }
        for label, is_same in matched.items():
            if is_same:
                signals[label] += 1
        signature = "+".join(k for k, v in matched.items() if v) or "NO_SHARED_SIGNATURE"
        signatures[signature] += 1
        p = any(v for k, v in matched.items() if k.startswith("P_"))
        m = any(v for k, v in matched.items() if k.startswith("M_"))
        d = any(v for k, v in matched.items() if k.startswith("DOB_"))
        category = ("PERSON_MOTHER_BIRTH" if p and m and d else
                    "PERSON_MOTHER" if p and m else
                    "PERSON_BIRTH" if p and d else
                    "PERSON_ONLY" if p else
                    "NO_PERSON_SIGNATURE")
        reasons[category] += 1
        possible = {
            "P_PHON_M_PHON": matched["P_PHON"] and matched["M_PHON"],
            "P_PHON_M_PREFIX3": matched["P_PHON"] and matched["M_PREFIX3"],
            "P_FIRST_M_ANY": matched["P_FIRST"] and matched["M_ANY"],
            "P_ANY_M_FIRST": matched["P_ANY"] and matched["M_FIRST"],
            "P_LAST_M_FIRST": matched["P_LAST"] and matched["M_FIRST"],
            "P_PHON_DOB_MONTHDAY": matched["P_PHON"] and matched["DOB_MONTHDAY"],
            "P_FIRST_DOB_MONTHDAY": matched["P_FIRST"] and matched["DOB_MONTHDAY"],
            "P_PREFIX3_BIRTH_YEAR": matched["P_PREFIX3"] and matched["DOB_YEAR"],
            "P_LAST_BIRTH_YEAR": matched["P_LAST"] and matched["DOB_YEAR"],
            "P_ANY_BIRTH_YEAR": matched["P_ANY"] and matched["DOB_YEAR"],
        }
        for hypothesis, could_recover in possible.items():
            if could_recover:
                hypotheses[hypothesis] += 1

    if sum(reasons.values()) != len(missing) or sum(signatures.values()) != len(missing):
        raise ValueError("Unrecovered M residual profile does not reconcile")
    return {
        "unrecovered_eligible_true_M": len(missing),
        "reason_categories": dict(sorted(reasons.items())),
        "matching_signal_counts": dict(sorted(signals.items())),
        "candidate_hypotheses_potential_true_M_ONLY": dict(
            sorted(hypotheses.items(), key=lambda x: (-x[1], x[0]))),
        "most_common_signatures": [
            {"signature": k, "M": v}
            for k, v in sorted(signatures.items(), key=lambda x: (-x[1], x[0]))[:20]
        ],
        "hypothesis_U_fanout_sql_cost_measured": False,
        "test_evaluated": False,
        "uses_surname_proxy_not_canonical_CSharp_parity": True
    }


def fit_min_u_plans_on_train(train_cells, train_truth, min_recall=0.95):
    """Derive TWO diagnostic candidate subsets from TRAIN M and U only.

    Targeted subset: EXACT search over 2^5 target-cell subsets with the V1
    14-cell union fixed. Cost is observed U under the same TRAIN frame.
    Pruned: deterministic backward elimination from ALL 19 cells, deleting
    a pass only if the resulting TRAIN recall remains >= the preset target.
    Neither route uses VALIDATION/TEST or estimates accepted-link FDR.
    """
    if set(train_cells) != set(PASS_ORDER):
        raise ValueError("missing or unknown physical memory pass")
    if not (0 < min_recall <= 1):
        raise ValueError("invalid minimum recall")
    # Avoid floating threshold ambiguity for a fixed 95% target.
    if min_recall != 0.95:
        raise ValueError("unversioned recall threshold")
    needed = (19 * len(train_truth) + 19) // 20
    if not train_truth:
        return {"status": "NO_TRAIN_TRUTH", "minimum_recall": min_recall,
                "required_M": needed, "exact_targeted": None,
                "greedy_pruned": None}

    baseline = set().union(*(train_cells[c] for c in EXTENDED))
    choices = []
    for mask in range(1 << len(TARGETED)):
        chosen = tuple(c for i, c in enumerate(TARGETED) if mask & (1 << i))
        pairs = baseline.union(*(train_cells[c] for c in chosen))
        m = len(pairs & train_truth)
        if m < needed:
            continue
        u = len(pairs) - m
        # Minimize U, then passes, then maximize recall M.
        choices.append((u, len(chosen), -m, chosen))
    exact = None
    if choices:
        u, _, neg_m, chosen = min(choices)
        exact = {"cells": list(EXTENDED) + list(chosen),
                 "added_cells": list(chosen), "train_M": -neg_m,
                 "train_U": u, "exact_over_targeted_subsets": True}

    # One candidate-pair bitmask per observed pair, so backtracking
    # does not repeatedly materialize huge unions in SQL or Python.
    masks = {}
    for idx, cell in enumerate(EXPANDED):
        bit = 1 << idx
        for pair in train_cells[cell]:
            masks[pair] = masks.get(pair, 0) | bit
    selected_mask = (1 << len(EXPANDED)) - 1
    train_m = sum(pair in train_truth for pair in masks)
    train_u = len(masks) - train_m
    if train_m < needed:
        pruned = None
    else:
        eliminated = []
        while True:
            exclusive_m = [0] * len(EXPANDED)
            exclusive_u = [0] * len(EXPANDED)
            for pair, bits in masks.items():
                live = bits & selected_mask
                if live and live.bit_count() == 1:
                    idx = live.bit_length() - 1
                    if pair in train_truth:
                        exclusive_m[idx] += 1
                    else:
                        exclusive_u[idx] += 1
            removable = [
                i for i in range(len(EXPANDED))
                if selected_mask & (1 << i)
                and train_m - exclusive_m[i] >= needed
                and (exclusive_u[i] > 0 or exclusive_m[i] == 0)
            ]
            if not removable:
                break
            # Largest U saving first, then smallest lost M. Deterministic.
            idx = min(removable, key=lambda i:
                      (-exclusive_u[i], exclusive_m[i], EXPANDED[i]))
            train_m -= exclusive_m[idx]
            train_u -= exclusive_u[idx]
            selected_mask &= ~(1 << idx)
            eliminated.append(EXPANDED[idx])
        chosen = [c for i, c in enumerate(EXPANDED) if selected_mask & (1 << i)]
        assert train_m >= needed
        pruned = {"cells": chosen, "removed_cells": eliminated,
                  "train_M": train_m, "train_U": train_u,
                  "locally_greedy_not_global_optimum": True}
    return {"status": ("TRAIN_TARGET_FEASIBLE" if exact is not None
                       else "TRAIN_TARGET_INFEASIBLE"),
            "minimum_recall": min_recall, "required_M": needed,
            "exact_targeted": exact, "greedy_pruned": pruned}


def apply_fixed_min_u_plans(train_fit, sql_pairs, train_validation_true,
                           eligible_counts):
    """Evaluate TRAIN-fitted cell lists unchanged in VALIDATION."""
    result = {"trained_on": "TRAIN_ONLY", "validation_refit": False,
              "target_is_diagnostic_not_promotion": True,
              "min_recall": train_fit["minimum_recall"],
              "train_required_M": train_fit["required_M"],
              "status": train_fit["status"], "plans": {}}
    for name in ("exact_targeted", "greedy_pruned"):
        fit = train_fit[name]
        if fit is None:
            result["plans"][name] = None
            continue
        splits = {}
        for part in ("TRAIN", "VALIDATION"):
            eligible = train_validation_true[part]
            pairs = set().union(*(sql_pairs[(part, cell)][0]
                                  for cell in fit["cells"]))
            stats = measure(pairs, eligible, eligible_counts[part])
            needed = (19 * len(eligible) + 19) // 20
            stats["recall_target_met"] = bool(eligible) and stats["M"] >= needed
            stats["more_M_needed_for_95"] = max(0, needed - stats["M"])
            splits[part] = stats
        result["plans"][name] = {"selected_cells": fit["cells"],
                                 "fit_diagnostic": fit,
                                 "measured": splits}
    return result


def assess(rows, cap=250000, max_passes=8):
    validate_rows(rows)
    if max_passes < 1 or max_passes > len(EXTENDED):
        raise ValueError("invalid maximum adaptive diagnostic passes")
    sql_pairs = indexed_candidates(rows, cap)
    by_part = {}
    for part in ("TRAIN", "VALIDATION"):
        subset = [r for r in rows if r["particao"] == part]
        full = truth_for(subset, False)
        eligible = truth_for(subset, True)
        name_rows = sum(not r["cpf"].strip() and meaningful(r["nome_normalizado"])
                        and (meaningful(r["nome_mae_normalizado"]) or
                             usable_birth(r["data_nascimento"]) is not None)
                        for r in subset)
        no_cpf = sum(not r["cpf"].strip() for r in subset)
        cells = {cell: sql_pairs[(part, cell)][0] for cell in PASS_ORDER}
        baseline = set().union(*(cells[x] for x in BASE))
        four = set().union(*(cells[x] for x in FOUR))
        extended = set().union(*(cells[x] for x in EXTENDED))
        expanded = set().union(*(cells[x] for x in EXPANDED))
        summaries = {}
        for label, pairs in (("BASE", baseline), ("UNION_4", four),
                             ("UNION_EXTENDED", extended),
                             ("UNION_EXPANDED", expanded)):
            summaries[label] = measure(pairs, eligible, name_rows)
            summaries[label]["more_M_needed_for_95"] = max(
                0, (19*len(eligible)+19)//20-summaries[label]["M"])
        single = {}
        for cell in PASS_ORDER:
            pairs, elapsed = sql_pairs[(part, cell)]
            individual = measure(pairs, eligible, name_rows)
            without = set().union(*(cells[x] for x in PASS_ORDER if x != cell))
            individual.update({
                "exclusive_new_M_vs_other_cells": len((pairs - without) & eligible),
                "exclusive_new_U_vs_other_cells": len(pairs - without - eligible),
                "incremental_M_vs_BASE": len((pairs - baseline) & eligible),
                "incremental_U_vs_BASE": len(pairs - baseline - eligible),
                "sqlite_elapsed_seconds": elapsed
            })
            single[cell] = individual
        by_part[part] = {
            "observations_without_cpf": no_cpf,
            "name_eligible_observations": name_rows,
            "abstaining_observations": no_cpf - name_rows,
            "all_no_cpf_truth_M": len(full),
            "name_eligible_truth_M": len(eligible),
            "eligibility_ceiling_vs_all_truth": len(eligible)/len(full) if full else None,
            "unrecovered_extended_truth_profile":
                profile_unrecovered_truth(subset, eligible, extended),
            "unrecovered_expanded_truth_profile":
                profile_unrecovered_truth(subset, eligible, expanded),
            "metrics": summaries,
            "cells": single
        }
    # PLAN FITS TRAIN ONLY: validation cannot change any selected cell.
    train_true = truth_for([r for r in rows if r["particao"] == "TRAIN"], True)
    selected = []
    remaining = set(EXPANDED)
    train_union = set()
    progression = []
    while remaining and len(selected) < max_passes:
        scored = []
        for cell in remaining:
            gain = sql_pairs[("TRAIN", cell)][0] - train_union
            m = len(gain & train_true)
            u = len(gain) - m
            if m:
                scored.append((m/(u+1), m, -u, cell))
        if not scored:
            break
        _, _, _, chosen = sorted(scored, key=lambda x: (-x[0], -x[1], -x[2], x[3]))[0]
        remaining.remove(chosen)
        selected.append(chosen)
        train_union.update(sql_pairs[("TRAIN", chosen)][0])
        progression.append({
            "cell": chosen, "train_recall": len(train_union & train_true)/len(train_true)
            if train_true else None,
            "train_M": len(train_union & train_true),
            "train_U": len(train_union-train_true)})
        if train_true and len(train_union & train_true)*20 >= len(train_true)*19:
            break
    plan = {}
    for part in ("TRAIN", "VALIDATION"):
        subset = [r for r in rows if r["particao"] == part]
        eligible = truth_for(subset, True)
        pairs = set().union(*(sql_pairs[(part, cell)][0] for cell in selected))
        metrics = measure(pairs, eligible, by_part[part]["name_eligible_observations"])
        metrics["more_M_needed_for_95"] = max(
            0, (19*len(eligible)+19)//20 - metrics["M"])
        plan[part] = metrics
    truth_sets = {
        part: truth_for([r for r in rows if r["particao"] == part], True)
        for part in ("TRAIN", "VALIDATION")
    }
    cell_sets = {cell: sql_pairs[("TRAIN", cell)][0] for cell in PASS_ORDER}
    min_u_fit = fit_min_u_plans_on_train(cell_sets, truth_sets["TRAIN"])
    min_u_evidence = apply_fixed_min_u_plans(
        min_u_fit, sql_pairs, truth_sets,
        {part: by_part[part]["name_eligible_observations"]
         for part in ("TRAIN", "VALIDATION")})
    return {
        "minimum_U_95pct_diagnostic": min_u_evidence,
        "version": VERSION, "policy": POLICY,
        "engine": "sqlite3_memory_real_indexed_sql",
        "key_semantics": "CANONICAL_CSHARP_FULLNAME_AND_PHONETIC;SURNAME_TOKEN_PROXY_NOT_CSHARP_PARITY",
        "selection_split": "TRAIN_ONLY", "TEST_evaluated": False,
        "diagnostic_only": True, "may_promote_policy": False,
        "fdr_measured": False, "sql_server_p95_measured": False,
        "candidate_cap_per_cell": cap, "results": by_part,
        "train_selected_cell_order": selected, "train_greedy_progress": progression,
        "frozen_train_plan_evaluated_without_validation_refit": plan
    }


def run(snapshot, output, cap=250000, max_passes=8):
    snapshot, output = Path(snapshot).resolve(), Path(output).resolve()
    if snapshot == output or snapshot in output.parents:
        raise ValueError("output cannot overwrite or reside inside immutable snapshot")
    manifest = json.loads((snapshot/"manifest.json").read_text(encoding="utf-8"))
    if manifest.get("version") != "V9_CANONICAL_NAME_FREQUENCY_SNAPSHOT_V1" \
            or manifest.get("projection_version") != PROJECTION \
            or manifest.get("frozen") is not True \
            or manifest.get("test_used_for_frequency") is not False:
        raise ValueError("wrong frozen canonical snapshot/projection contract")
    names = (("canonical_observations.csv", "frozen_source_sha256"),
             ("train_name_frequencies.json", "frequency_sha256"),
             ("train_joint_name_frequencies.json", "joint_frequency_sha256"))
    for filename, sha in names:
        if digest(snapshot/filename) != manifest.get(sha):
            raise ValueError("frozen canonical snapshot SHA256 mismatch")
    with (snapshot/"canonical_observations.csv").open(
            encoding="utf-8-sig", newline="") as fh:
        parser = csv.DictReader(fh)
        if not REQUIRED.issubset(parser.fieldnames or ()):
            raise ValueError("missing C# canonical CSV columns")
        rows = list(parser)
    report = assess(rows, cap, max_passes)
    report["canonical_observations_sha256"] = manifest["frozen_source_sha256"]
    report["frozen_manifest_sha256"] = digest(snapshot/"manifest.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        raise FileExistsError("output already exists; do not overwrite experiment")
    output.write_text(json.dumps(report, ensure_ascii=False, sort_keys=True, indent=2)
                      + "\n", encoding="utf-8")
    return report


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--snapshot-dir", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--cap", type=int, default=250000)
    p.add_argument("--max-passes", type=int, default=8)
    a = p.parse_args()
    r = run(a.snapshot_dir, a.out, a.cap, a.max_passes)
    print(json.dumps({"version": r["version"], "plan": r["train_selected_cell_order"],
                      "train": r["frozen_train_plan_evaluated_without_validation_refit"]["TRAIN"],
                      "validation": r["frozen_train_plan_evaluated_without_validation_refit"]["VALIDATION"]},
                     ensure_ascii=False))
