#!/usr/bin/env python3
"""Deterministic synthetic AA01 bulk fixture for disposable HTTP E2E (max 1k per ZIP).

Never outputs real people or writes under committed fixtures. All source rows
are synthesized locally, and the caller must keep ZIP/JSONL outside artifacts.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path


def make_cpf(index: int, seed: int) -> str:
    """Generate a unique *synthetic* CPF with valid check digits (test only)."""
    base = f"{800000000 + seed * 1000 + index:09d}"
    if len(base) != 9:
        raise ValueError("synthetic CPF prefix outside bounded range")

    def digit(data: str) -> str:
        value = (sum(int(x) * w for x, w in
                     zip(data, range(len(data) + 1, 1, -1))) * 10) % 11
        return "0" if value == 10 else str(value)

    return base + digit(base) + digit(base + digit(base))


def generate(fixture: Path, out: Path, count: int, seed: int, start_index: int = 1) -> dict:
    if not (1 <= count <= 1000 and seed == 355 and
            1 <= start_index <= 10000 and start_index + count - 1 <= 10000):
        raise ValueError("HTTP E2E permits at most 1000 rows/ZIP, range 1..10000, frozen seed355")
    if out.exists() and any(out.iterdir()):
        raise FileExistsError("Refuse to overwrite an existing bulk fixture directory")
    manifest = (fixture / "manifest.json").read_bytes()
    people = json.loads((fixture / "pessoas.jsonl").read_text(encoding="utf-8").strip())
    record = json.loads((fixture / "registros.jsonl").read_text(encoding="utf-8").strip())
    out.mkdir(parents=True, exist_ok=True)
    (out / "manifest.json").write_bytes(manifest)

    first_names = ("Maria", "Ana", "Julia", "Paula", "Beatriz", "Renata", "Lucia", "Teresa")
    last_names = ("Silva", "Santos", "Costa", "Oliveira", "Pereira", "Lima", "Ferreira")
    with (out / "pessoas.jsonl").open("w", encoding="utf-8", newline="\n") as persons, \
         (out / "registros.jsonl").open("w", encoding="utf-8", newline="\n") as records:
        for i in range(start_index, start_index + count):
            p = copy.deepcopy(people)
            r = copy.deepcopy(record)
            key = f"PESSOA-E2E-VOL-{i:06d}"
            p["idPessoaEntrega"] = key
            p["cpf"] = make_cpf(i, seed)
            p["cpfAusenteMotivo"] = None
            p["nomeCompleto"] = f"{first_names[(i - 1) % len(first_names)]} de {last_names[(i - 1) // len(first_names) % len(last_names)]}"
            p["nomeMae"] = f"{first_names[(i + 2) % len(first_names)]} {last_names[(i + 3) % len(last_names)]}"
            p["dataNascimento"] = f"{1975 + i % 35:04d}-{1 + i % 12:02d}-{1 + i % 28:02d}"
            p["sourceTransactionId"] = f"SEHAB-E2E-VOLUME-2026-{i:06d}"
            for position, attr in enumerate(p.get("atributosTransversais", []), start=1):
                attr["sourceRecordId"] = f"SEH-E2E-VOL-{i:06d}-END-{position}"
            r["idPessoaEntrega"] = key
            r["codigoRegistroOrigem"] = f"E2E-AA01-VOL-2026-{i:06d}"
            persons.write(json.dumps(p, ensure_ascii=False, separators=(",", ":")) + "\n")
            records.write(json.dumps(r, ensure_ascii=False, separators=(",", ":")) + "\n")
    return {"synthetic": True, "fixture": "AA01_v2", "count": count, "seed": seed,
            "startIndex": start_index}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--count", type=int, default=1000)
    parser.add_argument("--seed", type=int, default=355)
    parser.add_argument("--start-index", type=int, default=1)
    args = parser.parse_args()
    result = generate(args.fixture, args.output_dir, args.count, args.seed, args.start_index)
    # Output only cardinality/seed — never a CPF, name or payload.
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
