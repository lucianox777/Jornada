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

VERSION = "V9_SQLITE_MEMORY_CELLS_DIAGNOSTIC_V1"
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
)
BASE = PASS_ORDER[:2]
FOUR = PASS_ORDER[2:6]
EXTENDED = PASS_ORDER[2:]
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
        summaries = {}
        for label, pairs in (("BASE", baseline), ("UNION_4", four),
                             ("UNION_EXTENDED", extended)):
            summaries[label] = measure(pairs, eligible, name_rows)
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
            "metrics": summaries,
            "cells": single
        }
    # PLAN FITS TRAIN ONLY: validation cannot change any selected cell.
    train_true = truth_for([r for r in rows if r["particao"] == "TRAIN"], True)
    selected = []
    remaining = set(EXTENDED)
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
    return {
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
