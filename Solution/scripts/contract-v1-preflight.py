#!/usr/bin/env python3
"""Read-only guard for Pessoa v6 -> v1 contract consolidation.

Checks metadata, schemas, the approval SHA-256 inventory and atomic transition.
Never changes contracts, SQL, containers, environment files or Git.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

GESTORES = ("SEHAB", "SMADS", "SMDET", "SMS")


class PreflightError(ValueError):
    pass


def _read_json(path: Path) -> dict:
    if not path.is_file():
        raise PreflightError(f"Arquivo obrigatorio ausente: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise PreflightError(f"JSON invalido: {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise PreflightError(f"JSON deve ser objeto: {path}")
    return value


def audit(root: Path) -> dict:
    """Return read-only evidence; reject missing metadata, SHA drift and mixed state."""
    root = root.resolve()
    approvals = _read_json(root / "config/governance/schema-approvals.json")
    entries = approvals.get("contracts")
    if not isinstance(entries, list):
        raise PreflightError("schema-approvals.json: contracts deve ser uma lista")
    indexed = {}
    for item in entries:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str):
            raise PreflightError("Entrada de aprovacao invalida")
        path = item["path"]
        if path in indexed:
            raise PreflightError(f"Aprovacao duplicada: {path}")
        indexed[path] = item

    present_v6 = [
        (root / f"config/contracts/gestores/{gestor}/pessoa/v6/pessoa.schema.json").is_file()
        for gestor in GESTORES
    ]
    if any(present_v6) and not all(present_v6):
        raise PreflightError("Consolidacao parcial: v6 deve existir em todos os Gestores ou em nenhum")
    mode = "PRE_CONSOLIDACAO" if all(present_v6) else "CONSOLIDADO_V1"

    details = []
    for gestor in GESTORES:
        prefix = f"config/contracts/gestores/{gestor}/pessoa"
        metadata_rel = f"{prefix}/v1/pessoa.json"
        schema_rel = f"{prefix}/{'v6' if mode == 'PRE_CONSOLIDACAO' else 'v1'}/pessoa.schema.json"
        metadata = _read_json(root / metadata_rel)
        schema = _read_json(root / schema_rel)
        if metadata.get("gestor") != gestor or metadata.get("versao") != 1:
            raise PreflightError(f"Metadados v1 incorretos para {gestor}; preservar pessoa.json v1")
        expected_id = f"/gestores/{gestor}/pessoa/{'v6' if mode == 'PRE_CONSOLIDACAO' else 'v1'}/"
        if expected_id not in str(schema.get("$id", "")):
            raise PreflightError(f"$id do schema {gestor} nao corresponde a {schema_rel}")

        fingerprints = {}
        for rel in (metadata_rel, schema_rel):
            entry = indexed.get(rel)
            if entry is None or not isinstance(entry.get("sha256"), str):
                raise PreflightError(f"Hash nao registrado em schema-approvals.json: {rel}")
            actual = hashlib.sha256((root / rel).read_bytes()).hexdigest()
            if actual.lower() != entry["sha256"].lower():
                raise PreflightError(
                    f"Divergencia SHA-256: {rel}; esperado={entry['sha256']} atual={actual}"
                )
            fingerprints[rel] = actual
        legacy = [v for v in range(2, 7) if (root / f"{prefix}/v{v}").exists()]
        if mode == "CONSOLIDADO_V1" and legacy:
            raise PreflightError(f"Versoes antigas presentes apos consolidacao em {gestor}: {legacy}")
        details.append({
            "gestor": gestor,
            "metadataSource": metadata_rel,
            "schemaSource": schema_rel,
            "schemaHashesVerified": fingerprints,
            "legacyVersions": legacy,
        })

    return {
        "status": "OK_READ_ONLY",
        "mode": mode,
        "databaseResetAuthorized": False,
        "nextAction": (
            "Revisar inventario, capturar backup verificavel e migrar em banco descartavel."
            if mode == "PRE_CONSOLIDACAO"
            else "Validar gates contratuais, ZIPs e E2E no banco descartavel antes de cutover."
        ),
        "gestores": details,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root", type=Path, default=Path(__file__).resolve().parents[1],
        help="Diretorio Solution",
    )
    args = parser.parse_args(argv)
    try:
        result = audit(args.root)
    except PreflightError as exc:
        print(f"CONTRACT V1 PREFLIGHT: FAIL: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
