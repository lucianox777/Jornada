#!/usr/bin/env python3
"""Fail-closed technical closure gate for the current Jornada schema.

Enquanto a Jornada estiver em pré-implantação (`deployed=false`), o fechamento
valida apenas o estado técnico corrente e invariantes ainda aplicáveis. Versões
internas anteriores não constituem baseline histórica de regressão. Quando
houver implantação real, a política de compatibilidade passa a exigir uma
baseline operacional explícita e versionada.
"""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
READINESS = ROOT / "src" / "Jornada.Api" / "ApiHealth.cs"
CURRENT_INSTALLER = ROOT / "database" / "Jornada_Fase1_v3.70.sql"
SCHEMA_370 = ROOT / "database" / "migrations" / "20260910_Schema_Consolidation_370.sql"
SCHEMA_MANIFEST = ROOT / "database" / "migrations" / "manifest.txt"
SCHEMA_MANIFEST_TOOL = ROOT / "scripts" / "schema-manifest.py"
SCHEMA_APPROVALS = ROOT / "config" / "governance" / "schema-approvals.json"
LOCK_PROVENANCE = ROOT / "config" / "release" / "nuget-lock-provenance.json"
COMPATIBILITY_POLICY = ROOT / "config" / "release" / "contract-compatibility-policy.json"
MATERIALIZER = ROOT / "scripts" / "materialize-sql-installer.py"
RELEASE_INFO = ROOT.parent / "RELEASE_INFO.txt"
PRE_DEPLOYMENT_MODE = "PRE_DEPLOYMENT_NO_HISTORICAL_BASELINE"


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


def validate_predeployment_compatibility_policy() -> None:
    policy = json.loads(COMPATIBILITY_POLICY.read_text(encoding="utf-8"))
    if policy.get("deployed") is not False:
        fail(
            "deployed=true exige baseline operacional pós-implantação; "
            "o fechamento técnico pré-implantação não pode prosseguir"
        )
    if policy.get("mode") != PRE_DEPLOYMENT_MODE:
        fail(f"policy pré-implantação deve declarar mode={PRE_DEPLOYMENT_MODE}")
    preserved = set(policy.get("preserveBeforeDeployment") or [])
    required = {
        "architectural-invariants",
        "current-contract-validation",
        "unit-algorithm-correctness",
        "integration-correctness",
        "security-and-data-minimization",
        "ground-truth-anti-leakage",
    }
    missing = sorted(required - preserved)
    if missing:
        fail("policy pré-implantação deixou de preservar invariantes obrigatórios: " + ", ".join(missing))


def validate_current_schema() -> None:
    readiness = READINESS.read_text(encoding="utf-8")
    require(
        readiness,
        (
            "Jornada.BaseNormativa",
            "@base=N'3.62'",
            "Jornada.SolutionSchema",
            "@solution=N'3.70'",
            "SQL_SCHEMA_INCOMPATIVEL",
            "jornada.schema_migration",
            "identidade.cpf_ancora",
            "identidade.pessoa_origem_progressiva",
            "identidade.composicao_publicacao",
            "auditoria.modelo_linkage_estado_evento",
            "auditoria.linkage_conferencia_evidencia",
            "identidade.blocking_chave",
            "identidade.linkage_ruleset_passe_campo",
            "ref.frequencia_nome_versao",
            "identidade.linkage_quality_estimate",
            "auditoria.modelo_linkage_estado_evento",
            "auditoria.linkage_conferencia_evidencia",
            "serving.v_bi_qualidade_resolucao_operacional",
            "nome_publicacao_normalizado",
        ),
        "readiness corrente 3.70",
    )

    if not SCHEMA_MANIFEST_TOOL.is_file():
        fail("renderizador/validador do manifesto de schema ausente")
    manifest_check = subprocess.run(
        [sys.executable, str(SCHEMA_MANIFEST_TOOL), "--check"],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    if manifest_check.returncode != 0:
        fail("instalador canônico diverge do manifesto: " + (manifest_check.stderr or manifest_check.stdout).strip())

    manifest = SCHEMA_MANIFEST.read_text(encoding="utf-8")
    require(
        manifest,
        (
            "migrations/20260916_Schema_Migration_Ledger.sql",
            "Jornada_Identidade_Progressiva.sql",
            "migrations/20260912_Nome_Mae_Anulavel.sql",
            "migrations/20260912_Frequencia_Nomes_Referencia.sql",
            "migrations/20260912_Frequencia_Nomes_Cobertura.sql",
            "migrations/20260912_Gold_Nome_Publicacao.sql",
            "migrations/20260912_Linkage_Run_Frequencia_Nome_Proveniencia.sql",
            "migrations/20260913_BI_Qualidade_Resolucao.sql",
            "migrations/20260913_BI_Qualidade_Resolucao_Gestor_Real.sql",
            "migrations/20260913_BI_Qualidade_Resolucao_Estrato_Cpf.sql",
            "migrations/20260915_Linkage_LogOdds_Margin.sql",
            "migrations/20260915_Linkage_Model_Promotion_Contract.sql",
            "migrations/20260920_Linkage_Model_Promotion_Ledger.sql",
            "migrations/20260920_Linkage_Implementation_Conference_Evidence.sql",
            "migrations/20260910_Schema_Consolidation_370.sql",
        ),
        "manifesto canônico 3.70",
    )

    installer = CURRENT_INSTALLER.read_text(encoding="utf-8")
    require(
        installer,
        (
            "GERADO de database/migrations/manifest.txt",
            "Microsoft SQL Server é a tecnologia relacional normativa.",
            ":r database/migrations/20260916_Schema_Migration_Ledger.sql",
            ":r database/migrations/20260912_Frequencia_Nomes_Referencia.sql",
            ":r database/migrations/20260913_BI_Qualidade_Resolucao.sql",
            ":r database/migrations/20260920_Linkage_Model_Promotion_Ledger.sql",
            ":r database/migrations/20260920_Linkage_Implementation_Conference_Evidence.sql",
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
            "jornada.schema_migration",
            "ref.frequencia_nome_versao",
            "identidade.linkage_quality_estimate",
            "serving.v_bi_qualidade_resolucao_operacional",
            "NULLABILITY:",
            "COLUMN_SHAPE:identidade.linkage_resultado.margem decimal(18,8) NULL",
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
                "ref.frequencia_nome_versao",
                "identidade.linkage_quality_estimate",
                "identidade.tr_modelo_linkage_promotion_contract",
            ),
            "instalador autocontido 3.70",
        )


def main() -> int:
    validate_predeployment_compatibility_policy()
    validate_current_schema()
    release_state = validate_release_boundary()
    validate_standalone_installer()
    print(
        "TECHNICAL CLOSURE GATE: OK "
        "(pré-implantação sem baseline histórica; invariantes correntes preservados; "
        "readiness Base 3.62 / SolutionSchema 3.70; manifesto/instalador/consolidação alinhados; "
        "governança/proveniência 3.70; "
        f"{release_state}; instalador SQL Server canônico e autocontido verificados)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
