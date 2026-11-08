#!/usr/bin/env python3
"""Fail-closed inventory guard for the PO-authorized binary document removals.

Requires EXACTLY the documented surviving paths, not merely a count of 43.
Never alters documents, git history, SQL, or workflow permissions.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
from pathlib import Path

MANIFEST = "Solution/docs/Inventario_Binarios_Congelado_20261108.json"
EXTENSIONS = {".docx", ".pdf", ".pptx", ".xlsx"}
SCHEMA = "JORNADA_NEUT_DEV_OFFICE_INVENTORY_FROZEN_V1"
PINNED_MASTER = "db75f3a3f1a3ad38ec6d4f8f1ae3b2e254694b54"
APPROVED_REMOVALS = {
    "Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.docx",
    "Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.pdf",
}


def _check(condition: bool, explanation: str) -> None:
    if not condition:
        raise ValueError("BINARY_INVENTORY_FAIL_CLOSED: " + explanation)


def validate_manifest(manifest: dict, report: dict, tracked: set[str]) -> int:
    _check(manifest.get("schema") == SCHEMA, "schema de inventário inválido")
    _check(manifest.get("sourceMasterSha") == PINNED_MASTER, "SHA mestre do acervo divergente")
    originals = manifest.get("sourcePaths")
    excluded = manifest.get("authorizedRemovalsFromSource")
    _check(isinstance(originals, list) and len(originals) == len(set(originals)), "sourcePaths duplicados")
    _check(isinstance(excluded, list) and len(excluded) == 2 and len(excluded) == len(set(excluded)), "somente duas exclusões autorizadas")
    _check(all(isinstance(p, str) and p.startswith("Documentos/") and Path(p).suffix.lower() in EXTENSIONS for p in originals), "caminho de origem inválido")
    _check(set(excluded) == APPROVED_REMOVALS, "exclusões não coincidem com a decisão expressa do PO")
    _check(set(excluded).issubset(set(originals)), "exclusão não pertence ao acervo original")
    _check(len(originals) == manifest.get("sourceCount") == 45, "origem congelada 45 incorreta")
    expected = set(originals) - set(excluded)
    _check(len(expected) == manifest.get("expectedCurrentCount") == 43, "saldos 45-2 incorretos")
    _check(tracked == expected, "arquivos binários faltantes, restaurados ou acrescentados fora do inventário autorizado")
    rows = report.get("files")
    _check(isinstance(rows, list) and report.get("total_files") == len(expected) == len(rows), "relatório de contagem divergente")
    _check(all(isinstance(row, dict) and isinstance(row.get("path"), str) for row in rows), "linhas de auditoria inválidas")
    _check(len({row["path"] for row in rows}) == len(rows), "linhas duplicadas na auditoria")
    _check({row["path"] for row in rows} == expected, "cobertura da auditoria não corresponde ao inventário")
    _check(all(row.get("type") == Path(row["path"]).suffix.lower()
               and "error" not in row and not row.get("text_empty") for row in rows),
           "arquivo não lido, vazio ou tipo divergente")
    _check(report.get("not_audited") == [], "há documento não auditado")
    _check(report.get("verified_mentions_before") == sum(row.get("before", 0) for row in rows), "total antes incompatível")
    _check(report.get("verified_mentions_after") == sum(row.get("after", 0) for row in rows), "total depois incompatível")
    return len(expected)


def validate_inventory(root: Path, report: dict) -> int:
    root = root.resolve()
    manifest = json.loads((root / MANIFEST).read_text(encoding="utf8"))
    output = subprocess.run(["git", "-C", str(root), "ls-files", "-z"],
                            check=True, capture_output=True).stdout
    tracked = {os.fsdecode(p) for p in output.split(b"\x00") if p
               and Path(os.fsdecode(p)).suffix.lower() in EXTENSIONS}
    return validate_manifest(manifest, report, tracked)


def main() -> None:
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--root", type=Path, required=True)
    cli.add_argument("--report", type=Path, required=True)
    args = cli.parse_args()
    report = json.loads(args.report.read_text(encoding="utf8"))
    count = validate_inventory(args.root, report)
    print(json.dumps({"schema": SCHEMA, "filesAudited": count,
                      "authorizedRemovals": 2, "notAudited": 0}))


if __name__ == "__main__":
    main()
