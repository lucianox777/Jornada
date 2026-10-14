#!/usr/bin/env python3
"""Paired candidate-recall diagnostic for A/B/C/D (phonetic off/on), never promotion."""
import argparse
import csv
import hashlib
import itertools
import json
from collections import defaultdict
from pathlib import Path

ARMS = {
    "A0": [("nome_normalizado", "data_nascimento")],
    "A1": [("nome_normalizado", "data_nascimento"), ("nome_fonetico", "data_nascimento")],
    "B0": [("nome_normalizado", "nome_mae_normalizado")],
    "B1": [("nome_normalizado", "nome_mae_normalizado"), ("nome_fonetico", "nome_mae_fonetico")],
    "C0": [("nome_normalizado", "data_nascimento"), ("nome_mae_normalizado", "data_nascimento")],
    "C1": [("nome_normalizado", "data_nascimento"), ("nome_mae_normalizado", "data_nascimento"),
           ("nome_fonetico", "data_nascimento"), ("nome_mae_fonetico", "data_nascimento")],
    # D here is only a fixed-pass diagnostic proxy, NOT adaptive frequency planning.
    "D0": [("nome_normalizado", "nome_mae_normalizado"), ("nome_normalizado", "data_nascimento"), ("nome_mae_normalizado", "data_nascimento")],
    "D1": [("nome_normalizado", "nome_mae_normalizado"), ("nome_normalizado", "data_nascimento"), ("nome_mae_normalizado", "data_nascimento"),
           ("nome_fonetico", "nome_mae_fonetico"), ("nome_fonetico", "data_nascimento"), ("nome_mae_fonetico", "data_nascimento")],
}
E0 = ("nome_normalizado", "nome_mae_normalizado", "data_nascimento")
REQUIRED = {"observacao_id", "base_person_id", "particao", "gestor", "cpf",
            "nome_normalizado", "nome_fonetico", "nome_mae_normalizado",
            "nome_mae_fonetico", "data_nascimento"}

def evaluate(rows):
    if any(not REQUIRED.issubset(row) for row in rows):
        raise ValueError("canonical C# CSV required")
    seen = set()
    owners = {}
    for row in rows:
        oid, person, split = row["observacao_id"], row["base_person_id"], row["particao"]
        if not oid or oid in seen or not person or split not in {"TRAIN", "VALIDATION", "TEST"}:
            raise ValueError("invalid observation, identity or partition")
        seen.add(oid)
        if person in owners and owners[person] != split:
            raise ValueError("identity leaked across partitions")
        owners[person] = split
    result = {}
    for split in ("TRAIN", "VALIDATION", "TEST"):
        subset = [row for row in rows if row["particao"] == split]
        eligible = set()
        for i, left in enumerate(subset):
            for j in range(i + 1, len(subset)):
                right = subset[j]
                if left["gestor"] != right["gestor"] and not (left["cpf"] or right["cpf"]):
                    eligible.add((i, j))
        truth = {pair for pair in eligible if subset[pair[0]]["base_person_id"] == subset[pair[1]]["base_person_id"]}
        variants = {}
        retrieved_by_arm = {}
        for arm, passes in ARMS.items():
            for use_e0 in (False, True):
                retrieved = set()
                for fields in passes + ([E0] if use_e0 else []):
                    groups = defaultdict(list)
                    for i, row in enumerate(subset):
                        key = tuple(row.get(field, "").strip() for field in fields)
                        if all(key):
                            groups[key].append(i)
                    for indices in groups.values():
                        for pair in itertools.combinations(indices, 2):
                            if pair in eligible:
                                retrieved.add(pair)
                label = arm + ("+E0" if use_e0 else "")
                retrieved_by_arm[label] = retrieved
                variants[label] = {"m_eligible": len(truth), "m_retrieved": len(retrieved & truth),
                                   "m_missed": len(truth - retrieved), "u_retrieved": len(retrieved - truth),
                                   "candidate_pairs": len(retrieved),
                                   "recall": len(retrieved & truth) / len(truth) if truth else None}
        paired = {}
        for base in ("A", "B", "C", "D"):
            for use_e0 in (False, True):
                off = base + "0" + ("+E0" if use_e0 else "")
                on = base + "1" + ("+E0" if use_e0 else "")
                x, y = variants[off], variants[on]
                only_off = retrieved_by_arm[off] - retrieved_by_arm[on]
                only_on = retrieved_by_arm[on] - retrieved_by_arm[off]
                paired[base + ("+E0" if use_e0 else "")] = {
                    "m_only_off": len(only_off & truth), "m_only_on": len(only_on & truth),
                    "u_only_off": len(only_off - truth), "u_only_on": len(only_on - truth),
                    "phonetic_extra_m": len(only_on & truth) - len(only_off & truth),
                    "phonetic_extra_u": len(only_on - truth) - len(only_off - truth),
                    "delta_recall": (len(only_on & truth) - len(only_off & truth)) / len(truth) if truth else None}
        result[split] = {"observations": len(subset), "m_eligible": len(truth),
                         "u_eligible": len(eligible) - len(truth), "arms": variants,
                         "paired_phonetic": paired}
    return result

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--canonical-csv", required=True)
    p.add_argument("--out", required=True)
    a = p.parse_args()
    source = Path(a.canonical_csv)
    with source.open(newline="", encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    report = {"version": "V9_ABCD_PAIRED_DIAGNOSTIC_V1",
              "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "diagnostic_only": True,
              "not_abcd_production_strategy_equivalence": True,
              "warning": "Simplified fixed-pass proxies: D is NOT the adaptive joint-frequency strategy. No calibrated Rank/Resolve, physical SQL or certified sample-size comparison.",
              "results": evaluate(rows)}
    Path(a.out).write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
if __name__ == "__main__":
    main()
