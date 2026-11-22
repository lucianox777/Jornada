#!/usr/bin/env python3
"""Fail-closed offline integrity check of generated synthetic HTTP volume fixtures.

This checks source cardinality and internal references only; it is NOT a linkage oracle.
Never emits identity data, even on errors.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path


def verify(directory: Path, expected_count: int) -> dict:
    if expected_count != 1000:
        raise ValueError("only approved 1k fixture phase supported")
    if not (directory / "manifest.json").is_file():
        raise ValueError("manifest missing")
    if not json.loads((directory / "manifest.json").read_text(encoding="utf-8")):
        raise ValueError("manifest empty")
    keys: set[str] = set()
    cpfs: set[str] = set()
    person_count = 0
    with (directory / "pessoas.jsonl").open(encoding="utf-8") as stream:
        for line in stream:
            person_count += 1
            if person_count > expected_count:
                raise ValueError("excess persons")
            row = json.loads(line)
            key, cpf = row.get("idPessoaEntrega"), row.get("cpf")
            if (not isinstance(key, str) or not key.startswith("PESSOA-E2E-VOL-")
                    or not isinstance(cpf, str) or len(cpf) != 11 or not cpf.isdigit()
                    or key in keys or cpf in cpfs):
                raise ValueError("invalid or duplicated synthetic person")
            keys.add(key)
            cpfs.add(cpf)
    if person_count != expected_count:
        raise ValueError("person cardinality mismatch")
    record_keys: set[str] = set()
    record_ids: set[str] = set()
    record_count = 0
    with (directory / "registros.jsonl").open(encoding="utf-8") as stream:
        for line in stream:
            record_count += 1
            if record_count > expected_count:
                raise ValueError("excess records")
            row = json.loads(line)
            key, record_id = row.get("idPessoaEntrega"), row.get("codigoRegistroOrigem")
            if (key not in keys or key in record_keys
                    or not isinstance(record_id, str) or not record_id.startswith("E2E-AA01-VOL-")
                    or record_id in record_ids):
                raise ValueError("invalid synthetic record reference")
            record_keys.add(key)
            record_ids.add(record_id)
    if record_count != expected_count or record_keys != keys:
        raise ValueError("record cardinality or reference mismatch")
    return {"status": "PASS", "scope": "SYNTHETIC_FIXTURE_INTEGRITY_NOT_LINKAGE_ORACLE",
            "personCount": person_count, "recordCount": record_count,
            "uniquePersonReferences": len(keys), "uniqueCpfCount": len(cpfs)}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture-dir", required=True, type=Path)
    parser.add_argument("--count", required=True, type=int)
    args = parser.parse_args()
    try:
        print(json.dumps(verify(args.fixture_dir, args.count), sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError):
        print('E2E FIXTURE INTEGRITY: FAIL (no identity data emitted)', file=__import__('sys').stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
