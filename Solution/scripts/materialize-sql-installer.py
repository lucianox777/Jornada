#!/usr/bin/env python3
"""Materializa o instalador SQL Server canônico em um único .sql sem diretivas :r.

A fonte editável continua sendo database/Jornada_Fase1_v3.70.sql e seus includes.
O artefato materializado é destinado a entrega/execução por DBA ou ferramenta de deploy
sem depender da árvore de arquivos no host que executa sqlcmd.
"""
from __future__ import annotations

import argparse
import hashlib
import re
from pathlib import Path

INCLUDE = re.compile(r"^\s*:r\s+(.+?)\s*$", re.IGNORECASE)
SQLCMD_ONLY = re.compile(r"^\s*:on\s+error\s+exit\s*$", re.IGNORECASE)


def fail(message: str) -> None:
    raise SystemExit(f"SQL INSTALLER MATERIALIZER: FAIL: {message}")


def materialize(path: Path, root: Path, stack: tuple[Path, ...]) -> str:
    resolved = path.resolve()
    try:
        resolved.relative_to(root)
    except ValueError:
        fail(f"include fora da raiz permitida: {resolved}")
    if resolved in stack:
        cycle = " -> ".join(p.relative_to(root).as_posix() for p in (*stack, resolved))
        fail(f"ciclo de include: {cycle}")
    if not resolved.is_file():
        fail(f"arquivo ausente: {resolved.relative_to(root)}")

    output: list[str] = []
    rel = resolved.relative_to(root).as_posix()
    output.append(f"-- BEGIN MATERIALIZED SOURCE: {rel}\n")
    for raw in resolved.read_text(encoding="utf-8-sig").splitlines(keepends=True):
        line = raw.rstrip("\r\n")
        match = INCLUDE.match(line)
        if match:
            include_text = match.group(1).strip().strip('"')
            include_path = (root / include_text).resolve()
            output.append(materialize(include_path, root, (*stack, resolved)))
            continue
        if SQLCMD_ONLY.match(line):
            output.append("-- sqlcmd directive removed by materializer: :on error exit\n")
            continue
        output.append(raw if raw.endswith(("\n", "\r")) else raw + "\n")
    output.append(f"-- END MATERIALIZED SOURCE: {rel}\n")
    return "".join(output)


def main() -> int:
    parser = argparse.ArgumentParser(description="Gera instalador SQL Server autocontido a partir da fonte canônica com :r.")
    parser.add_argument("--root", default=".", help="Raiz da Solution")
    parser.add_argument("--source", default="database/Jornada_Fase1_v3.70.sql")
    parser.add_argument("--output", default=".local/release/Jornada_Fase1_v3.70_standalone.sql")
    parser.add_argument("--sha256-output", default=None)
    args = parser.parse_args()

    root = Path(args.root).resolve()
    source = (root / args.source).resolve()
    output = (root / args.output).resolve()
    text = materialize(source, root, ())

    if INCLUDE.search(text):
        fail("artefato final ainda contém diretiva :r")
    header = (
        "-- Jornada do Cidadão - SQL Server - instalador materializado\n"
        "-- GERADO; não editar diretamente. Fonte: database/Jornada_Fase1_v3.70.sql\n"
        "-- Microsoft SQL Server é a tecnologia relacional normativa da Jornada.\n\n"
    )
    payload = header + text
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(payload, encoding="utf-8", newline="\n")
    digest = hashlib.sha256(output.read_bytes()).hexdigest()

    if args.sha256_output:
        sha_path = (root / args.sha256_output).resolve()
        sha_path.parent.mkdir(parents=True, exist_ok=True)
        sha_path.write_text(f"{digest}  {output.name}\n", encoding="utf-8")

    print(f"SQL INSTALLER MATERIALIZER: OK output={output.relative_to(root)} sha256={digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
