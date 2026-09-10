#!/usr/bin/env python3
"""Fail-closed technical closure gate for the current Jornada schema.

The frozen v4.05 battery remains responsible for historical technical
invariants, but it must not pin mutable current-release metadata. This wrapper
proves the current 3.70 runtime/schema state and validates RELEASE_INFO at the
release boundary: while v5.00 has not been cut, RELEASE_INFO may consistently
describe the last published v4.05 release; when v5.00 is cut, its engineering
version, schema and source tag must move together to v5.00 / v3.70.
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
RELEASE_INFO = ROOT.parent / "RELEASE_INFO.txt"


def fail(message: str) -> None:
    raise SystemExit(f"TECHNICAL CLOSURE GATE: FAIL: {message}")


def require(text: str, snippets: tuple[str, ...], context: str) -> None:
    for snippet in snippets:
        if snippet not in text:
            fail(f"{context}: trecho obrigatório ausente: {snippet}")


def parse_release_info() -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in RELEASE_INFO.read_text(encoding="utf-8").splitlines():
        if not raw or raw.lstrip().startswith("#") or "=" not in raw:
            continue
        key, value = raw.split("=", 1)
        values[key.strip()] = value.strip()
    return values


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


def validate_release_boundary() -> str:
    info = parse_release_info()
    required = ("base_normativa", "solution_engenharia", "schema_base_normativa", "schema_solution", "source_git_tag")
    missing = [key for key in required if not info.get(key)]
    if missing:
        fail("RELEASE_INFO sem campos obrigatórios: " + ", ".join(missing))

    if info["base_normativa"] != "v3.64" or info["schema_base_normativa"] != "v3.62":
        fail("RELEASE_INFO deve preservar Base Normativa v3.64 / schema-base v3.62 nesta consolidação")

    engineering = info["solution_engenharia"]
    schema = info["schema_solution"]
    tag = info["source_git_tag"]

    if engineering == "v4.05":
        if schema != "v3.69" or tag != "jornada-solution-v4.05":
            fail(
                "RELEASE_INFO da última release publicada v4.05 está inconsistente; "
                "antes do corte v5.00 deve permanecer v4.05 / v3.69 / jornada-solution-v4.05"
            )
        return "última release publicada v4.05 preservada; branch técnica corrente em 3.70"

    if engineering == "v5.00":
        if schema != "v3.70" or tag != "jornada-solution-v5.00":
            fail(
                "corte v5.00 exige atualização atômica de solution_engenharia=v5.00, "
                "schema_solution=v3.70 e source_git_tag=jornada-solution-v5.00"
            )
        return "metadados de release v5.00 / schema 3.70 coerentes"

    fail(
        "RELEASE_INFO em estado de release não reconhecido para esta consolidação: "
        f"solution_engenharia={engineering}"
    )
    raise AssertionError("unreachable")


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
    release_state = validate_release_boundary()
    validate_standalone_installer()
    print(
        "TECHNICAL CLOSURE GATE: OK "
        "(bateria v4.05 preservada sem pin de RELEASE_INFO corrente; "
        "readiness Base 3.62 / SolutionSchema 3.70; governança/proveniência 3.70; "
        f"{release_state}; instalador SQL Server canônico e autocontido verificados)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
