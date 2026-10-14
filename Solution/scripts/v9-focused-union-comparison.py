#!/usr/bin/env python3
"""Focused blocking retrieval diagnostic; not Rank/Resolve or FDR certification."""
import argparse
import csv
import hashlib
import json
import time
from collections import defaultdict
from datetime import date
from pathlib import Path

REQUIRED = {"observacao_id", "base_person_id", "particao", "gestor", "cpf",
            "nome_normalizado", "nome_fonetico", "nome_mae_normalizado",
            "nome_mae_fonetico", "data_nascimento"}
BASE = [("nome_normalizado", "data_nascimento"),
        ("nome_mae_normalizado", "data_nascimento")]
# All token extraction uses canonical C# normalized/phonetic columns.
# These are experimental proxy keys, not an independent normalizer.
def tokens(value):
    return value.strip().split()

MISSING = {"NAO INFORMADO", "NÃO INFORMADO", "IGNORADO", "DESCONHECIDO",
           "SEM INFORMACAO", "SEM INFORMAÇÃO", "NULL", "NULO",
           "N/A", "NA", "NAO CONSTA", "NÃO CONSTA",
           "SEM NOME", "INDEFINIDO"}

def has_name(value):
    value = " ".join(tokens(value)).upper()
    return bool(value) and value not in MISSING and any(ch.isalpha() for ch in value) \
        and not any(ch.isdigit() for ch in value)

def has_birth(value):
    if not value.strip():
        return False
    try:
        birth = date.fromisoformat(value.strip())
    except ValueError:
        return False
    return birth.year >= 1850 and birth <= date.today() \
        and birth != date(1900, 1, 1)

def name_eligible(row):
    return has_name(row["nome_normalizado"]) and (
        has_name(row["nome_mae_normalizado"]) or has_birth(row["data_nascimento"]))

def true_pairs(subset):
    by_person = defaultdict(list)
    for i, row in enumerate(subset):
        by_person[row["base_person_id"]].append(i)
    return {(i, j) for group in by_person.values() for i in group for j in group
            if i < j and subset[i]["gestor"] != subset[j]["gestor"]}

def key(row, cell):
    name, mother = tokens(row["nome_normalizado"]), tokens(row["nome_mae_normalizado"])
    dob = row["data_nascimento"].strip()
    if cell == "P1": parts = (row["nome_fonetico"].strip(), mother[-1] if mother else "")
    elif cell == "P2": parts = (row["nome_fonetico"].strip(), dob[:4] if has_birth(dob) else "")
    elif cell == "P3": parts = (name[-1] if name else "", row["nome_mae_fonetico"].strip())
    elif cell == "P5": parts = (name[0][:3] if name and len(name[0]) >= 3 else "", dob if has_birth(dob) else "")
    elif cell == "P4": raise ValueError("P4 uses multiple maternal surnames in index")
    else: parts = tuple(row[f].strip() for f in cell)
    return parts if all(parts) else None

def evaluate(rows):
    seen, owners = set(), {}
    for row in rows:
        if not REQUIRED.issubset(row): raise ValueError("canonical C# columns required")
        oid, person, split = (row[k] for k in ("observacao_id", "base_person_id", "particao"))
        if not oid or oid in seen or not person or split not in ("TRAIN", "VALIDATION", "TEST"):
            raise ValueError("invalid canonical observation or partition")
        seen.add(oid)
        if person in owners and owners[person] != split: raise ValueError("identity leakage")
        owners[person] = split
    results = {}
    for split in ("TRAIN", "VALIDATION"):
        no_cpf = [r for r in rows if r["particao"] == split and not r["cpf"].strip()]
        all_truth_count = len(true_pairs(no_cpf))
        subset = [r for r in no_cpf if name_eligible(r)]
        truth = true_pairs(subset)
        pairs_by_cell = {}
        elapsed_by_cell = {}
        for cell in [*BASE, "P1", "P2", "P3", "P4", "P5"]:
            start = time.perf_counter()
            index = defaultdict(list)
            for i, row in enumerate(subset):
                keys = ([ (row["nome_fonetico"].strip(), surname) for surname in set(tokens(row["nome_mae_normalizado"])[1:]) ]
                        if cell == "P4" else [key(row, cell)])
                for k in keys:
                    if k and all(k): index[k].append(i)
            pairs = set()
            for group in index.values():
                for offset, i in enumerate(group):
                    for j in group[offset+1:]:
                        if subset[i]["gestor"] != subset[j]["gestor"]:
                            pairs.add((min(i,j), max(i,j)))
            label = "+".join(cell) if isinstance(cell, tuple) else cell
            pairs_by_cell[label] = pairs
            elapsed_by_cell[label] = time.perf_counter() - start
        configs = {"BASE": ["nome_normalizado+data_nascimento", "nome_mae_normalizado+data_nascimento"],
                   "UNIAO_4": ["P1", "P2", "P3", "P4"],
                   "UNIAO_5": ["P1", "P2", "P3", "P4", "P5"]}
        configs.update({c:[c] for c in ("P1", "P2", "P3", "P4", "P5")})
        measurements = {}
        for label, cells in configs.items():
            pairs = set().union(*(pairs_by_cell[c] for c in cells))
            hits = len(pairs & truth)
            fanout = [0] * len(subset)
            for i,j in pairs: fanout[i] += 1; fanout[j] += 1
            measurements[label] = {
                "recall": hits / len(truth) if truth else None,
                "true_pairs_retrieved": hits, "true_pairs_eligible": len(truth),
                "candidate_pairs": len(pairs),
                "false_candidate_pairs": len(pairs) - hits,
                "fanout_mean": sum(fanout)/len(fanout) if fanout else 0,
                "fanout_max": max(fanout, default=0),
                "retrieval_elapsed_seconds": sum(elapsed_by_cell[c] for c in cells),
                "fdr": None, "p95_sql_latency_ms": None}
        results[split] = {
            "observations_without_cpf": len(no_cpf),
            "name_eligible_observations": len(subset),
            "abstaining_observations": len(no_cpf) - len(subset),
            "all_no_cpf_true_pairs": all_truth_count,
            "name_eligible_true_pairs": len(truth),
            "eligibility_ceiling_over_all_truth":
                len(truth) / all_truth_count if all_truth_count else None,
            "metrics": measurements
        }
    return results

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--canonical-csv",required=True)
    p.add_argument("--out",required=True)
    a=p.parse_args()
    source=Path(a.canonical_csv)
    with source.open(newline="",encoding="utf-8-sig") as f: rows=list(csv.DictReader(f))
    report={"version":"V9_FOCUSED_UNION_DIAGNOSTIC_V1",
            "source_sha256":hashlib.sha256(source.read_bytes()).hexdigest(),
            "diagnostic_only":True,
            "test_not_used_for_selection":True,
            "name_required_policy":"V9_PERSON_REQUIRED_PLUS_MOTHER_OR_BIRTH_DIAGNOSTIC_V1",
            "warning":"P AND (M OR valid DOB) enforced. Token/surname P1/P3/P4 are proxy keys requiring C# parity; no SQL p95, FDR, Rank/Resolve or TEST certification.",
            "results":evaluate(rows)}
    Path(a.out).write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
if __name__=="__main__": main()
