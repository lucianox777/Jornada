#!/usr/bin/env python3
"""Admit an isolated synthetic corpus namespace under an operator-provisioned REF store.

Path separation is necessary but NOT evidence of cross-run durability.
"""
import argparse
import json
from pathlib import Path


def validate(ref_root: Path, corpus_store: Path, official_ref: Path) -> dict:
    for name, path in (("REF root", ref_root), ("Corpus store", corpus_store), ("Official REF", official_ref)):
        if not path.is_absolute():
            raise ValueError(name + " must be absolute")
        if any(part.is_symlink() for part in (path, *path.parents)):
            raise ValueError(name + " cannot contain symlink ancestors")
    if not ref_root.is_dir():
        raise ValueError("REF root must already exist")
    if not official_ref.is_dir():
        raise ValueError("Official REF must already exist")
    if not official_ref.is_relative_to(ref_root):
        raise ValueError("Official REF must belong to the REF root")
    if not corpus_store.is_relative_to(ref_root):
        raise ValueError("Synthetic store must belong to the REF root")
    if corpus_store == ref_root or corpus_store == official_ref:
        raise ValueError("Synthetic store cannot overwrite the REF root or official REF")
    if corpus_store.is_relative_to(official_ref) or official_ref.is_relative_to(corpus_store):
        raise ValueError("Synthetic corpus and official REF must be disjoint")
    if corpus_store.exists() and not corpus_store.is_dir():
        raise ValueError("Synthetic store is not a directory")
    return {"status": "REF_NAMESPACE_ISOLATION_PASS_NOT_DURABILITY",
            "ref_root": str(ref_root), "corpus_store": str(corpus_store),
            "official_ref": str(official_ref)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ref-root", type=Path, required=True)
    parser.add_argument("--corpus-store", type=Path, required=True)
    parser.add_argument("--official-ref", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(validate(args.ref_root, args.corpus_store, args.official_ref), sort_keys=True))


if __name__ == "__main__":
    main()
