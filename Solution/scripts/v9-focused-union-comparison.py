#!/usr/bin/env python3
"""Focused blocking retrieval diagnostic; not Rank/Resolve or FDR certification."""
import argparse
import csv
import hashlib
import json
import time
from collections import defaultdict
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

def key(row, cell):
    name, mother = tokens(row["nome_normalizado"]), tokens(row["nome_mae_normalizado"])
    dob = row["data_nascimento"].strip()
    if cell == "P1": parts = (row["nome_fonetico"].strip(), mother[-1] if mother else "")
    elif cell == "P2": parts = (row["nome_fonetico"].strip(), dob[:4] if len(dob) >= 4 else "")
    elif cell == "P3": parts = (name[-1] if name else "", row["nome_mae_fonetico"].strip())
    elif cell == "P5": parts = (name[0][:3] if name and len(name[0]) >= 3 else "", dob)
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
        subset = [r for r in rows if r["particao"] == split and not r["cpf"].strip()]
        truth_by_person = defaultdict(list)
        for i, row in enumerate(subset): truth_by_person[row["base_person_id"]].append(i)
        truth = {(i,j) for group in truth_by_person.values() for i in group for j in group
                 if i < j and subset[i]["gestor"] != subset[j]["gestor"]}
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
        results[split] = {"observations_without_cpf":len(subset),"metrics":measurements}
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
            "warning":"Canonical token splitting needs C# parity; no SQL p95, Rank/Resolve FDR or TEST certification.",
            "results":evaluate(rows)}
    Path(a.out).write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
if __name__=="__main__": main()
