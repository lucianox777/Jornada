#!/usr/bin/env python3
"""Build a canonical V9 corpus generation input receipt from actual immutable files."""
import argparse
import hashlib
import json
from pathlib import Path

def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def tree(root):
    if not root.is_dir() or root.is_symlink():
        raise ValueError("Reference root missing or unsafe: " + str(root))
    result = {}
    for file in sorted(root.rglob("*")):
        if file.is_symlink():
            raise ValueError("Reference contains symlink: " + str(file))
        if file.is_file():
            result[file.relative_to(root).as_posix()] = sha(file)
    if not result:
        raise ValueError("Reference has no files: " + str(root))
    return result

def make(args):
    if args.birth_daily.is_symlink() or not args.birth_daily.is_file():
        raise ValueError("Birth daily reference missing or unsafe: " + str(args.birth_daily))
    return {
        "schema": "v9-corpus-inputs-v2",
        "partition_contract": "TRAIN_VALIDATION_TEST_SEALED_V9",
        "generator_command": "Jornada.Linkage.SyntheticCorpus generate",
        "generator_source_sha256": tree(args.generator_source),
        "ibge_names_sha256": tree(args.ibge_names),
        "birth_daily_sha256": sha(args.birth_daily),
        "family_ref_sha256": tree(args.family_ref) if args.family_ref else None,
        "people": args.people,
        "seed": args.seed,
        "population_profile": args.population_profile,
        "error_profile": args.error_profile,
        "generator_arguments": {
            "reference_root": "ibge-nomes-2022",
            "birth_daily_source": "synthetic-birth-sp"
        }
    }

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--generator-source", type=Path, required=True)
    p.add_argument("--ibge-names", type=Path, required=True)
    p.add_argument("--birth-daily", type=Path, required=True)
    p.add_argument("--family-ref", type=Path)
    p.add_argument("--people", type=int, default=100000)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--population-profile", default="demographic-primary")
    p.add_argument("--error-profile", default="clean")
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    if a.people < 1 or a.seed < 0:
        p.error("people and seed must be valid nonnegative generation inputs")
    result = make(a)
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, sort_keys=True, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("V9_CORPUS_INPUT_RECEIPT_CREATED " + str(a.output))

if __name__ == "__main__":
    main()
