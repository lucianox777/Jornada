#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import re
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
DATABASE = ROOT / "database"
MANIFEST = DATABASE / "migrations" / "manifest.txt"
INSTALLER = DATABASE / "Jornada_Fase1_v3.70.sql"
BASELINE = "Jornada_Fase1.sql"
LEDGER_MIGRATION = "migrations/20260916_Schema_Migration_Ledger.sql"
FINAL_MIGRATION = "migrations/20260910_Schema_Consolidation_370.sql"
ENTRY_PATTERN = re.compile(r"[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\.sql\Z")


def source_path(relative: str) -> Path:
    return DATABASE.joinpath(*PurePosixPath(relative).parts)


def read_manifest() -> list[str]:
    entries: list[str] = []
    for raw in MANIFEST.read_text(encoding="utf-8-sig").splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        path = PurePosixPath(line)
        if (
            not ENTRY_PATTERN.fullmatch(line)
            or path.is_absolute()
            or any(part in {".", "..", ""} for part in path.parts)
        ):
            raise SystemExit(f"SCHEMA MANIFEST: entrada inválida: {line}")
        source = source_path(line)
        if not source.is_file():
            raise SystemExit(f"SCHEMA MANIFEST: arquivo ausente: {line}")
        entries.append(line)

    if not entries:
        raise SystemExit("SCHEMA MANIFEST: manifesto vazio")
    if len(entries) != len(set(entries)):
        raise SystemExit("SCHEMA MANIFEST: entradas duplicadas")
    if entries[0] != LEDGER_MIGRATION:
        raise SystemExit(
            f"SCHEMA MANIFEST: primeira entrada deve ser {LEDGER_MIGRATION}; atual={entries[0]}"
        )
    if entries[-1] != FINAL_MIGRATION:
        raise SystemExit(
            f"SCHEMA MANIFEST: última entrada deve ser {FINAL_MIGRATION}; atual={entries[-1]}"
        )
    if FINAL_MIGRATION in entries[:-1]:
        raise SystemExit("SCHEMA MANIFEST: consolidação final apareceu antes do fim")
    return entries


def checksum(relative: str) -> str:
    return hashlib.sha256(source_path(relative).read_bytes()).hexdigest()


def ledger_guard(relative: str) -> list[str]:
    digest = checksum(relative)
    return [
        f"IF EXISTS(SELECT 1 FROM jornada.schema_migration WHERE migration_name=N'{relative}' AND sha256<>'{digest}')",
        f"    THROW 51710, 'Checksum divergente para migração versionada: {relative}', 1;",
        f"IF NOT EXISTS(SELECT 1 FROM jornada.schema_migration WHERE migration_name=N'{relative}')",
        f"    INSERT jornada.schema_migration(migration_name,sha256) VALUES(N'{relative}','{digest}');",
        "GO",
    ]


def render_installer(entries: list[str]) -> str:
    lines = [
        "-- Jornada do Cidadão - Fase 1 - baseline operacional consolidado v3.70",
        "-- GERADO de database/migrations/manifest.txt por scripts/schema-manifest.py.",
        "-- Não editar a lista de includes/checksums manualmente; altere o manifesto e regenere.",
        "-- Microsoft SQL Server é a tecnologia relacional normativa.",
        "",
        ":on error exit",
        f":r database/{BASELINE}",
    ]
    for entry in entries:
        lines.append(f":r database/{entry}")
        lines.extend(ledger_guard(entry))
    return "\n".join(lines) + "\n"


def render_flat(entries: list[str]) -> str:
    parts = [
        "-- Jornada do Cidadão - Fase 1 - SolutionSchema 3.70",
        "-- DDL achatado gerado do mesmo manifesto canônico; apropriado para executores sem suporte a :r.",
        "",
        f"-- BEGIN {BASELINE}",
        source_path(BASELINE).read_text(encoding="utf-8-sig").rstrip(),
        f"-- END {BASELINE}",
        "",
    ]
    for relative in entries:
        text = source_path(relative).read_text(encoding="utf-8-sig").rstrip()
        parts.append(f"-- BEGIN {relative}")
        parts.append(text)
        parts.append(f"-- END {relative}")
        parts.extend(ledger_guard(relative))
        parts.append("")
    return "\n".join(parts).rstrip() + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description="Valida e renderiza o schema canônico SQL Server da Jornada.")
    parser.add_argument("--write-installer", action="store_true", help="regenera database/Jornada_Fase1_v3.70.sql")
    parser.add_argument("--check", action="store_true", help="falha se o instalador versionado divergir do manifesto")
    parser.add_argument("--flatten-output", help="gera DDL achatado em um caminho de saída")
    parser.add_argument("--print-count", action="store_true", help="imprime a quantidade de entradas do manifesto")
    args = parser.parse_args()

    entries = read_manifest()
    expected = render_installer(entries)

    if args.write_installer:
        INSTALLER.write_text(expected, encoding="utf-8", newline="\n")

    if args.check:
        actual = INSTALLER.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
        if actual != expected:
            raise SystemExit(
                "SCHEMA MANIFEST: Jornada_Fase1_v3.70.sql diverge do manifesto; execute "
                "python scripts/schema-manifest.py --write-installer"
            )

    if args.flatten_output:
        output = Path(args.flatten_output)
        if not output.is_absolute():
            output = ROOT / output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(render_flat(entries), encoding="utf-8", newline="\n")

    if args.print_count:
        print(len(entries))

    if not any((args.write_installer, args.check, args.flatten_output, args.print_count)):
        parser.error("informe ao menos uma ação")


if __name__ == "__main__":
    main()
