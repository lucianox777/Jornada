#!/usr/bin/env python3
"""Fail-closed technical closure gate for the current Jornada schema.

The complete v4.05 closure battery is preserved verbatim in
technical-closure-gate-v405.py. This wrapper first requires that historical
battery to pass and then proves the current 3.70 runtime/schema/release
metadata, preventing a legacy 3.69 substring from masquerading as current
readiness.
"""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LEGACY_GATE = ROOT / "scripts" / "technical-closure-gate-v405.py"
READINESS = ROOT / "src" / "Jornada.Api" / "ApiHealth.cs"
CURRENT_INSTALLER = ROOT / "database" / "Jornada_Fase1_v3.70.sql"
SCHEMA_370 = ROOT / "database" / "migrations" / "20260910_Schema_Consolidation_370.sql"
SCHEMA_APPROVALS = ROOT / "config" / "governance" / "schema-approvals.json"
LOCK_PROVENANCE = ROOT / "config" / "release" / "nuget-lock-provenance.json"
MATERIALIZER = ROOT / "scripts" / "materialize-sql-installer.py"


def fail(message: str) -> None:
    raise SystemExit(f"TECHNICAL CLOSURE GATE: FAIL: {message}")


def require(text: str, snippets: tuple[str, ...], context: str) -> None:
    for snippet in snippets:
        if snippet not in text:
            fail(f"{context}: trecho obrigatório ausente: {snippet}")


def run_legacy_closure() -> None:
    result = subprocess.run(
        [sys.executable, str(LEGACY_GATE)],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        if result.stdout:
            print(result.stdout, end="", file=sys.stderr)
        if result.stderr:
            print(result.stderr, end="", file=sys.stderr)
        fail("bateria histórica v4.05 falhou")


def validate_current_schema() -> None:
    readiness = READINESS.read_text(encoding="utf-8")
    require(
        readiness,
        (
            "Jornada.BaseNormativa",
            "@base=N'3.62'",
            "Jornada.SolutionSchema",
            "NOT (@solution=N'3.69')",
            "@solution=N'3.70'",
            "SQL_SCHEMA_INCOMPATIVEL",
            "identidade.cpf_ancora",
            "identidade.pessoa_origem_progressiva",
            "identidade.composicao_publicacao",
            "identidade.blocking_chave",
            "identidade.linkage_ruleset_passe_campo",
        ),
        "readiness corrente 3.70",
    )

    installer = CURRENT_INSTALLER.read_text(encoding="utf-8")
    require(
        installer,
        (
            "Microsoft SQL Server é a tecnologia relacional normativa.",
            ":r database/migrations/20260910_Schema_Consolidation_370.sql",
        ),
        "instalador canônico 3.70",
    )

    consolidation = SCHEMA_370.read_text(encoding="utf-8")
    require(
        consolidation,
        (
            "Jornada.SolutionSchema",
            "3.70",
            "Jornada.BaseNormativa",
            "3.62",
        ),
        "consolidação final 3.70",
    )

    approvals = json.loads(SCHEMA_APPROVALS.read_text(encoding="utf-8"))
    if approvals.get("baseNormativa") != "3.62" or approvals.get("solutionSchema") != "3.70":
        fail("schema-approvals deve declarar Base 3.62 / Solution 3.70")

    provenance = json.loads(LOCK_PROVENANCE.read_text(encoding="utf-8"))
    if provenance.get("solutionSchema") != "v3.70":
        fail("proveniência NuGet deve declarar SolutionSchema v3.70")


def validate_standalone_installer() -> None:
    if not MATERIALIZER.is_file():
        fail("materializador do instalador autocontido ausente")
    with tempfile.TemporaryDirectory(prefix="jornada-sql-installer-") as tmp:
        output = Path(tmp) / "Jornada_Fase1_v3.70_standalone.sql"
        result = subprocess.run(
            [
                sys.executable,
                str(MATERIALIZER),
                "--root",
                str(ROOT),
                "--output",
                str(output),
            ],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        if result.returncode != 0:
            fail("materialização do instalador autocontido falhou: " + (result.stderr or result.stdout).strip())
        payload = output.read_text(encoding="utf-8")
        if ":r " in payload or "\n:r" in payload:
            fail("instalador materializado ainda contém diretiva :r")
        require(
            payload,
            (
                "Microsoft SQL Server é a tecnologia relacional normativa da Jornada.",
                "Jornada.SolutionSchema",
                "3.70",
            ),
            "instalador autocontido 3.70",
        )


def main() -> int:
    run_legacy_closure()
    validate_current_schema()
    validate_standalone_installer()
    print(
        "TECHNICAL CLOSURE GATE: OK "
        "(bateria v4.05 preservada; readiness corrente Base 3.62 / SolutionSchema 3.70; "
        "governança/proveniência 3.70; instalador SQL Server canônico e autocontido verificados)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
