#!/usr/bin/env python3
"""Adapt already-generated V9 synthetic observations.csv to in-memory blocking JSONL.

This is an input adapter, NOT a substitute for canonical C# projections.
Use only synthetic data, and compare canonical SQL/C# keys before claiming equivalence.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path

REQUIRED = ("observacao_id", "base_person_id", "particao", "gestor",
            "nome", "nome_mae", "data_nascimento", "cpf")
SPLITS = {"TRAIN", "VALIDATION", "TEST"}

def convert(source, output_dir):
    source = Path(source)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    writers = {}
    counts = {s: 0 for s in SPLITS}
    people = {s: set() for s in SPLITS}
    seen = set()
    try:
        for split in sorted(SPLITS):
            writers[split] = (output_dir / f"v9-observations-{split.lower()}.jsonl").open("w", encoding="utf-8")
        with source.open(newline="", encoding="utf-8-sig") as handle:
            reader = csv.DictReader(handle)
            if not set(REQUIRED).issubset(reader.fieldnames or ()):
                raise ValueError("missing required columns: " + ", ".join(sorted(set(REQUIRED) - set(reader.fieldnames or ()))))
            for item in reader:
                split = item["particao"]
                identity = item["base_person_id"]
                obs_id = item["observacao_id"]
                if split not in SPLITS or not identity or not obs_id or obs_id in seen:
                    raise ValueError("invalid split/base person/duplicate observation")
                seen.add(obs_id)
                people[split].add(identity)
                # This adapter uses raw strings; canonical C# projection equivalence is not asserted.
                name = item["nome"].strip()
                mother = item["nome_mae"].strip()
                birth = item["data_nascimento"].strip()
                row = {"id": obs_id, "truth_id": identity,
                       "split": split, "gestor": item["gestor"],
                       "cpf_present": bool(item["cpf"].strip()),
                       "person_full_raw": name, "mother_full_raw": mother,
                       "birth_date": birth, "birth_year": birth[:4] if birth else "",
                       "person_first": name.split()[0] if name else "",
                       "mother_first": mother.split()[0] if mother else ""}
                # Canonical projections must be supplied by the C# evaluation harness.\n                # Do not synthesize phonetic keys with a divergent Python algorithm.\n                writers[split].write(json.dumps(row, ensure_ascii=False) + "\n")
                counts[split] += 1
    finally:
        for writer in writers.values():
            writer.close()
    overlaps = [(a,b) for a in SPLITS for b in SPLITS if a < b and people[a] & people[b]]
    if overlaps:
        raise ValueError("BasePersonId leaks across splits")
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    return {"input_sha256": digest, "rows_by_split": counts,
            "base_people_by_split": {s: len(people[s]) for s in sorted(SPLITS)},
            "projection": "RAW_FIRST_TOKEN_DIAGNOSTIC_NOT_CANONICAL_CSHARP",
            "not_certified": True}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--observations-csv", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()
    report = convert(args.observations_csv, args.output_dir)
    out = Path(args.output_dir) / "v9-memory-input-manifest.json"
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))
if __name__ == "__main__":
    main()
