#!/usr/bin/env python3
"""Translate canonical C# CSV columns to planner JSONL without modifying projection values."""
import csv
import json
from pathlib import Path

def convert(source, target):
    required = {"observacao_id", "base_person_id", "particao", "gestor", "cpf",
                "nome_normalizado", "nome_fonetico", "nome_mae_normalizado",
                "nome_mae_fonetico", "data_nascimento"}
    seen = set()
    identity_split = {}
    with Path(source).open(newline="", encoding="utf-8-sig") as src, Path(target).open("w", encoding="utf-8") as dst:
        reader = csv.DictReader(src)
        if not required.issubset(reader.fieldnames or []):
            raise ValueError("missing canonical fields")
        for row in reader:
            obs, identity, split = row["observacao_id"], row["base_person_id"], row["particao"]
            if not obs or obs in seen or not identity or split not in ("TRAIN", "VALIDATION", "TEST"):
                raise ValueError("invalid observation or partition")
            if identity in identity_split and identity_split[identity] != split:
                raise ValueError("identity partition leakage")
            identity_split[identity] = split
            seen.add(obs)
            mapped = {"id": obs, "truth_id": identity, "split": split,
                      "birth_date": row["data_nascimento"], "gestor": row["gestor"],
                      "cpf_present": bool(row["cpf"])}
            for field in ("nome_normalizado", "nome_fonetico", "nome_mae_normalizado", "nome_mae_fonetico"):
                mapped[field] = row[field]
            dst.write(json.dumps(mapped, ensure_ascii=False, sort_keys=True) + "\n")
    return len(seen)
