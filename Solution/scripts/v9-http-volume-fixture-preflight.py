#!/usr/bin/env python3
"""Fail-closed offline integrity check of generated synthetic HTTP volume fixtures.

This checks source cardinality and internal references only; it is NOT a linkage oracle.
Never emits identity data, even on errors.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import importlib.util

_PRODUCER = Path(__file__).with_name("v9-http-volume-fixture.py")
_spec = importlib.util.spec_from_file_location("v9_volume_fixture_generator", _PRODUCER)
_generator = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_generator)


def verify(directory: Path, expected_count: int) -> dict:
    if expected_count != 1000:
        raise ValueError("only approved 1k fixture phase supported")
    if not (directory / "manifest.json").is_file():
        raise ValueError("manifest missing")
    canonical_manifest = (Path(__file__).resolve().parents[1] / "tests/fixtures/ingestao/AA01_v2/manifest.json").read_bytes()
    if (directory / "manifest.json").read_bytes() != canonical_manifest:
        raise ValueError("manifest differs from frozen AA01_v2 fixture")
    if {p.name for p in directory.iterdir()} != {"manifest.json", "pessoas.jsonl", "registros.jsonl"}:
        raise ValueError("unexpected or missing fixture files")
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
            expected_key = f"PESSOA-E2E-VOL-{person_count:06d}"
            if key != expected_key or cpf != _generator.make_cpf(person_count, 355):
                raise ValueError("synthetic identity deviates from frozen seed/index")
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
            expected_key = f"PESSOA-E2E-VOL-{record_count:06d}"
            expected_record = f"E2E-AA01-VOL-2026-{record_count:06d}"
            if key != expected_key or record_id != expected_record:
                raise ValueError("synthetic record deviates from frozen seed/index")
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
