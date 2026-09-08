#!/usr/bin/env python3
"""Static fail-closed gate for v4.05 technical closure, runtime contract conformance, SQL target compatibility, architecture, supply-chain, governance and HML execution contracts."""
from __future__ import annotations
import hashlib
import json
import re
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
DDL = ROOT / "database" / "Jornada_Fase1.sql"
SEED = ROOT / "database" / "Jornada_Seed_Dev.sql"
RESERVATION = ROOT / "src" / "Jornada.Processor.Worker" / "SqlProcessorRepository.Reservation.cs"
PHONE_CS = ROOT / "src" / "Jornada.Processor.Worker" / "TransversalAttributeInstanceKey.cs"
MATERIALIZATION_CS = ROOT / "src" / "Jornada.Processor.Worker" / "SqlProcessorRepository.Materialization.cs"
READINESS_CS = ROOT / "src" / "Jornada.Api" / "ApiHealth.cs"
VECTORS = ROOT / "tests" / "fixtures" / "phone" / "telefone-br-canonico-v2.json"
EMAIL_VECTORS = ROOT / "tests" / "fixtures" / "email" / "email-canonico-v2.json"
IDENTITY_TESTS = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "IdentityGovernanceTests.cs"
IDENTITY_ERROR_TESTS = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "IdentityGovernanceErrorContractTests.cs"
OPERATIONAL_TESTS = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "OperationalAtomicityTests.cs"
PROCESSOR_TESTS = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "ProcessorRepositoryTests.cs"
PHONE_INTEGRATION = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "PhoneNormalizationConformanceTests.cs"
EMAIL_INTEGRATION = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "EmailNormalizationConformanceTests.cs"
API_READINESS_TESTS = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "ApiReadinessTests.cs"
RUNTIME_SMOKE = ROOT / "database" / "Jornada_Runtime_Smoke.sql"
CI_WORKFLOW = ROOT.parent / ".github" / "workflows" / "ci.yml"
LOCAL_RUNTIME_SH = ROOT / "scripts" / "local-sql-runtime-smoke.sh"
LOCAL_RUNTIME_PS = ROOT / "scripts" / "local-sql-runtime-smoke.ps1"
SBOM_GENERATOR = ROOT / "scripts" / "generate-sbom.py"
PREDECESSOR_GATE = ROOT / "scripts" / "predecessor-integrity-gate.py"
SOURCE_GATE = ROOT / "scripts" / "release-source-gate.py"
SOURCE_BUNDLE_BUILDER = ROOT / "scripts" / "build-release-source-bundle.sh"
RELEASE_INFO = ROOT.parent / "RELEASE_INFO.txt"
SOURCE_PROVENANCE = ROOT.parent / "SOURCE_PROVENANCE.json"
REQ_DIR = ROOT.parent / "Documentos" / "Requisitos"
MASTER_REQ_MD = REQ_DIR / "00_Indice_Mestre_Requisitos_Jornada_v1.0.md"
MASTER_REQ_DOCX = REQ_DIR / "00_Indice_Mestre_Requisitos_Jornada_v1.0.docx"
MASTER_REQ_PDF = REQ_DIR / "00_Indice_Mestre_Requisitos_Jornada_v1.0.pdf"
BUSINESS_REQ_MD = REQ_DIR / "01_Requisitos_de_Negocio_Jornada_v1.1.md"
BUSINESS_REQ_DOCX = REQ_DIR / "01_Requisitos_de_Negocio_Jornada_v1.1.docx"
BUSINESS_REQ_PDF = REQ_DIR / "01_Requisitos_de_Negocio_Jornada_v1.1.pdf"
FUNCTIONAL_REQ_MD = REQ_DIR / "02_Requisitos_Funcionais_Jornada_v1.0.md"
FUNCTIONAL_REQ_DOCX = REQ_DIR / "02_Requisitos_Funcionais_Jornada_v1.0.docx"
FUNCTIONAL_REQ_PDF = REQ_DIR / "02_Requisitos_Funcionais_Jornada_v1.0.pdf"
NONFUNCTIONAL_REQ_MD = REQ_DIR / "03_Requisitos_Nao_Funcionais_Jornada_v1.0.md"
NONFUNCTIONAL_REQ_DOCX = REQ_DIR / "03_Requisitos_Nao_Funcionais_Jornada_v1.0.docx"
NONFUNCTIONAL_REQ_PDF = REQ_DIR / "03_Requisitos_Nao_Funcionais_Jornada_v1.0.pdf"
TECHNICAL_REQ_MD = REQ_DIR / "04_Requisitos_Tecnicos_Jornada_v1.1.md"
TECHNICAL_REQ_DOCX = REQ_DIR / "04_Requisitos_Tecnicos_Jornada_v1.1.docx"
TECHNICAL_REQ_PDF = REQ_DIR / "04_Requisitos_Tecnicos_Jornada_v1.1.pdf"
TRACEABILITY_REQ_MD = REQ_DIR / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.md"
TRACEABILITY_REQ_DOCX = REQ_DIR / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.docx"
TRACEABILITY_REQ_PDF = REQ_DIR / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.pdf"
REQUIREMENTS_MAP = REQ_DIR / "requirements-map.json"
RUNTIME_EVIDENCE_V395 = ROOT.parent / "Documentos" / "Evidencia_Runtime_v3.95_2026-09-03.md"
RUNTIME_EVIDENCE_V399 = ROOT.parent / "Documentos" / "Evidencia_Runtime_v3.99_2026-09-03.md"
ROOT_GITIGNORE = ROOT.parent / ".gitignore"
GIT_RELEASE_RUNBOOK = ROOT / "docs" / "Runbook_Git_Release.md"
RELEASE_INFO_SH = ROOT / "scripts" / "generate-release-info.sh"
RELEASE_INFO_PS = ROOT / "scripts" / "generate-release-info.ps1"
TEST_EVIDENCE_GATE = ROOT / "scripts" / "test-evidence-gate.py"
PERFORMANCE_EVIDENCE_GATE = ROOT / "scripts" / "performance-evidence-gate.py"
BRONZE_RESTORE_EVIDENCE_GATE = ROOT / "scripts" / "bronze-restore-evidence-gate.py"
POWERBI_STATIC_GATE = ROOT / "scripts" / "powerbi-static-gate.py"
LOCAL_BACKUP_SH = ROOT / "scripts" / "local-backup-restore-drill.sh"
LOCAL_BACKUP_PS = ROOT / "scripts" / "local-backup-restore-drill.ps1"
LOCAL_FAULT_SH = ROOT / "scripts" / "local-fault-injection.sh"
LOCAL_FAULT_PS = ROOT / "scripts" / "local-fault-injection.ps1"
LOCAL_SCALE_SH = ROOT / "scripts" / "local-scale.sh"
LOCAL_SCALE_PS = ROOT / "scripts" / "local-scale.ps1"
HML_CONFIG_GATE = ROOT / "scripts" / "hml-config-gate.py"
HML_READINESS_GATE = ROOT / "scripts" / "hml-readiness-gate.sh"
HML_READINESS_GATE_PS = ROOT / "scripts" / "hml-readiness-gate.ps1"
LINKAGE_EVIDENCE_GATE = ROOT / "scripts" / "linkage-evaluation-evidence-gate.py"
LINKAGE_EVAL_SMOKE = ROOT / "scripts" / "linkage-evaluation-smoke.sh"
HML_PARAMETERS = ROOT / "config" / "hml" / "parameters.json"
HML_PERFORMANCE_BASELINE = ROOT / "config" / "hml" / "performance-baseline.json"
HML_LINKAGE_POLICY = ROOT / "config" / "hml" / "linkage-evaluation-policy.json"
AUTHORIZATION_MATRIX_GATE = ROOT / "scripts" / "authorization-matrix-gate.py"
SECURITY_SURFACE_GATE = ROOT / "scripts" / "security-surface-gate.py"
COMPATIBILITY_MATRIX_GATE = ROOT / "scripts" / "compatibility-matrix-gate.py"
OBSERVABILITY_GATE = ROOT / "scripts" / "observability-contract-gate.py"
HML_STALENESS_GATE = ROOT / "scripts" / "hml-staleness-gate.py"
BRONZE_DEEP_GATE = ROOT / "scripts" / "bronze-deep-evidence-gate.py"
UPGRADE_INVARIANT_GATE = ROOT / "scripts" / "upgrade-invariant-gate.py"
UPGRADE_INVARIANTS_SQL = ROOT / "database" / "Jornada_Upgrade_Invariants.sql"
AUTHORIZATION_MATRIX = ROOT / "config" / "security" / "authorization-matrix.json"
DATA_MINIMIZATION_POLICY = ROOT / "config" / "security" / "data-minimization-policy.json"
COMPATIBILITY_MATRIX = ROOT / "config" / "release" / "compatibility-matrix.json"
OBSERVABILITY_CATALOG = ROOT / "config" / "observability" / "metrics-slo-catalog.json"
CALIBRATION_VALIDITY = ROOT / "config" / "hml" / "calibration-validity-policy.json"
JORNADA_TELEMETRY = ROOT / "src" / "Jornada.Contracts" / "JornadaTelemetry.cs"
BRONZE_VERIFY = ROOT / "src" / "Jornada.Bronze.Verify" / "Program.cs"
AUTHORIZATION_TESTS = ROOT / "tests" / "Jornada.Tests" / "Unit" / "AuthorizationMatrixContractTests.cs"
TELEMETRY_TESTS = ROOT / "tests" / "Jornada.Tests" / "Unit" / "TelemetryContractTests.cs"
GOVERNANCE_GATE = ROOT / "scripts" / "governance-readiness-gate.py"
SCHEDULER_GATE = ROOT / "scripts" / "scheduler-contract-gate.py"
ENVIRONMENT_GATE = ROOT / "scripts" / "environment-preflight-gate.py"
SQL_PERFORMANCE_GATE = ROOT / "scripts" / "sql-performance-evidence-gate.py"
API_PROJECTION_GATE = ROOT / "scripts" / "api-projection-evidence-gate.py"
SQL_PERFORMANCE_SQL = ROOT / "database" / "Jornada_HML_SQL_Performance_Evidence.sql"
SCHEMA_APPROVALS = ROOT / "config" / "governance" / "schema-approvals.json"
RETENTION_DR_POLICY = ROOT / "config" / "governance" / "retention-dr-policy.json"
IDENTITY_LIFECYCLE_POLICY = ROOT / "config" / "governance" / "identity-pending-lifecycle.json"
SCHEDULER_JOBS = ROOT / "config" / "operations" / "scheduler-jobs.json"
ENVIRONMENT_REQUIREMENTS = ROOT / "config" / "release" / "environment-requirements.json"
SQL_PERFORMANCE_POLICY = ROOT / "config" / "hml" / "sql-performance-policy.json"
API_PROJECTION_POLICY = ROOT / "config" / "hml" / "api-projection-load-policy.json"
API_PROJECTION_HARNESS = ROOT / "scripts" / "api-projection-load-harness.py"
BACKWARD_COMPAT_GATE = ROOT / "scripts" / "contract-backward-compatibility-gate.py"
DDL_DESTRUCTIVE_GATE = ROOT / "scripts" / "ddl-destructive-change-gate.py"
DEPENDENCY_DRIFT_GATE = ROOT / "scripts" / "dependency-drift-gate.py"
WORKFLOW_PIN_GATE = ROOT / "scripts" / "workflow-action-pin-gate.py"
SOURCE_SANITY_GATE = ROOT / "scripts" / "source-sanity-gate.py"
COVERAGE_GATE = ROOT / "scripts" / "coverage-evidence-gate.py"
VULNERABILITY_GATE = ROOT / "scripts" / "vulnerability-evidence-gate.py"
SARIF_GATE = ROOT / "scripts" / "sarif-security-gate.py"
DETERMINISTIC_BUILD_GATE = ROOT / "scripts" / "deterministic-build-gate.sh"
DDL_CHANGE_POLICY = ROOT / "config" / "release" / "ddl-change-policy.json"
COVERAGE_BASELINE = ROOT / "config" / "release" / "coverage-baseline.json"
DEPENDENCY_DRIFT_POLICY = ROOT / "config" / "release" / "dependency-drift-policy.json"
SOURCE_SANITY_POLICY = ROOT / "config" / "security" / "source-sanity-policy.json"
DIRECTORY_BUILD_PROPS = ROOT / "Directory.Build.props"
TEST_CSPROJ = ROOT / "tests" / "Jornada.Tests" / "Jornada.Tests.csproj"
INTEGRATION_TEST_CSPROJ = ROOT / "tests" / "Jornada.Integration.Tests" / "Jornada.Integration.Tests.csproj"
NUGET_LOCK_PROVENANCE = ROOT / "config" / "release" / "nuget-lock-provenance.json"
NUGET_LOCK_PROVENANCE_GATE = ROOT / "scripts" / "nuget-lock-provenance-gate.py"
TEST_RUNBOOK = ROOT / "docs" / "Runbook_Testes_Tecnicos.md"
OPENAPI_RUNTIME_TESTS = ROOT / "tests" / "Jornada.Tests" / "Unit" / "OpenApiRuntimeConformanceTests.cs"
JSON_SCHEMA_META_GATE = ROOT / "scripts" / "json-schema-meta-gate.py"
ARCHITECTURE_GATE = ROOT / "scripts" / "architecture-dependency-gate.py"
ANALYZER_CLEANLINESS_GATE = ROOT / "scripts" / "analyzer-cleanliness-gate.py"
ARCHITECTURE_POLICY = ROOT / "config" / "release" / "architecture-dependencies.json"
LOCAL_DB_PS = ROOT / "scripts" / "local-db.ps1"
LOCAL_CLEAN_PS = ROOT / "scripts" / "local-clean.ps1"
LOCAL_VALIDATE_RELEASE_PS = ROOT / "scripts" / "local-validate-release.ps1"
OPERATIONAL_SQL_ADAPTER = ROOT / "src" / "Jornada.Operational.Sql" / "OperationalSqlAdapter.cs"
OPERATIONAL_SQL_ADAPTER_CSPROJ = ROOT / "src" / "Jornada.Operational.Sql" / "Jornada.Operational.Sql.csproj"
OPERATIONAL_SQL_ADAPTER_TESTS = ROOT / "tests" / "Jornada.Tests" / "OperationalSqlAdapterTests.cs"
OPERATIONAL_SQL_ADAPTER_DOC = ROOT / "docs" / "Operational_SQL_Adapter.md"
FABRIC_SQL_COMPAT_DOC = ROOT / "docs" / "Fabric_SQL_Compatibility.md"
FABRIC_SQL_COMPAT_PS = ROOT / "scripts" / "fabric-sql-compatibility.ps1"
FABRIC_SQL_COMPAT_SH = ROOT / "scripts" / "fabric-sql-compatibility.sh"
SQL_INTEGRATION_ENV = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "Infrastructure" / "SqlIntegrationEnvironment.cs"
SQL_RUNTIME_INTEGRATION_TESTS = ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "SqlServerRuntimeIntegrationTests.cs"
API_SQL_SERVICES = ROOT / "src" / "Jornada.Api" / "SqlApiServices.cs"
PIPELINE_COORDINATOR = ROOT / "src" / "Jornada.Pipeline.Coordination" / "SqlPipelineCoordinator.cs"
BRONZE_MAINTENANCE = ROOT / "src" / "Jornada.Bronze.Maintenance.Worker" / "BronzeMaintenance.cs"
DELIVERY_BRONZE_RETENTION = ROOT / "src" / "Jornada.Operations.Maintenance.Worker" / "DeliveryBronzeRetentionWorker.cs"
FRIEND_ASSEMBLY_FILES = [
    ROOT / "src" / "Jornada.Api" / "Properties" / "AssemblyInfo.cs",
    ROOT / "src" / "Jornada.Processor.Worker" / "Properties" / "AssemblyInfo.cs",
    ROOT / "src" / "Jornada.Bronze.Maintenance.Worker" / "Properties" / "AssemblyInfo.cs",
]


def fail(message: str) -> None:
    raise SystemExit(f"TECHNICAL CLOSURE GATE: FAIL: {message}")


def last_proc(sql: str, name: str) -> str:
    marker = "CREATE OR ALTER PROCEDURE " + name
    start = sql.rfind(marker)
    if start < 0:
        fail(f"procedure ausente: {name}")
    end = sql.find("\nGO", start)
    if end < 0:
        fail(f"GO final ausente: {name}")
    return sql[start:end]


def method_block(source: str, marker: str, next_marker: str) -> str:
    start = source.find(marker)
    if start < 0:
        fail(f"método ausente: {marker}")
    end = source.find(next_marker, start)
    if end < 0:
        fail(f"limite do método ausente: {marker}")
    return source[start:end]


def require(text: str, snippets: list[str], context: str) -> None:
    for snippet in snippets:
        if snippet not in text:
            fail(f"{context}: trecho obrigatório ausente: {snippet}")


def main() -> None:
    sql = DDL.read_text(encoding="utf-8")

    # Regressão v3.93: a reaplicação do bloco de compatibilidade v3.42 não pode
    # rebaixar temporariamente o domínio de vinculo_fonte antes do bloco v3.44.
    # Existem duas definições pós-criação que precisam aceitar CONFLITO_GOVERNADO:
    # o bloco de compatibilidade/reentrada e o bloco evolutivo v3.44.
    metodo_final = "CHECK(metodo_resolucao IN('CPF_DETERMINISTICO','PENDENTE_PROBABILISTICO','LINKAGE_PROBABILISTICO','CORRECAO_GOVERNADA','CONFLITO_GOVERNADO'))"
    modelo_conflict = "(metodo_resolucao='CONFLITO_GOVERNADO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL)"
    if sql.count(metodo_final) < 2:
        fail("ck_vinculo_metodo: bloco de reentrada e bloco v3.44 devem aceitar CONFLITO_GOVERNADO")
    if sql.count(modelo_conflict) < 2:
        fail("ck_vinculo_modelo: bloco de reentrada e bloco v3.44 devem aceitar CONFLITO_GOVERNADO")
    tx_snippets = [
        "SET XACT_ABORT ON",
        "DECLARE @jornada_own_tran BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END",
        "IF @jornada_own_tran=1 BEGIN TRANSACTION",
        "BEGIN TRY",
        "IF @jornada_own_tran=1 COMMIT TRANSACTION",
        "BEGIN CATCH",
        "IF @jornada_own_tran=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION",
        "THROW;",
    ]
    procedures = [
        "identidade.sp_abrir_caso_conflito_identidade",
        "identidade.sp_aplicar_caso_conflito_identidade",
        "identidade.sp_aplicar_correcao_identidade",
        "identidade.sp_recompor_gold_pessoa",
        "identidade.sp_sincronizar_atribuicao_fatos",
        "ingestao.sp_recalcular_entrega",
    ]
    for proc in procedures:
        require(last_proc(sql, proc), tx_snippets, proc)

    # Regressão v3.66/v3.67: a recomposição materializa a fonte antes do MERGE.
    # CTEs de T-SQL só existem para a instrução imediatamente seguinte; por isso
    # nenhuma decisão pós-MERGE pode referenciar obs/stats diretamente.
    recompor = last_proc(sql, "identidade.sp_recompor_gold_pessoa")
    require(recompor, [
        "DECLARE @src TABLE",
        "INSERT @src",
        "MERGE gold.pessoa WITH (HOLDLOCK)",
        "IF NOT EXISTS(SELECT 1 FROM @src)",
    ], "sp_recompor_gold_pessoa materialização")
    if "SELECT 1 FROM obs" in recompor:
        fail("sp_recompor_gold_pessoa voltou a referenciar CTE obs fora da instrução consumidora")

    # Auditoria defensiva: nenhuma procedure com duas ou mais mutações pode depender
    # implicitamente de autocommit. A v3.67 remove a antiga exceção da recomposição.
    proc_matches = list(re.finditer(r"CREATE\s+OR\s+ALTER\s+PROCEDURE\s+([A-Za-z0-9_]+\.[A-Za-z0-9_]+)", sql, re.I))
    latest: dict[str, str] = {}
    for match in proc_matches:
        end = sql.find("\nGO", match.start())
        if end < 0:
            fail(f"GO final ausente na auditoria multi-write: {match.group(1)}")
        latest[match.group(1).lower()] = sql[match.start():end]
    audited_multiwrite = 0
    for name, block in latest.items():
        writes = len(re.findall(r"(?im)^\s*(?:INSERT|UPDATE|DELETE|MERGE)\b", block))
        if writes < 2:
            continue
        audited_multiwrite += 1
        owns = "@jornada_own_tran" in block
        explicit = re.search(r"\bBEGIN\s+TRAN(?:SACTION)?\b", block, re.I) is not None
        if not owns and not explicit:
            fail(f"procedure multi-write sem transação autocontida/explícita: {name}")

    reservation = RESERVATION.read_text(encoding="utf-8")
    retry = method_block(reservation, "public async Task<ProcessingFailureOutcome> ScheduleRetryOrPoisonAsync", "private async Task MarkFailedAsync")
    failed = method_block(reservation, "private async Task MarkFailedAsync", "private static async Task SetProcessingAsync")
    cs_tx = [
        "BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)",
        "command.Transaction = tx",
        "await tx.CommitAsync(ct)",
        "await tx.RollbackAsync(CancellationToken.None)",
    ]
    require(retry, cs_tx, "ScheduleRetryOrPoisonAsync")
    require(failed, cs_tx, "MarkFailedAsync")

    phone_cs = PHONE_CS.read_text(encoding="utf-8")
    require(phone_cs, [
        "PhoneEnvelopeTrimChars = [' ', '\\t', '\\r', '\\n', '\\u00A0']",
        "value.Trim(PhoneEnvelopeTrimChars)",
    ], "TELEFONE_BR_CANONICO_V2 C#")
    require(sql, [
        "UNICODE(LEFT(@v,1)) IN(9,10,13,32,160)",
        "UNICODE(SUBSTRING(@v,@n,1)) IN(9,10,13,32,160)",
    ], "TELEFONE_BR_CANONICO_V2 SQL")

    # EMAIL_CANONICO_V2: regra corrente não depende de NFC nem de collation.
    require(phone_cs, [
        "EmailEnvelopeTrimChars = [' ', '\\t', '\\r', '\\n', '\\u00A0']",
        '"EMAIL_CANONICO_V2" => NormalizeEmailV2(value)',
        "normalized.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c)",
    ], "EMAIL_CANONICO_V2 C#")
    require(sql, [
        "CREATE OR ALTER FUNCTION ref.fn_email_casefold_v1",
        "CREATE OR ALTER FUNCTION ref.fn_email_canonico_v2",
        "Upgrade de EMAIL_CONTATO encontrou valor legado incompatível com EMAIL_CANONICO_V2",
        "SET atributo_instancia_chave=ref.fn_email_canonico_v2(valor)",
        "chave_instancia_codigo='EMAIL_CANONICO_V2'",
        "Jornada.BaseNormativa",
        "@value=N'3.62'",
        "Jornada.SolutionSchema",
        "@value=N'3.69'",
    ], "EMAIL_CANONICO_V2 / schema marker SQL")
    email_v2_block = method_block(phone_cs, "private static string NormalizeEmailV2", "\n    }\n}")
    if ".Normalize(" in email_v2_block or "ToLowerInvariant" in email_v2_block:
        fail("EMAIL_CANONICO_V2 voltou a depender de normalização/lowercase Unicode do .NET")

    # 51114 deve anteceder o INSERT @o; do contrário PK(obs) mascara o contrato.
    apply_case = last_proc(sql, "identidade.sp_aplicar_caso_conflito_identidade")
    pos_51114 = apply_case.find("THROW 51114")
    pos_insert_o = apply_case.find("INSERT @o")
    if pos_51114 < 0 or pos_insert_o < 0 or pos_51114 > pos_insert_o:
        fail("51114 deve ser validado antes de INSERT @o")
    for code in range(51110, 51120):
        if f"THROW {code}" not in apply_case:
            fail(f"contrato governado ausente: THROW {code}")

    materialization = MATERIALIZATION_CS.read_text(encoding="utf-8")
    require(materialization, [
        'string.Equals(persisted.Cardinality, "SINGLE", StringComparison.OrdinalIgnoreCase)',
        "string Cardinality",
    ], "supressão de falso CONFLITO_EVIDENCIA em MULTI")

    readiness = READINESS_CS.read_text(encoding="utf-8")
    require(readiness, [
        "Jornada.BaseNormativa",
        "@base=N'3.62'",
        "Jornada.SolutionSchema",
        "@solution=N'3.69'",
        "SQL_SCHEMA_INCOMPATIVEL",
        "ref.fn_email_canonico_v2",
        "identidade.sp_recompor_gold_pessoa",
    ], "readiness de schema exato")
    data = json.loads(VECTORS.read_text(encoding="utf-8"))
    if data.get("rule") != "TELEFONE_BR_CANONICO_V2":
        fail("arquivo de vetores usa regra inesperada")
    if data.get("envelopeTrimUnicodeCodePoints") != [9, 10, 13, 32, 160]:
        fail("conjunto de trim dos vetores diverge do contrato")
    cases = data.get("cases")
    if not isinstance(cases, list) or len(cases) < 10:
        fail("vetores de telefone insuficientes")
    ids = [case.get("id") for case in cases]
    if len(ids) != len(set(ids)) or any(not item for item in ids):
        fail("IDs de vetores ausentes/duplicados")
    for case in cases:
        if not isinstance(case.get("input"), str) or not isinstance(case.get("valid"), bool):
            fail(f"vetor inválido: {case}")
        if case["valid"] and not case.get("expected"):
            fail(f"vetor válido sem expected: {case['id']}")
        if not case["valid"] and case.get("expected") is not None:
            fail(f"vetor inválido deve ter expected null: {case['id']}")

    email_data = json.loads(EMAIL_VECTORS.read_text(encoding="utf-8"))
    if email_data.get("rule") != "EMAIL_CANONICO_V2":
        fail("arquivo de vetores de e-mail usa regra inesperada")
    if email_data.get("envelopeTrimUnicodeCodePoints") != [9, 10, 13, 32, 160]:
        fail("trim dos vetores de e-mail diverge do contrato")
    if email_data.get("lowercase") != "ASCII_A_Z_ONLY" or email_data.get("unicodeNormalization") != "NONE":
        fail("vetores de e-mail não explicitam lowercase ASCII / ausência de normalização")
    email_cases = email_data.get("cases")
    if not isinstance(email_cases, list) or len(email_cases) < 12:
        fail("vetores de e-mail insuficientes")
    if not any("\\u0301" in json.dumps(c, ensure_ascii=True) for c in email_cases):
        fail("vetores de e-mail não cobrem representação Unicode decomposta")
    if not any("É" in (c.get("input") or "") for c in email_cases):
        fail("vetores de e-mail não cobrem não-ASCII")

    # Os invariantes possuem regressões de execução preparadas para o job integration-sql.
    identity_tests = IDENTITY_TESTS.read_text(encoding="utf-8")
    operational_tests = OPERATIONAL_TESTS.read_text(encoding="utf-8")
    processor_tests = PROCESSOR_TESTS.read_text(encoding="utf-8")
    phone_integration = PHONE_INTEGRATION.read_text(encoding="utf-8")
    email_integration = EMAIL_INTEGRATION.read_text(encoding="utf-8")
    identity_error_tests = IDENTITY_ERROR_TESTS.read_text(encoding="utf-8")
    readiness_tests = API_READINESS_TESTS.read_text(encoding="utf-8")
    require(identity_tests, [
        "Open_governed_case_is_atomic_when_called_directly_without_outer_transaction",
        "Apply_governed_case_is_atomic_when_called_directly_without_outer_transaction",
        "Cpf_identity_correction_is_atomic_when_called_directly_without_outer_transaction",
        "Recompose_gold_person_executes_source_and_no_source_paths",
    ], "fault injection governança")
    require(operational_tests, [
        "Recalculate_delivery_is_atomic_when_called_directly_without_outer_transaction",
        "Synchronize_fact_assignment_is_atomic_when_called_directly_without_outer_transaction",
    ], "fault injection operacional")
    require(processor_tests, [
        "Rejected_batch_rolls_back_lote_when_delivery_recalculation_fails",
    ], "fault injection Processor")
    require(phone_integration, [
        "Sql_and_processor_phone_v2_match_the_same_conformance_vectors",
        "ref.fn_telefone_br_canonico_v2",
    ], "conformidade SQL/C#")
    require(email_integration, [
        "Sql_and_processor_email_v2_match_the_same_conformance_vectors",
        "ref.fn_email_canonico_v2",
    ], "conformidade e-mail SQL/C#")
    for expected_name in [
        "Invalid_groups_json_returns_51110",
        "Inactive_or_unknown_manager_returns_51111",
        "Missing_or_closed_case_returns_51112",
        "Empty_group_set_returns_51113",
        "Observation_in_more_than_one_group_returns_51114_not_pk_error",
        "Not_all_case_observations_classified_returns_51115",
        "Group_without_observations_returns_51116",
        "Historical_merge_rejects_invalid_or_cyclic_destination_chain_with_51117",
        "Historical_merge_rejects_divergent_active_cpfs_with_51118",
    ]:
        if expected_name not in identity_error_tests:
            fail(f"teste direto de contrato governado ausente: {expected_name}")
    if 'Assert.Ignore("Seed' in identity_error_tests or 'Assert.Ignore("Seed' in identity_tests:
        fail("fixture crítica de identidade não pode ser mascarada por Assert.Ignore")

    # Release CI deve aceitar Ignore somente para indisponibilidade ambiental explícita.
    # Fixtures determinísticas ou contratos ausentes precisam falhar, não desaparecer do TRX.
    all_test_sources = "\n".join(
        path.read_text(encoding="utf-8")
        for test_root in (ROOT / "tests" / "Jornada.Tests", ROOT / "tests" / "Jornada.Integration.Tests")
        for path in sorted(test_root.rglob("*.cs"))
    )
    ignore_messages = re.findall(r'Assert\.Ignore\("([^"]+)"', all_test_sources)
    allowed_ignore_prefixes = (
        "Defina JORNADA_TEST_SQL_CONNECTION",
        "A credencial de integração não possui permissão para KILL",
    )
    unexpected_ignores = [m for m in ignore_messages if not m.startswith(allowed_ignore_prefixes)]
    if unexpected_ignores:
        fail("Assert.Ignore determinístico/não permitido: " + " | ".join(sorted(set(unexpected_ignores))))
    require(readiness_tests, [
        "Readiness_accepts_only_current_schema_marker_and_essential_objects",
    ], "readiness integration")

    sbom = SBOM_GENERATOR.read_text(encoding="utf-8")
    require(sbom, [
        "read_release_info",
        "base_normativa",
        "solution_engenharia",
        "RELEASE_INFO.txt",
    ], "SBOM derivado de RELEASE_INFO")
    if "'3.55'" in sbom or "default='3.60'" in sbom:
        fail("SBOM voltou a conter versão normativa/solution hardcoded antiga")
    release_info = RELEASE_INFO.read_text(encoding="utf-8")
    require(release_info, [
        "base_normativa=v3.64",
        "solution_engenharia=v4.05",
        "schema_base_normativa=v3.62",
        "schema_solution=v3.69",
        "origem_engenharia_anterior_1_materializada=true",
        "origem_engenharia_anterior_1_sha256=e18beb63aad7683ac749e2e5f2b600540c33fd7f92513ca2223daa684a3f6e19",
        "source_git_tag=jornada-solution-v4.05",
        "source_git_predecessor_tag=jornada-solution-v4.04",
        "source_git_bundle=Solution/supply-chain/source/Jornada_Source_v4.04_v4.05.bundle",
        "source_git_provenance=SOURCE_PROVENANCE.json",
    ], "RELEASE_INFO corrente")

    # v3.99: fronteira fina de acesso operacional Microsoft SQL. A regra estrutural é
    # centralizar a criação/abertura de SqlConnection sem esconder T-SQL ou criar um
    # caminho Fabric específico antes de incompatibilidade comprovada.
    for artifact in (OPERATIONAL_SQL_ADAPTER, OPERATIONAL_SQL_ADAPTER_CSPROJ, OPERATIONAL_SQL_ADAPTER_TESTS, OPERATIONAL_SQL_ADAPTER_DOC):
        if not artifact.is_file() or artifact.stat().st_size <= 0:
            fail(f"artefato Operational SQL Adapter ausente/vazio: {artifact.relative_to(ROOT)}")
    adapter_source = OPERATIONAL_SQL_ADAPTER.read_text(encoding="utf-8")
    require(adapter_source, [
        "public interface IOperationalSqlAdapter",
        "SqlConnection CreateConnection()",
        "SqlConnection CreateDedicatedSessionConnection()",
        "Task<SqlConnection> OpenAsync",
        "Task<SqlConnection> OpenDedicatedSessionAsync",
        "Pooling = false",
        "Enlist = false",
        "Não existe comportamento específico de Fabric",
    ], "Operational SQL Adapter v3.99")
    require(OPERATIONAL_SQL_ADAPTER_CSPROJ.read_text(encoding="utf-8"), [
        'PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2"',
    ], "csproj Operational SQL Adapter v3.99")
    adapter_tests = OPERATIONAL_SQL_ADAPTER_TESTS.read_text(encoding="utf-8")
    require(adapter_tests, [
        "Rejects_empty_connection_string",
        "Normal_connection_preserves_configured_sql_properties",
        "Dedicated_session_disables_pooling_and_automatic_enlistment",
        "Assert.That(builder.Pooling, Is.False)",
        "Assert.That(builder.Enlist, Is.False)",
    ], "testes unitários do Operational SQL Adapter v3.99")
    require(OPERATIONAL_SQL_ADAPTER_DOC.read_text(encoding="utf-8"), [
        "IOperationalSqlAdapter", "SQL Database in Microsoft Fabric", "`if (fabric)`",
        "`FabricSqlAdapter`",
    ], "documentação Operational SQL Adapter v3.99")

    direct_sql_connection = re.compile(r"\bnew\s+SqlConnection\s*\(")
    direct_offenders = []
    for source in sorted((ROOT / "src").rglob("*.cs")):
        if source.resolve() == OPERATIONAL_SQL_ADAPTER.resolve():
            continue
        if direct_sql_connection.search(source.read_text(encoding="utf-8")):
            direct_offenders.append(source.relative_to(ROOT).as_posix())
    if direct_offenders:
        fail("new SqlConnection fora do Operational SQL Adapter: " + ", ".join(direct_offenders))

    require(API_SQL_SERVICES.read_text(encoding="utf-8"), ["OpenDedicatedSessionAsync(ct)", "LockOwner='Session'"], "sessão dedicada da ingestão/API v3.99")
    require(PIPELINE_COORDINATOR.read_text(encoding="utf-8"), ["CreateDedicatedSessionConnection()", "IOperationalSqlAdapter"], "sessões dedicadas do pipeline v3.99")
    require(BRONZE_MAINTENANCE.read_text(encoding="utf-8"), ["OpenDedicatedSessionAsync(ct)", "IOperationalSqlAdapter"], "sessão dedicada Bronze Maintenance v3.99")
    require(DELIVERY_BRONZE_RETENTION.read_text(encoding="utf-8"), ["OpenDedicatedSessionAsync(ct)", "IOperationalSqlAdapter"], "sessão dedicada Delivery Bronze Retention v3.99")

    unit_csproj = TEST_CSPROJ.read_text(encoding="utf-8")
    integration_csproj = INTEGRATION_TEST_CSPROJ.read_text(encoding="utf-8")
    if "Testcontainers.MsSql" in unit_csproj:
        fail("Jornada.Tests não pode depender de Testcontainers após a separação v3.84")
    require(integration_csproj, ["Testcontainers.MsSql", "Jornada.Api", "Jornada.Processor.Worker"], "Integration dedicado")
    for forbidden_direct in ("Jornada.Ingestion\\Jornada.Ingestion.csproj", "Jornada.Linkage.Parameters.Worker", "Jornada.Linkage.Runner"):
        if forbidden_direct in integration_csproj:
            fail(f"Integration mantém ProjectReference direto desnecessário: {forbidden_direct}")
    # Regressão revelada pela compilação externa da v3.84: o novo assembly Integration
    # precisa ser friend assembly nos componentes cujos internals a suíte exerce.
    for assembly_info in FRIEND_ASSEMBLY_FILES:
        friend_text = assembly_info.read_text(encoding="utf-8")
        require(friend_text, [
            'InternalsVisibleTo("Jornada.Tests")',
            'InternalsVisibleTo("Jornada.Integration.Tests")',
        ], f"friend assemblies em {assembly_info.relative_to(ROOT)}")

    # Em PowerShell, $args é variável automática e nomes são case-insensitive.
    # Declarar [string[]]$Args como parâmetro de helper perdeu argumentos em execução real.
    reserved_args_pattern = re.compile(r"\[string\[\]\]\s*\$Args\b", re.I)
    offenders = []
    for script in sorted((ROOT / "scripts").glob("*.ps1")):
        if reserved_args_pattern.search(script.read_text(encoding="utf-8")):
            offenders.append(script.name)
    if offenders:
        fail("scripts PowerShell redeclaram a variável automática $args: " + ", ".join(offenders))
    local_db = LOCAL_DB_PS.read_text(encoding="utf-8")
    require(local_db, [
        "ComposeArgs",
        "SqlCmdArgs",
        "Test-DockerEngine",
        "docker info --format",
        "docker compose --env-file $EnvFile ps --format json sqlserver",
        "ConvertFrom-Json",
        "Container SQL Server não foi criado",
        "SQL Server ficou unhealthy",
        "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I @SqlCmdArgs",
    ], "hardening local-db v3.88")
    if "{{.Name}}|{{.State}}|{{.Health}}" in local_db:
        fail("local-db voltou ao template Go customizado incompatível de docker compose ps")

    integration_setup = (ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "Infrastructure" / "SqlServerIntegrationSetUp.cs").read_text(encoding="utf-8")
    require(integration_setup, [
        'Environment.GetEnvironmentVariable("DOCKER_API_VERSION")',
        'docker',
        '{{.Server.APIVersion}}',
        'serverApi < new Version(1, 44)',
        'Environment.SetEnvironmentVariable("DOCKER_API_VERSION", output)',
    ], "compatibilidade Docker API/Testcontainers v3.88")

    require(integration_setup, [
        'JornadaIntegration_Test_{Guid.NewGuid():N}',
    ], "isolamento Integration v3.91")
    if '_isolatedDatabaseName = $"JornadaIntegration_{Guid.NewGuid():N}"' in integration_setup:
        fail("fixture Integration voltou a gerar banco sem token Test/Dev/Local")

    # v4.03: o mesmo assembly Integration pode apontar para SQL Server 2022 ou para
    # SQL Database in Microsoft Fabric, sem alterar DDL/seed e sem criar Adapter específico.
    # SQL Server permanece baseline local/CI; Fabric é homologação adicional e condicional.
    sql_env = SQL_INTEGRATION_ENV.read_text(encoding="utf-8")
    require(sql_env, [
        'TargetVariable = "JORNADA_TEST_SQL_TARGET"',
        'SqlServer2022Target = "SQL_SERVER_2022"',
        'FabricSqlDatabaseTarget = "FABRIC_SQL_DATABASE"',
        'IsFabricSqlDatabase',
    ], "seleção explícita do alvo SQL v4.03")
    require(integration_setup, [
        "JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE exige",
        "JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true",
        "EnsureIntegrationDatabaseNameIsSafe",
        "Pooling = false",
        "SELECT DB_NAME();",
        "Integration SQL target:",
    ], "fixture externa/Fabric v4.03")
    runtime_target_tests = SQL_RUNTIME_INTEGRATION_TESTS.read_text(encoding="utf-8")
    require(runtime_target_tests, [
        "ConfiguredSqlTargetIsReachableAndEngineContractIsExplicitAsync",
        "SqlServer2022Target",
        "Is.EqualTo(16)",
        "UseExistingExternalDatabase",
        "builder.Pooling, Is.False",
    ], "prova de alvo SQL v4.03")
    for artifact in (FABRIC_SQL_COMPAT_DOC, FABRIC_SQL_COMPAT_PS, FABRIC_SQL_COMPAT_SH):
        if not artifact.is_file() or artifact.stat().st_size <= 0:
            fail(f"harness Fabric ausente/vazio: {artifact.relative_to(ROOT)}")
    require(FABRIC_SQL_COMPAT_DOC.read_text(encoding="utf-8"), [
        "FABRIC_SQL_DATABASE", "JORNADA_FABRIC_SQL_CONNECTION", "Microsoft Entra",
        "58/58", "FabricSqlAdapter", "baseline obrigatória", "ensaios não funcionais",
        "nenhuma connection string Fabric é distribuída",
    ], "documentação Fabric v4.03")
    require(FABRIC_SQL_COMPAT_PS.read_text(encoding="utf-8-sig"), [
        "JORNADA_FABRIC_SQL_CONNECTION", "$env:JORNADA_TEST_SQL_TARGET = 'FABRIC_SQL_DATABASE'",
        "$env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = 'true'",
        "$env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE = 'true'",
        "Active Directory Device Code Flow", "'restore', 'Jornada.sln', '--locked-mode'",
        "'restore', $Project, '--locked-mode'", "Write-TrxSummary",
        "FABRIC SQL COMPATIBILITY: OK", "Python: NÃO utilizado",
    ], "harness Fabric PowerShell v4.03")
    require(FABRIC_SQL_COMPAT_SH.read_text(encoding="utf-8"), [
        "JORNADA_FABRIC_SQL_CONNECTION", "JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE",
        "JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true",
        "JORNADA_TEST_SQL_RESET_EXISTING_DATABASE=true", "dotnet restore Jornada.sln --locked-mode",
        "not_executed", "FABRIC SQL COMPATIBILITY: OK", "Python: NÃO utilizado",
    ], "harness Fabric shell v4.03")

    local_clean = LOCAL_CLEAN_PS.read_text(encoding="utf-8-sig")
    require(local_clean, [
        "$ScriptVersion = '2026.09.04-v4.05'",
        "docker compose",
        "'down','-v','--remove-orphans'",
        "Where-Object { $_.Name -in @('bin','obj') }",
        "Remove-DirectoryBestEffort",
        "Cache .vs do Visual Studio",
        "Esse cache não é necessário para restore/build/test. A limpeza continuará.",
        "local-validate-release-r4.ps1",
        "Imagem Docker do SQL Server preservada; local-validate-release.ps1 validará digest e engine.",
    ], "local-clean canônico v4.05")

    local_validate = LOCAL_VALIDATE_RELEASE_PS.read_text(encoding="utf-8-sig")
    require(local_validate, [
        "$ScriptVersion = '2026.09.04-v4.05'",
        "@('restore', $Solution, '--locked-mode')",
        "@('restore', $UnitProject, '--locked-mode')",
        "@('restore', $IntegrationProject, '--locked-mode')",
        "'8/9 Build explícito Jornada.Integration.Tests'",
        "Assert-FileExists -Path $IntegrationDll",
        "Remove-Item Env:JORNADA_TEST_SQL_CONNECTION",
        "Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE",
        "$env:JORNADA_TEST_SQL_TARGET = 'SQL_SERVER_2022'",
        "JornadaIntegration_Test_<guid>",
        "Get-CanonicalSqlServerImage",
        "@sha256:[0-9a-fA-F]{64}$",
        "docker image inspect",
        "DBCC CHECKDB (N'master') WITH NO_INFOMSGS, ALL_ERRORMSGS",
        "Recuperação da imagem SQL: OK",
        "@('image','rm',$imageId)",
        "@('pull',$image)",
        "$env:JORNADA_TEST_SQL_IMAGE = $canonicalSqlImage",
        "'9/9 Testes Integration'",
        "$CleanScript = Join-Path $PSScriptRoot 'local-clean.ps1'",
        "& $CleanScript",
        "$TestResultsDir = Join-Path $Root 'TestResults\\Release'",
        "Jornada.Unit.Release.trx",
        "Jornada.Integration.Release.trx",
        '--logger "trx;LogFileName=$trxName"',
        "--results-directory $TestResultsDir",
    ], "local-validate-release canônico v4.05")
    if local_validate.find("& $CleanScript") > local_validate.find("@('restore', $Solution, '--locked-mode')"):
        fail("local-validate-release deve executar local-clean.ps1 antes do primeiro restore")
    if "@('image','rm','-f'" in local_validate:
        fail("recuperação SQL não pode force-remover imagem potencialmente compartilhada")
    if (ROOT / "scripts" / "local-validate-release-r4.ps1").exists():
        fail("variante temporária local-validate-release-r4.ps1 não pode integrar a release")

    # Pessoa v2: codigoPessoaOrigem pode ser derivado do CPF somente quando o código não veio preenchido.
    processor_models = (ROOT / "src" / "Jornada.Processor.Worker" / "ProcessorModels.cs").read_text(encoding="utf-8")
    require(processor_models, [
        'var sourceCode = OptionalString(json, "codigoPessoaOrigem")',
        'var cpf = OptionalString(json, "cpf")',
        'codigoPessoaOrigem ausente exige CPF preenchido para derivação do código de origem.',
    ], "fallback CPF -> codigoPessoaOrigem")
    for gestor in ("SEHAB", "SMADS", "SMDET", "SMS"):
        schema_v2 = ROOT / "config" / "contracts" / "gestores" / gestor / "pessoa" / "v2" / "pessoa.schema.json"
        if not schema_v2.is_file():
            fail(f"contrato Pessoa v2 ausente para {gestor}")
        data = json.loads(schema_v2.read_text(encoding="utf-8"))
        if "codigoPessoaOrigem" in data.get("required", []):
            fail(f"Pessoa v2 de {gestor} voltou a exigir codigoPessoaOrigem incondicionalmente")
        if "cpf" not in data.get("required", []):
            fail(f"Pessoa v2 de {gestor} deve manter cpf explicitamente presente")

    # Regressões reveladas pelo bootstrap real da v3.86: índices filtrados dependiam
    # implicitamente de SETs da sessão e EXEC(...QUOTENAME(...)) não é sintaxe válida.
    required_sql_sets = [
        "SET QUOTED_IDENTIFIER ON;",
        "SET ANSI_NULLS ON;",
        "SET ANSI_PADDING ON;",
        "SET ANSI_WARNINGS ON;",
        "SET ARITHABORT ON;",
        "SET CONCAT_NULL_YIELDS_NULL ON;",
        "SET NUMERIC_ROUNDABORT OFF;",
    ]
    ddl_candidates = [DDL] + sorted((ROOT / "database" / "baselines").glob("Jornada_Fase1_v*.sql"))
    invalid_exec_quotename = re.compile(r"EXEC\s*\([^;\n]*QUOTENAME\s*\(", re.I)
    for ddl_path in ddl_candidates:
        ddl_text = ddl_path.read_text(encoding="utf-8-sig")
        require(ddl_text, required_sql_sets, f"SETs obrigatórios em {ddl_path.relative_to(ROOT)}")
        if invalid_exec_quotename.search(ddl_text):
            fail(f"SQL dinâmico inválido EXEC(...QUOTENAME(...)) em {ddl_path.relative_to(ROOT)}")
    require(DDL.read_text(encoding="utf-8-sig"), [
        "EXEC sys.sp_executesql @sql_drop_credencial_finalidades",
        "EXEC sys.sp_executesql @sql_drop_api_auditoria",
        "EXEC sys.sp_executesql @sql_drop_recebido_entrega",
        "EXEC sys.sp_executesql @sql_drop_recebido_bronze",
        "EXEC sys.sp_executesql @sql_add_identity_check",
    ], "SQL dinâmico DDL v3.88")

    current_ddl = DDL.read_text(encoding="utf-8-sig")
    if "''''BENEFICIO''''" in current_ddl or "''''ENCERRADA''''" in current_ddl:
        fail("migração de vigência voltou a sobre-escapar literais dentro de SQL dinâmico")
    require(current_ddl, [
        "WHERE natureza=''BENEFICIO''",
        "NOT IN(''VIGENTE'',''SUSPENSA'',''ENCERRADA'',''ENCERRADO'')",
        "THEN ''ENCERRADA''",
        "THEN ''TERMINO_REGULAR''",
    ], "SQL dinâmico de migração de vigência v4.05")
    require(current_ddl, [
        "IX_bronze_entrega_arquivo_payload_sha256",
        "CREATE INDEX IX_bronze_entrega_arquivo_payload_sha256 ON bronze.entrega_arquivo(payload_sha256)",
        "DROP INDEX IX_bronze_entrega_arquivo_objeto_chave ON bronze.entrega_arquivo",
    ], "índice Bronze seguro v3.88")
    if "CREATE INDEX IX_bronze_entrega_arquivo_objeto_chave ON bronze.entrega_arquivo(objeto_chave)" in current_ddl:
        fail("DDL corrente voltou a indexar NVARCHAR(1024) objeto_chave como chave física (>1700 bytes)")

    # Regressões reveladas pela execução real da v3.90.
    require(current_ddl, [
        "UPDATE im SET estado='EM_CONFLITO'",
        "FROM identidade.identity_map im",
        "WHERE im.vigencia_fim IS NULL AND im.estado='ATIVO'",
    ], "UPDATE identity_map governado v3.91")

    release_info_text = RELEASE_INFO.read_text(encoding="utf-8")
    require(release_info_text, [
        "V399_LOCKED_RESTORE_SOLUTION_PASS",
        "V399_LOCKED_RESTORE_UNIT_PASS",
        "V399_LOCKED_RESTORE_INTEGRATION_PASS",
        "V399_BUILD_SOLUTION_PASS",
        "V399_UNIT_TESTS_PASS",
        "V399_SQL_IMAGE_DIGEST_PASS",
        "V399_SQL_ENGINE_CHECKDB_PASS",
        "V399_INTEGRATION_TESTS_PASS",
        "V399_LOCAL_RELEASE_VALIDATION_PASS",
    ], "evidência runtime externa v3.99 preservada na v4.03")


    # Baseline documental introduzido na v3.98 e preservado na v3.99: RN/RF/RNF/RT + matriz única de rastreabilidade.
    requirement_artifacts = (
        MASTER_REQ_MD, MASTER_REQ_DOCX, MASTER_REQ_PDF,
        BUSINESS_REQ_MD, BUSINESS_REQ_DOCX, BUSINESS_REQ_PDF,
        FUNCTIONAL_REQ_MD, FUNCTIONAL_REQ_DOCX, FUNCTIONAL_REQ_PDF,
        NONFUNCTIONAL_REQ_MD, NONFUNCTIONAL_REQ_DOCX, NONFUNCTIONAL_REQ_PDF,
        TECHNICAL_REQ_MD, TECHNICAL_REQ_DOCX, TECHNICAL_REQ_PDF,
        TRACEABILITY_REQ_MD, TRACEABILITY_REQ_DOCX, TRACEABILITY_REQ_PDF,
        REQUIREMENTS_MAP, RUNTIME_EVIDENCE_V395, RUNTIME_EVIDENCE_V399,
    )
    for artifact in requirement_artifacts:
        if not artifact.is_file() or artifact.stat().st_size <= 0:
            fail(f"artefato documental baseline v3.98 ausente/vazio: {artifact.relative_to(ROOT.parent)}")
    if (ROOT.parent / "Documentos" / "Requisitos_de_Negocio_Jornada_v1.0.md").exists() or (ROOT.parent / "Documentos" / "Requisitos_Tecnicos_Jornada_v1.0.md").exists():
        fail("baselines RN/RT antigos não podem permanecer duplicados na raiz de Documentos")
    business_text = BUSINESS_REQ_MD.read_text(encoding="utf-8")
    functional_text = FUNCTIONAL_REQ_MD.read_text(encoding="utf-8")
    nonfunctional_text = NONFUNCTIONAL_REQ_MD.read_text(encoding="utf-8")
    technical_text = TECHNICAL_REQ_MD.read_text(encoding="utf-8")
    trace_text = TRACEABILITY_REQ_MD.read_text(encoding="utf-8")
    master_text = MASTER_REQ_MD.read_text(encoding="utf-8")
    if len(re.findall(r"(?m)^### RN-\d{3} - ", business_text)) != 36:
        fail("Requisitos de Negócio v1.1 deve conter exatamente 36 RN")
    if len(re.findall(r"(?m)^#### RF-\d{3} - ", functional_text)) != 50:
        fail("Requisitos Funcionais v1.0 deve conter exatamente 50 RF")
    if len(re.findall(r"(?m)^### RNF\d{2} - ", nonfunctional_text)) != 33:
        fail("Requisitos Não Funcionais v1.0 deve conter exatamente 33 RNF")
    if len(re.findall(r"(?m)^#### RT-\d{3} - ", technical_text)) != 65:
        fail("Requisitos Técnicos v1.1 deve conter exatamente 65 RT")
    require(master_text, ["RN - Requisitos de Negócio", "RF - Requisitos Funcionais", "RNF - Requisitos Não Funcionais", "RT - Requisitos Técnicos", "Matriz de Rastreabilidade"], "Índice Mestre de Requisitos v1.0")
    require(business_text, ["Versão do documento:** 1.1", "RN-004 - Constituir a Gold de Pessoas de forma evolutiva", "RN-036 - Manter biometria como evolução e não como núcleo cadastral", "Matriz_Rastreabilidade_Requisitos_Jornada_v1.0"], "Requisitos de Negócio v1.1")
    require(functional_text, ["Versão do documento:** 1.0", "RF-001", "RF-050", "Rastreabilidade de qualidade/técnica"], "Requisitos Funcionais v1.0")
    require(nonfunctional_text, ["Versão do documento:** 1.0", "RNF01", "RNF33", "Seção 17"], "Requisitos Não Funcionais v1.0")
    require(technical_text, ["Versão do documento:** 1.1", "RT-001", "RT-065", "camada de realização"], "Requisitos Técnicos v1.1")
    require(trace_text, ["RN -> RF -> RNF -> RT -> EVIDÊNCIA", "Cobertura por RF", "Cobertura por RNF", "Todos os 65 RT"], "Matriz de Rastreabilidade v1.0")
    req_map = json.loads(REQUIREMENTS_MAP.read_text(encoding="utf-8"))
    if req_map.get("counts") != {"RN": 36, "RF": 50, "RNF": 33, "RT": 65}:
        fail("requirements-map.json declara contagens inesperadas")
    expected_rn = {f"RN-{i:03d}" for i in range(1, 37)}
    expected_rf = {f"RF-{i:03d}" for i in range(1, 51)}
    expected_rnf = {f"RNF{i:02d}" for i in range(1, 34)}
    expected_rt = {f"RT-{i:03d}" for i in range(1, 66)}
    if set(req_map.get("rn_to_rf", {})) != expected_rn:
        fail("requirements-map.json não cobre os 36 RN")
    mapped_rf = {x for values in req_map.get("rn_to_rf", {}).values() for x in values}
    mapped_rnf = {x for values in req_map.get("rn_to_rnf", {}).values() for x in values}
    mapped_rt = {x for values in req_map.get("rn_to_rt", {}).values() for x in values}
    if mapped_rf != expected_rf or mapped_rnf != expected_rnf or mapped_rt != expected_rt:
        fail("requirements-map.json possui requisito órfão em RF/RNF/RT")
    require(RUNTIME_EVIDENCE_V395.read_text(encoding="utf-8"), [
        "V395_LOCKED_RESTORE_SOLUTION_PASS", "V395_BUILD_RELEASE_0_WARNINGS_0_ERRORS",
        "V395_UNIT_153_OF_153_PASS", "V395_DBCC_CHECKDB_MASTER_OK", "V395_INTEGRATION_58_OF_58_PASS",
    ], "evidência runtime v3.95")
    require(RUNTIME_EVIDENCE_V399.read_text(encoding="utf-8"), [
        "ScriptVersion:        2026.09.03-v3.99",
        "V399_LOCKED_RESTORE_SOLUTION_PASS",
        "V399_LOCKED_RESTORE_UNIT_PASS",
        "V399_LOCKED_RESTORE_INTEGRATION_PASS",
        "V399_BUILD_SOLUTION_PASS",
        "V399_UNIT_TESTS_PASS",
        "V399_SQL_ENGINE_CHECKDB_PASS",
        "V399_INTEGRATION_TESTS_PASS",
        "A síntese reportada não informa contagens individuais de testes",
    ], "evidência runtime v3.99")

    # O defeito v3.92 só é evitado se o bloco de compatibilidade anterior ao v3.44
    # já reconhecer o estado final. O count>=2 acima protege reentrada + bloco v3.44.
    if "v3.93: este bloco de compatibilidade pode ser reaplicado" not in current_ddl:
        fail("DDL corrente não documenta/protege a reentrada v3.93")

    reservation = (ROOT / "src" / "Jornada.Processor.Worker" / "SqlProcessorRepository.Reservation.cs").read_text(encoding="utf-8")
    if "ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct)" in reservation:
        fail("ReserveNext voltou a usar SequentialAccess com leitura fora de ordem")
    require(reservation, ["await commandLoad.ExecuteReaderAsync(ct)"], "reader de reserva v3.91")

    sql_batch_runner = (ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "SqlBatchRunner.cs").read_text(encoding="utf-8")
    require(sql_batch_runner, [
        "SET NOCOUNT OFF; SET XACT_ABORT OFF;",
    ], "reset de opções de sessão Integration v3.91")

    seed_dev = SEED.read_text(encoding="utf-8-sig")
    require(seed_dev, [
        "v3.91: a fixture DEV é convergente",
        "estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL",
        "WHERE lote_id=@lotPessoa",
        "WHERE lote_id=@lotBen",
        "WHERE lote_id=@lotS",
    ], "seed DEV convergente v3.91")

    seed_tests = (ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "SeedDatabaseTests.cs").read_text(encoding="utf-8")
    require(seed_tests, [
        "situacao_geografia,origem_geografia",
        "'NAO_RESOLVIDA_ORIGEM'",
    ], "fixture territorial coerente v3.91")

    # Regressões v3.94: quatro resíduos da execução externa v3.93.
    persistence = (ROOT / "src" / "Jornada.Processor.Worker" / "SqlProcessorRepository.Persistence.cs").read_text(encoding="utf-8")
    require(persistence, [
        'string.Equals(attribute.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)',
        '!attribute.VerificadoEm.HasValue',
        'throw new InvalidDataException($"Atributo COMPROVADO sem verificadoEm:',
    ], "validação COMPROVADO antes da constraint Silver v3.94")
    validation_pos = persistence.find('string.Equals(attribute.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)')
    insert_pos = persistence.find('INSERT silver.pessoa_atributo_observacao(', validation_pos)
    if validation_pos < 0 or insert_pos < 0 or validation_pos > insert_pos:
        fail("validação COMPROVADO deve ocorrer antes do INSERT Silver do atributo")

    require(processor_tests, [
        "codigo_pessoa_origem='PROC-V325'",
        "codigo_registro_origem='REG-PROC-V325'",
        "Worker que perdeu a primeira disputa deve progredir no poll seguinte.",
        "first = await firstRepository.ReserveNextAsync",
        "second = await secondRepository.ReserveNextAsync",
    ], "isolamento e progresso concorrente dos testes Processor v3.94")
    processor_worker = (ROOT / "src" / "Jornada.Processor.Worker" / "ProcessorWorker.cs").read_text(encoding="utf-8")
    require(processor_worker, [
        "var processed = await processor.ProcessNextAsync(stoppingToken);",
        "if (!processed)",
        "await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100, options.PollingMilliseconds)), stoppingToken);",
    ], "contrato de poll eventual do Processor v3.94")

    # Regressão v3.95: a prova de rollback não pode confundir linhas canônicas
    # preexistentes do lote de seed com persistência parcial do cenário.
    require(processor_tests, [
        "codigo_pessoa_origem IN('PROC-V325-ROLLBACK-A','PROC-V325-ROLLBACK-B')",
        "po.codigo_pessoa_origem IN('PROC-V325-ROLLBACK-A','PROC-V325-ROLLBACK-B')",
        "codigo_origem IN('PROC-V325-ROLLBACK-A','PROC-V325-ROLLBACK-B')",
        "identificador IN('31415926590','27182818205')",
        "cpf IN('31415926590','27182818205')",
        'Is.EqualTo("VALIDANDO")',
    ], "escopo da prova de rollback Processor v3.95")

    require(seed_dev, [
        "v3.94: avaliações de possibilidade",
        "WHERE pessoa_uuid=@u1 AND tipo_registro_versao_id=@arv",
        "WHERE pessoa_uuid=@u2 AND tipo_registro_versao_id=@potv",
        "WHERE pessoa_uuid=@u5 AND tipo_registro_versao_id=@arv",
        "WHERE pessoa_uuid=@u3 AND tipo_registro_versao_id=@crv",
    ], "seed DEV de possibilidades convergente v3.94")
    if "IF NOT EXISTS(SELECT 1 FROM qualidade.avaliacao_possibilidade)" in seed_dev:
        fail("seed DEV voltou à guarda global não convergente de avaliacao_possibilidade")
    require(seed_tests, [
        "registrosAtribuidos",
        "estado_atribuicao_identidade='ATRIBUIDA'",
        "registros, Is.EqualTo(registrosAtribuidos)",
    ], "projeção individual identity-aware v3.94")

    lock_provenance = json.loads(NUGET_LOCK_PROVENANCE.read_text(encoding="utf-8"))
    if lock_provenance.get("assurance") != "CI_REGENERATED_AND_REPRODUCIBLE_LOCK_GRAPH" or lock_provenance.get("release") != "v4.05":
        fail("proveniência NuGet deve declarar v4.05 / grafo regenerado e reproduzível")
    if lock_provenance.get("currentGraphVerification") != "SDK_8_0_424_FORCE_EVALUATED_NO_DIFF_THEN_LOCKED_MODE" or lock_provenance.get("pendingLockCount") != 0:
        fail("grafo NuGet v4.05 deve ser reproduzível pelo SDK fixado e não possuir lock pendente")
    generation = lock_provenance.get("lockGraphGeneration") or {}
    if generation.get("sdk") != "8.0.424" or generation.get("nugetLockGate") != "PASS" or generation.get("lockCount") != 18:
        fail("proveniência NuGet não registra a regeneração CI v4.05 completa")
    require(NUGET_LOCK_PROVENANCE_GATE.read_text(encoding="utf-8"), ["REGENERATED_OR_VERIFIED_V405_SDK_8_0_424", "CI_FORCE_EVALUATE_AND_LOCK_GATE_PASS_V405", "--force-evaluate", "8.0.424"], "gate de proveniência NuGet v4.05")

    lineage_gate = PREDECESSOR_GATE.read_text(encoding="utf-8")
    require(lineage_gate, [
        "--require-all-predecessors",
        "SHA-256 divergente",
        "promoção bloqueada",
    ], "gate de integridade de predecessor")

    source_gate = SOURCE_GATE.read_text(encoding="utf-8")
    require(source_gate, [
        "--bundle",
        "--repo",
        "source_git_predecessor_tag",
        "merge-base",
        "bundleSha256",
        "--compare-root",
        "HEAD",
    ], "gate de fonte Git")
    source_builder = SOURCE_BUNDLE_BUILDER.read_text(encoding="utf-8")
    require(source_builder, [
        "bundle create",
        "release-source-gate.py",
        "SOURCE_PROVENANCE.json",
    ], "builder de bundle Git")
    # SOURCE_PROVENANCE e o bundle são artefatos gerados após a tag. Em checkout Git
    # limpo eles podem não existir; nesse caso a promoção os cria e release-source-gate.py
    # os verifica. Se estiverem presentes no pacote distribuído, também os auditamos aqui.
    if SOURCE_PROVENANCE.is_file():
        provenance = json.loads(SOURCE_PROVENANCE.read_text(encoding="utf-8"))
        if provenance.get("solutionEngenharia") != "v4.05" or provenance.get("baseNormativa") != "v3.64":
            fail("SOURCE_PROVENANCE declara versões inesperadas")
        if provenance.get("current", {}).get("tag") != "jornada-solution-v4.05" or provenance.get("predecessor", {}).get("tag") != "jornada-solution-v4.04":
            fail("SOURCE_PROVENANCE declara cadeia Git inesperada")
        bundle = ROOT.parent / str(provenance.get("bundlePath", ""))
        if not bundle.is_file():
            fail(f"bundle Git declarado pela proveniência está ausente: {bundle}")
        bundle_sha = hashlib.sha256(bundle.read_bytes()).hexdigest()
        if bundle_sha != provenance.get("bundleSha256"):
            fail("SHA-256 do bundle Git diverge de SOURCE_PROVENANCE")

    root_gitignore = ROOT_GITIGNORE.read_text(encoding="utf-8")
    if re.search(r"(?m)^RELEASE_INFO\.txt\s*$", root_gitignore):
        fail("RELEASE_INFO.txt não pode continuar ignorado pelo Git")
    git_runbook = GIT_RELEASE_RUNBOOK.read_text(encoding="utf-8")
    require(git_runbook, [
        "RELEASE_INFO.txt`, ao contrário, precisa estar no commit aprovado **antes** da tag",
        "packages.lock.json` por restore confiável e **commitá-los**",
        "release-promotion",
        "--compare-root .",
    ], "runbook Git/release v3.69")
    require(RELEASE_INFO_SH.read_text(encoding="utf-8"), [
        "NÃO gera",
        "release-source-gate.py",
        "--require-clean",
    ], "wrapper RELEASE_INFO sh")
    require(RELEASE_INFO_PS.read_text(encoding="utf-8"), [
        "release-source-gate.py",
        "--require-clean",
    ], "wrapper RELEASE_INFO ps1")

    require(TEST_EVIDENCE_GATE.read_text(encoding="utf-8"), [
        "--forbid-skipped", "minimum-tests", "UnitTestResult", "TRX EVIDENCE GATE: OK"
    ], "gate TRX crítico")
    require(PERFORMANCE_EVIDENCE_GATE.read_text(encoding="utf-8"), [
        "CONCLUIDO_SEM_PUBLICACAO", "noCandidateInBirthDateBlock", "--baseline", "--require-baseline-approved",
        "Com baseline APROVADO, também aplica os limites versionados de HML"
    ], "gate de evidência de escala + baseline HML")
    require(HML_CONFIG_GATE.read_text(encoding="utf-8"), [
        "--require-approved", "approvedValue", "evidence.sha256", "matriz HML ainda não está integralmente APROVADA", "HML CONFIG GATE: OK"
    ], "contrato executável de parâmetros HML")
    require(LINKAGE_EVIDENCE_GATE.read_text(encoding="utf-8"), [
        "DEV_HML_ONLY_NO_PUBLICATION", "--require-policy-approved", "maximumCandidateExpansionRatio",
        "nomeTotalVariation", "SUBMETER_V2_PARA_REVISAO_NORMATIVA", "nunca promove V2"
    ], "gate de blocking/transportabilidade")
    for hml_gate, label in [(HML_READINESS_GATE, "readiness HML sh"), (HML_READINESS_GATE_PS, "readiness HML ps1")]:
        require(hml_gate.read_text(encoding="utf-8"), [
            "JORNADA_HML_PERFORMANCE_REPORT", "JORNADA_HML_LINKAGE_REPORT", "JORNADA_HML_SQL_PERFORMANCE_REPORT",
            "JORNADA_HML_API_PROJECTION_REPORT", "governance-readiness-gate.py", "scheduler-contract-gate.py",
            "environment-preflight-gate.py", "--require-approved", "--require-baseline-approved",
            "--require-policy-approved", "HML READINESS GATE: OK"
        ], label)
    for config_path, expected_status in [
        (HML_PARAMETERS, "PENDENTE"), (HML_PERFORMANCE_BASELINE, "PENDENTE"), (HML_LINKAGE_POLICY, "PENDENTE")
    ]:
        if not config_path.is_file():
            fail(f"contrato HML ausente: {config_path}")
        config_data = json.loads(config_path.read_text(encoding="utf-8"))
        if config_data.get("schemaVersion") != 1 or config_data.get("status") != expected_status:
            fail(f"contrato HML inicial deve ser schemaVersion=1/status=PENDENTE: {config_path.name}")
    for gate_path, tokens, label in [
        (GOVERNANCE_GATE, ["GOVERNANCE READINESS GATE: OK", "schemas ainda não estão integralmente APROVADOS", "autoMutate"], "governança executável"),
        (SCHEDULER_GATE, ["SCHEDULER CONTRACT GATE: OK", "scheduler corporativo ainda não está APROVADO/configurado", "CONTINUOUS_WORKER"], "contrato scheduler"),
        (ENVIRONMENT_GATE, ["ENVIRONMENT PREFLIGHT GATE", "exactVersion", "requiredEnvironment"], "preflight de ambiente"),
        (SQL_PERFORMANCE_GATE, ["SQL PERFORMANCE EVIDENCE GATE", "deltaLockWaitMilliseconds", "deltaDeadlocks", "Query Store"], "evidência SQL HML"),
        (API_PROJECTION_GATE, ["API PROJECTION EVIDENCE GATE", "1,10,100,1000", "p95Milliseconds"], "evidência API 1/10/100/1000"),
    ]:
        require(gate_path.read_text(encoding="utf-8"), tokens, label)

    for config_path in [SCHEMA_APPROVALS, RETENTION_DR_POLICY, IDENTITY_LIFECYCLE_POLICY, SCHEDULER_JOBS, ENVIRONMENT_REQUIREMENTS, SQL_PERFORMANCE_POLICY, API_PROJECTION_POLICY]:
        if not config_path.is_file():
            fail(f"contrato v3.76 ausente: {config_path}")
        data = json.loads(config_path.read_text(encoding="utf-8"))
        if data.get("schemaVersion") != 1:
            fail(f"schemaVersion inválido em {config_path.name}")

    schema_inventory = json.loads(SCHEMA_APPROVALS.read_text(encoding="utf-8"))
    actual_contracts = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "config" / "contracts").rglob("*.json"))
    listed_contracts = sorted(row.get("path") for row in schema_inventory.get("contracts", []))
    if actual_contracts != listed_contracts:
        fail("schema-approvals deve inventariar exatamente todos os config/contracts/*.json")
    if len(actual_contracts) < 1:
        fail("inventário de schemas vazio")

    scheduler = json.loads(SCHEDULER_JOBS.read_text(encoding="utf-8"))
    if len(scheduler.get("jobs", [])) != 10:
        fail("scheduler-jobs deve conter os 10 jobs/operacoes previstos")
    if scheduler.get("status") != "PENDENTE_HML":
        fail("scheduler-jobs distribuído deve permanecer PENDENTE_HML")

    require(SQL_PERFORMANCE_SQL.read_text(encoding="utf-8"), ["sys.database_query_store_options", "sys.dm_os_wait_stats", "Number of Deadlocks/sec", "FOR JSON PATH, WITHOUT_ARRAY_WRAPPER"], "coletor SQL HML sem PII")
    require(API_PROJECTION_HARNESS.read_text(encoding="utf-8"), ["SIZES=(1,10,100,1000)", "JORNADA_HML_ACCESS_KEY", "UUIDs, payloads e respostas não são persistidos"], "harness API de projeção")

    require(BRONZE_RESTORE_EVIDENCE_GATE.read_text(encoding="utf-8"), [
        "missingObjectDetected", "corruptObjectDetected", "finalVerifyPassed", "BRONZE RESTORE EVIDENCE GATE: OK"
    ], "gate de restore Bronze")
    require(POWERBI_STATIC_GATE.read_text(encoding="utf-8"), [
        "esperadas 22 páginas PBIR", "pages.json não referencia exatamente as 22 páginas", "POWER BI STATIC GATE: OK"
    ], "gate estático Power BI")
    for path, label in [(LOCAL_BACKUP_SH,"backup sh"),(LOCAL_BACKUP_PS,"backup ps1")]:
        text = path.read_text(encoding="utf-8")
        require(text, ["missingObjectDetected", "corruptObjectDetected", "bronze-restore-evidence-gate.py"], label)
    for path, label in [(LOCAL_FAULT_SH,"fault sh"),(LOCAL_FAULT_PS,"fault ps1")]:
        text = path.read_text(encoding="utf-8")
        require(text, ["test-evidence-gate.py", "forbid-skipped"], label)
    for path, label in [(LOCAL_SCALE_SH,"scale sh"),(LOCAL_SCALE_PS,"scale ps1")]:
        text = path.read_text(encoding="utf-8")
        require(text, ["performance-evidence-gate.py", "performance-baseline.json", "JORNADA_LOCKED_RESTORE"], label)

    require(LINKAGE_EVAL_SMOKE.read_text(encoding="utf-8"), [
        "linkage-evaluation-evidence-gate.py", "linkage-evaluation-policy.json", "evidence-gate-summary.json"
    ], "smoke de evidência linkage")

    # v3.73: regressões deixam de depender apenas de vetores fixos. Property tests são
    # determinísticos por seed e exercitam CPF, telefone/e-mail e ZIP canônico.
    property_tests = (ROOT / "tests" / "Jornada.Tests" / "Unit" / "DeterministicPropertyTests.cs").read_text(encoding="utf-8")
    require(property_tests, [
        "new Random(37301)", "new Random(37302)", "new Random(37303)", "new Random(37304)",
        "Cpf_normalization_preserves_generated_valid_values", "Phone_v2_is_invariant_to_ascii_punctuation",
        "Email_v2_is_ascii_case_idempotent", "Deterministic_zip_is_byte_stable"
    ], "property/fuzz tests determinísticos")

    replay_tests = (ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "IdentityReplayInvariantTests.cs").read_text(encoding="utf-8")
    require(replay_tests, [
        "Recompose_gold_person_is_idempotent_on_business_state",
        "Identity_graph_global_invariants_hold_after_seed_and_recomposition_replay",
        "cycle_roots", "cpf_ativo_em_fundido", "fato_atribuido_sem_uuid"
    ], "invariantes/replay de identidade")
    pipeline_tests = (ROOT / "tests" / "Jornada.Integration.Tests" / "Integration" / "PipelineCoordinationTests.cs").read_text(encoding="utf-8")
    require(pipeline_tests, [
        "Eight_simultaneous_processor_contenders_have_exactly_one_winner_and_gate_recovers",
        "const int contenderCount = 8", "Enumerable.Range(0, contenderCount)",
        "allAttemptsCompleted", "Volatile.Read(ref acquired), Is.EqualTo(1)"
    ], "concorrência determinística v4.03")

    possibility_engine = (ROOT / "src" / "Jornada.Contracts" / "PossibilityRules.cs").read_text(encoding="utf-8")
    require(possibility_engine, [
        "PossibilityRuleValidator", "PossibilityRuleEngine", "PossibilityImpactSimulator",
        "PossibilityRuleApproval", "LoadFromFile", "SHA-256 da evidência de aprovação",
        "DADO_AUSENTE", "Dry-run só compara versões da mesma Natureza/Código"
    ], "framework de Possibilidades")
    possibility_tests = (ROOT / "tests" / "Jornada.Tests" / "Unit" / "PossibilityRuleEngineTests.cs").read_text(encoding="utf-8")
    require(possibility_tests, [
        "Rule_engine_distinguishes_compatible_incompatible_and_not_evaluable",
        "Dry_run_reports_entries_and_exits_without_publishing",
        "Invalid_or_empty_published_rule_fails_closed",
        "Approved_catalog_requires_complete_approval_metadata",
        "Approved_catalog_file_verifies_evidence_sha_and_rejects_tampering"
    ], "testes de Possibilidades")
    possibility_catalog = ROOT / "config" / "possibilities" / "rule-catalog.json"
    possibility_data = json.loads(possibility_catalog.read_text(encoding="utf-8"))
    if possibility_data.get("schemaVersion") != 1 or possibility_data.get("status") != "PENDENTE" or possibility_data.get("rules") != []:
        fail("catálogo inicial de Possibilidades deve permanecer PENDENTE e sem regras institucionais inventadas")
    possibility_gate = (ROOT / "scripts" / "possibility-rules-gate.py").read_text(encoding="utf-8")
    require(possibility_gate, ["--require-approved", "evidenceSha256", "status=PENDENTE não pode distribuir regras como vigentes", "POSSIBILITY RULES GATE: OK"], "gate de Possibilidades")

    scale_sql = (ROOT / "database" / "Jornada_Dev_SyntheticScale.sql").read_text(encoding="utf-8")
    require(scale_sql, ["SCALE_COLLISION_MODULO", "SCALE_BIRTH_SHIFT_MODULO", "Pessoa Colisao", "collision_modulo", "birth_shift_modulo"], "massa sintética controlada")
    release_evidence_gate = (ROOT / "scripts" / "release-evidence-gate.py").read_text(encoding="utf-8")
    release_evidence_policy = json.loads((ROOT / "config" / "release" / "release-evidence-policy.json").read_text(encoding="utf-8"))
    require(release_evidence_gate, ["RELEASE_EVIDENCE.json", "evidenceCombinedSha256", "releaseEvidenceEnvelopeSha256", "releaseInfoSha256", "policySha256", "artifactFileCount", "inventory_artifact", "RELEASE EVIDENCE GATE: OK"], "gate agregado de evidências")
    expected_artifacts = {"release-static-gates","nuget-lockfiles","unit-test-evidence","openapi-runtime-evidence","integration-test-evidence","fault-injection-evidence","sbom-cyclonedx","linkage-evaluation-smoke","ddl-upgrade-evidence","local-e2e-evidence","bronze-restore-drill","scale-harness-smoke","release-source-provenance","deterministic-build-evidence","security-analysis-evidence"}
    configured_artifacts = {x.get("name") for x in release_evidence_policy.get("requiredArtifacts", [])}
    if configured_artifacts != expected_artifacts:
        fail(f"política de evidência de release incompleta/divergente: {sorted(configured_artifacts)}")

    # v3.75+: governança/scheduler/ambiente deixam de depender somente de revisão manual.
    for gate_path, tokens, label in [
        (AUTHORIZATION_MATRIX_GATE, ["AUTHORIZATION MATRIX GATE: OK", "allowTypeCredentials", "IsAllowedAsync"], "matriz de autorização"),
        (SECURITY_SURFACE_GATE, ["SECURITY SURFACE GATE: OK", "X-Jornada-Agente-CPF", "serialização sensível", "Exception.Message", "SRC.rglob"], "superfície de minimização/logs"),
        (COMPATIBILITY_MATRIX_GATE, ["COMPATIBILITY MATRIX GATE: OK", "SQL_SCHEMA_INCOMPATIVEL", "Jornada_Fase1_v3.65.sql"], "matriz de compatibilidade"),
        (OBSERVABILITY_GATE, ["OBSERVABILITY CONTRACT GATE: OK", "JornadaTelemetry", "thresholdStatus", "alerts", "requiredDimensions"], "contrato de observabilidade"),
        (HML_STALENESS_GATE, ["HML STALENESS GATE: OK", "maximumApprovalAgeDays", "approvalContext", "--strict", "technicalFingerprintSha256", "technical_fingerprint"], "expiração de calibração"),
        (BRONZE_DEEP_GATE, ["BRONZE DEEP EVIDENCE GATE: OK", "DRY_RUN_ONLY", "minimum-gc-candidates"], "verificação profunda/GC Bronze"),
        (UPGRADE_INVARIANT_GATE, ["UPGRADE INVARIANT GATE: OK", "activeCpfDuplicate", "multipleActiveVinculoPerObservation"], "invariantes de upgrade"),
    ]:
        require(gate_path.read_text(encoding="utf-8"), tokens, label)

    for config_path in [AUTHORIZATION_MATRIX, DATA_MINIMIZATION_POLICY, COMPATIBILITY_MATRIX, OBSERVABILITY_CATALOG, CALIBRATION_VALIDITY]:
        if not config_path.is_file():
            fail(f"contrato v3.76 ausente: {config_path}")
        data = json.loads(config_path.read_text(encoding="utf-8"))
        if data.get("schemaVersion") != 1:
            fail(f"schemaVersion inválido em {config_path.name}")

    calibration_data = json.loads(CALIBRATION_VALIDITY.read_text(encoding="utf-8"))
    if "technicalFingerprintSha256" not in calibration_data.get("requiredApprovalContext", []):
        fail("calibration-validity-policy deve exigir technicalFingerprintSha256")
    if not calibration_data.get("technicalFingerprintInputs"):
        fail("calibration-validity-policy deve declarar inputs do fingerprint técnico")

    auth_data = json.loads(AUTHORIZATION_MATRIX.read_text(encoding="utf-8"))
    auth_routes = auth_data.get("routes", [])
    if not isinstance(auth_routes, list) or not auth_routes:
        fail("authorization-matrix deve conter rotas protegidas")
    normalize_route = lambda path: re.sub(r"\{([^}:]+):[^}]+\}", r"{\1}", path.rstrip("/") or "/")
    actual_routes = [(row["method"].upper(), normalize_route(row["path"])) for row in auth_routes]
    if len(actual_routes) != len(set(actual_routes)):
        fail("authorization-matrix contém rotas duplicadas")
    openapi = json.loads((ROOT / "openapi" / "jornada-v1.openapi.json").read_text(encoding="utf-8"))
    http_methods = {"get", "post", "put", "delete", "patch", "options", "head", "trace"}
    expected_routes = {
        (method.upper(), normalize_route(path))
        for path, item in openapi.get("paths", {}).items() if path.startswith("/api/")
        for method in item if method.lower() in http_methods
    }
    if set(actual_routes) != expected_routes:
        fail("authorization-matrix deve cobrir exatamente as operações /api publicadas no OpenAPI")
    require(AUTHORIZATION_TESTS.read_text(encoding="utf-8"), [
        "Development_credentials_do_not_grant_privileged_identity_scopes_to_type_credentials",
        "Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes"
    ], "testes de matriz de autorização")

    telemetry = JORNADA_TELEMETRY.read_text(encoding="utf-8")
    obs_data = json.loads(OBSERVABILITY_CATALOG.read_text(encoding="utf-8"))
    for metric in obs_data.get("metrics", []):
        mid = metric.get("id")
        if mid not in telemetry:
            fail(f"métrica catalogada sem instrumento: {mid}")
    if len(obs_data.get("alerts", [])) < 4:
        fail("catálogo de observabilidade deve conter alertas técnicos PENDENTE_HML")
    for metric in obs_data.get("metrics", []):
        for dim in metric.get("requiredDimensions", []):
            if f'"{dim}"' not in telemetry:
                fail(f"dimensão catalogada sem emissão em JornadaTelemetry: {metric.get('id')}:{dim}")
    require(TELEMETRY_TESTS.read_text(encoding="utf-8"), [
        "Telemetry_emits_only_catalogued_non_sensitive_dimensions_for_api_request",
        "Every_metric_declared_in_catalog_exists_in_Jornada_meter"
    ], "testes de telemetria/minimização")

    require(BRONZE_VERIFY.read_text(encoding="utf-8"), [
        "--deep", "--gc-plan", "DRY_RUN_ONLY", "UNREFERENCED_AND_OLDER_THAN_GRACE",
        "keyHashMismatch", "metadataConflicts", "physicalDivergent"
    ], "Bronze.Verify profundo e GC dry-run")
    if "DeleteIfExistsAsync(" in BRONZE_VERIFY.read_text(encoding="utf-8"):
        fail("Bronze.Verify não pode executar deleção; --gc-plan deve permanecer DRY_RUN_ONLY")
    require(UPGRADE_INVARIANTS_SQL.read_text(encoding="utf-8"), [
        "identityMapMissingPerson", "resolvedVinculoMissingPerson", "activeCpfDuplicate",
        "multipleActiveVinculoPerObservation", "FOR JSON PATH, WITHOUT_ARRAY_WRAPPER"
    ], "snapshot de invariantes DDL")
    require((ROOT / "scripts" / "local-ddl-upgrade.sh").read_text(encoding="utf-8"), [
        "Jornada_Upgrade_Invariants.sql", "upgrade-invariant-gate.py", "upgrade_invariants=true", "-C -b -I"
    ], "harness upgrade com invariantes")
    require(LOCAL_BACKUP_SH.read_text(encoding="utf-8"), [
        "--deep", "--gc-plan", "bronze-deep-evidence-gate.py", "ORPHAN-DRY-RUN", "gcDryRunPlanPassed",
        "compose exec -T -u 0 sqlserver cat \"/var/opt/mssql/backup/$BACKUP_FILE\" > \"$EVID/$BACKUP_FILE\"",
        "SEED_CANONICAL_SHA=\"8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2\"",
        "printf 'PK\\003\\004' > \"$SEED_CANONICAL_DEST\"",
        "--minimum-count 1 --deep --report"
    ], "restore Bronze profundo/dry-run")
    require(LOCAL_BACKUP_PS.read_text(encoding="utf-8"), [
        "--deep", "--gc-plan", "bronze-deep-evidence-gate.py", "JORNADA-ORPHAN-DRY-RUN", "gcDryRunPlanPassed"
    ], "restore Bronze profundo/dry-run ps1")
    require(HML_READINESS_GATE.read_text(encoding="utf-8"), ["hml-staleness-gate.py", "calibrationFreshness=PASS"], "readiness HML com expiração")
    require(HML_READINESS_GATE_PS.read_text(encoding="utf-8"), ["hml-staleness-gate.py", "calibrationFreshness=PASS"], "readiness HML ps1 com expiração")

    # v3.76: compatibilidade/supply-chain/reprodutibilidade deixam de depender de revisão manual.
    for gate_path, tokens, label in [
        (BACKWARD_COMPAT_GATE, ["CONTRACT BACKWARD COMPATIBILITY GATE: OK", "source_git_predecessor_tag", "JSON Schema removido"], "compatibilidade retroativa OpenAPI/JSON"),
        (DDL_DESTRUCTIVE_GATE, ["DDL DESTRUCTIVE CHANGE GATE: OK", "DROP\\s+", "migrationEvidence"], "DDL destrutivo"),
        (DEPENDENCY_DRIFT_GATE, ["DEPENDENCY DRIFT GATE: OK", "PackageReference", "allowedChanges"], "drift de dependências"),
        (WORKFLOW_PIN_GATE, ["WORKFLOW ACTION PIN GATE: OK", "[0-9a-f]{40}"], "pin de actions"),
        (SOURCE_SANITY_GATE, ["SOURCE SANITY GATE: OK", "PRIVATE KEY", "Encrypt"], "sanidade da fonte"),
        (COVERAGE_GATE, ["COVERAGE EVIDENCE GATE: OK", "approvedLineRate", "maximumRegressionPercentagePoints"], "coverage ratchet"),
        (VULNERABILITY_GATE, ["VULNERABILITY EVIDENCE GATE: OK", "High", "Critical"], "vulnerabilidades NuGet"),
        (SARIF_GATE, ["SARIF SECURITY GATE: OK", "security-severity", "8.0"], "CodeQL SARIF"),
        (DETERMINISTIC_BUILD_GATE, ["DETERMINISTIC BUILD GATE: OK", "build1.sha256", "build2.sha256"], "build determinístico"),
    ]:
        require(gate_path.read_text(encoding="utf-8"), tokens, label)
    for config_path in [DDL_CHANGE_POLICY, COVERAGE_BASELINE, DEPENDENCY_DRIFT_POLICY, SOURCE_SANITY_POLICY]:
        if not config_path.is_file(): fail(f"contrato v3.76 ausente: {config_path}")
        if json.loads(config_path.read_text(encoding="utf-8")).get("schemaVersion") != 1: fail(f"schemaVersion inválido em {config_path.name}")
    props = DIRECTORY_BUILD_PROPS.read_text(encoding="utf-8")
    require(props, ["<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", "<Deterministic>true</Deterministic>", "<ContinuousIntegrationBuild Condition=", "<DebugType>portable</DebugType>"], "Directory.Build.props")
    require(TEST_CSPROJ.read_text(encoding="utf-8"), ["coverlet.collector", "<PrivateAssets>all</PrivateAssets>"], "collector de cobertura")
    if "--version 3.60" in TEST_RUNBOOK.read_text(encoding="utf-8"):
        fail("Runbook de testes ainda força SBOM --version 3.60")
    if release_evidence_policy.get("policyVersion") != "RELEASE_EVIDENCE_V5":
        fail("release-evidence-policy deve estar em RELEASE_EVIDENCE_V5")

    # v3.77: três gaps de contrato que ainda podem revelar defeito + três hardenings conservadores.
    require(JSON_SCHEMA_META_GATE.read_text(encoding="utf-8"), [
        "JSON SCHEMA META GATE: OK", "draft/2020-12", "$id duplicado", "$ref local não resolvido", "keyword desconhecida"
    ], "meta-validação JSON Schema")
    require(ARCHITECTURE_GATE.read_text(encoding="utf-8"), [
        "ARCHITECTURE DEPENDENCY GATE: OK", "ProjectReference", "núcleo deve permanecer sem ProjectReference"
    ], "dependências arquiteturais")
    require(ANALYZER_CLEANLINESS_GATE.read_text(encoding="utf-8"), [
        "ANALYZER CLEANLINESS GATE: OK", "CodeAnalysisTreatWarningsAsErrors", "InvariantCulture", "base.DisposeAsync"
    ], "baseline de warnings de analyzer")
    architecture = json.loads(ARCHITECTURE_POLICY.read_text(encoding="utf-8"))
    if architecture.get("status") != "VIGENTE" or len(architecture.get("projects") or {}) != 18:
        fail("architecture-dependencies.json deve cobrir os 18 projetos")
    require(OPENAPI_RUNTIME_TESTS.read_text(encoding="utf-8"), [
        '[Category("OpenApiRuntime")]', "Runtime_response_matches_declared_status_media_and_json_shape",
        "Runtime_probe_catalog_covers_every_openapi_operation_once", "/health/ready", "/api/v1/pessoas/{pessoaUuid}/possibilidades"
    ], "conformidade OpenAPI em runtime")
    require(BACKWARD_COMPAT_GATE.read_text(encoding="utf-8"), [
        "parâmetro existente passou de opcional para obrigatório", "compare_security", "header de resposta removido",
        "nullable true -> false", "format alterado/removido", "exclusiveMinimum"
    ], "backward compatibility ampliado")
    require(props, [
        "<EnableNETAnalyzers>true</EnableNETAnalyzers>", "<AnalysisLevel>8.0-recommended</AnalysisLevel>", "<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>",
        "<CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>"
    ], "analyzers fail-closed")
    analyzer_policy = (ROOT / ".editorconfig").read_text(encoding="utf-8")
    require(analyzer_policy, [
        "dotnet_diagnostic.CA1014.severity = none",
        "dotnet_diagnostic.CA1707.severity = none",
        "dotnet_diagnostic.CA1848.severity = none",
        "dotnet_diagnostic.CA1859.severity = none",
        "dotnet_diagnostic.CA1861.severity = none",
        "dotnet_diagnostic.CA1822.severity = none",
    ], "política explícita de analyzers aceitos")
    for cleanup_file in [ROOT / "src/Jornada.Bronze.Storage/BronzeObjectStorage.cs", ROOT / "src/Jornada.Api/ApiHealth.cs", ROOT / "src/Jornada.Api/IngestionStaging.cs"]:
        require(cleanup_file.read_text(encoding="utf-8"), ["Falha best-effort", "ex.GetType().Name"], f"cleanup observável {cleanup_file.name}")

    # v3.78: defeitos confirmados pela primeira compilação real da v3.77.
    require((ROOT / "src/Jornada.Bronze.Verify/Jornada.Bronze.Verify.csproj").read_text(encoding="utf-8"), [
        'Microsoft.Extensions.Configuration.EnvironmentVariables" Version="8.0.0"'
    ], "fix NU1601 Bronze.Verify")
    require((ROOT / "src/Jornada.Api/Program.cs").read_text(encoding="utf-8"), ["using Microsoft.Data.SqlClient;"], "SqlException namespace")
    require((ROOT / "src/Jornada.Api/ApiEntryPointMarker.cs").read_text(encoding="utf-8"), ["public sealed class ApiEntryPointMarker"], "marcador do assembly API")
    require(OPENAPI_RUNTIME_TESTS.read_text(encoding="utf-8"), ["WebApplicationFactory<ApiEntryPointMarker>"], "OpenAPI runtime entry point inequívoco")
    require((ROOT / "tests/Jornada.Tests/Unit/ApiHttpPipelineTests.cs").read_text(encoding="utf-8"), ["WebApplicationFactory<ApiEntryPointMarker>"], "API HTTP entry point inequívoco")
    require((ROOT / "src/Jornada.Linkage.Parameters.Worker/LinkageParametersWorker.cs").read_text(encoding="utf-8"), ["Value = matchedPairs.Count;"], "amostra m compilável")
    # A margem passou para a política compartilhada SQL Server/PostgreSQL.
    # Aceita o baseline legado ou a delegação explícita, sem enfraquecer a prova.
    sql_linkage = (ROOT / "src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs").read_text(encoding="utf-8")
    if "decimal? margin =" in sql_linkage:
        require(sql_linkage, ["decimal? margin ="], "margem nullable explícita")
    else:
        require(sql_linkage, [
            "return ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);",
        ], "delegação da decisão probabilística SQL Server")
        shared_policy_path = ROOT / "src/Jornada.Linkage.Runner/ProbabilisticLinkagePolicy.cs"
        if not shared_policy_path.is_file():
            fail("política compartilhada de Linkage ausente")
        shared_policy = shared_policy_path.read_text(encoding="utf-8")
        require(shared_policy, [
            "internal static class ProbabilisticLinkageDecisions",
            "decimal? margin =",
            "best.Score - secondScore.Value",
            "best.Score < model.Threshold",
            "margin!.Value < model.ConflictMargin",
            "ThenBy(x => x.PessoaUuid)",
        ], "margem nullable e decisão probabilística compartilhadas")
    require((ROOT / "src/Jornada.Processor.Worker/ProcessorWorker.cs").read_text(encoding="utf-8"), ["internal sealed class ProcessorWorker("], "acessibilidade ProcessorWorker")
    ingestion_processor = (ROOT / "src/Jornada.Processor.Worker/IngestionProcessor.cs").read_text(encoding="utf-8")
    require(ingestion_processor, [
        "catch (InvalidDataException ex)",
        "ClassifyValidationStage(ex, validationPhase)",
        "private static string ClassifyValidationStage(InvalidDataException exception, string validationPhase)",
        "exception.StackTrace",
        "validationPhase = \"PARSE\"",
        "validationPhase = \"PERSISTENCIA\"",
        "PERSISTENCIA_ATRIBUTO",
        "PERSISTENCIA_TERRITORIO",
        "Etapa={ValidationStage}",
    ], "diagnóstico seguro de validação do Processor")
    linkage_parameters_worker = (ROOT / "src/Jornada.Linkage.Parameters.Worker/LinkageParametersWorker.cs").read_text(encoding="utf-8")
    require(linkage_parameters_worker, [
        "nome NOT IN('M_SAMPLE_SIZE','U_SAMPLE_SIZE')",
    ], "domínio probabilístico não pode tratar tamanhos de amostra como probabilidade")
    if 'logger.LogWarning(ex, "Entrega {EntregaId} rejeitada' in ingestion_processor or 'logger.LogError(ex, "Entrega {EntregaId} rejeitada' in ingestion_processor:
        fail("IngestionProcessor não pode registrar a exceção bruta da rejeição de validação")
    require((ROOT / "tests/Jornada.Tests/Unit/PossibilityRuleEngineTests.cs").read_text(encoding="utf-8"), ['var json = $$$"""', '"{{{sha}}}"'], "raw string interpolada de Possibilidades")
    require((ROOT / "tests/Jornada.Tests/Unit/SqlGovernanceArtifactTests.cs").read_text(encoding="utf-8"), ["using NUnit.Framework.Legacy;"], "NUnit 4 StringAssert")

    # v3.79: correções confirmadas pela primeira execução Unit real após build verde.
    trusted_proxy_tests = (ROOT / "tests/Jornada.Tests/Unit/TrustedProxyConfigurationTests.cs").read_text(encoding="utf-8")
    require(trusted_proxy_tests, ["using Microsoft.AspNetCore.Builder;", "new ForwardedHeadersOptions()"], "namespace ForwardedHeadersOptions")
    if "using Microsoft.AspNetCore.HttpOverrides;" in trusted_proxy_tests:
        fail("TrustedProxyConfigurationTests voltou ao namespace incorreto de ForwardedHeadersOptions")
    api_http_tests = (ROOT / "tests/Jornada.Tests/Unit/ApiHttpPipelineTests.cs").read_text(encoding="utf-8")
    require(api_http_tests, [
        'builder.ConfigureAppConfiguration((_, config) =>',
        '["BronzeStorage:RootPath"] = bronzeRoot',
        '["IngestionStaging:RootPath"] = stagingRoot',
        'InMemoryApiAuditSink',
        'InMemorySqlReadinessProbe',
        'Path.GetTempPath()',
        'Directory.Delete(root, recursive: true)'
    ], "fixture HTTP Production autocontida e sem SQL")
    zip_tests = (ROOT / "tests/Jornada.Tests/Unit/DeterministicIngestionZipTests.cs").read_text(encoding="utf-8")
    require(zip_tests, [
        "e.LastWriteTime.DateTime == DeterministicIngestionZipWriter.CanonicalEntryTimestamp.DateTime",
        "Same_logical_package_bytes_generate_identical_zip_and_sha256"
    ], "timestamp ZIP sem offset artificial")
    schema_tests = (ROOT / "tests/Jornada.Tests/Unit/JsonSchemaSubsetValidatorTests.cs").read_text(encoding="utf-8")
    require(schema_tests, [
        '"situacaoGeografia":"RESOLVIDA"',
        '"referenciaMalha":"ORIGEM-2026"',
        "Person_schema_accepts_declared_territorial_reference_without_postal_address"
    ], "fixtures territoriais alinhadas ao schema")

    # v3.80: unit/runtime HTTP sem banco e baseline de analyzers fail-closed.
    audit_sink = (ROOT / "src/Jornada.Api/ApiAuditSink.cs").read_text(encoding="utf-8")
    require(audit_sink, ["interface IApiAuditSink", "class SqlApiAuditSink", "CultureInfo.InvariantCulture"], "abstração de auditoria SQL")
    api_health = (ROOT / "src/Jornada.Api/ApiHealth.cs").read_text(encoding="utf-8")
    require(api_health, ["interface ISqlReadinessProbe", "class SqlSchemaReadinessProbe", "ApiReadinessProbe(ISqlReadinessProbe"], "abstração de readiness SQL")
    in_memory_tests = (ROOT / "tests/Jornada.Tests/Unit/ApiInMemoryTestInfrastructure.cs").read_text(encoding="utf-8")
    require(in_memory_tests, ["class InMemoryApiAuditSink", "class InMemorySqlReadinessProbe", "Task.CompletedTask"], "doubles de API em memória")
    if "ConnectionStrings:Jornada" in OPENAPI_RUNTIME_TESTS.read_text(encoding="utf-8"):
        fail("OpenApiRuntimeConformanceTests voltou a depender de connection string SQL")
    require(OPENAPI_RUNTIME_TESTS.read_text(encoding="utf-8"), ["InMemoryApiAuditSink", "InMemorySqlReadinessProbe"], "OpenAPI runtime sem SQL")

    runtime_smoke = RUNTIME_SMOKE.read_text(encoding="utf-8")
    require(runtime_smoke, [
        "sys.sp_refreshsqlmodule N'identidade.sp_recompor_gold_pessoa'",
        "EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@existente",
        "EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@sem_observacao",
        "JORNADA SQL RUNTIME SMOKE: OK",
    ], "runtime smoke SQL")
    ci = CI_WORKFLOW.read_text(encoding="utf-8")
    require(ci, ["Jornada_Runtime_Smoke.sql", "SQL runtime semantic smoke"], "CI runtime smoke")
    require(ci, [
        "Generate and validate NuGet lock files on branches and PRs",
        "Require committed NuGet lock files on release tags",
        "github.ref_type == 'tag'",
        "dotnet restore Jornada.sln --locked-mode",
        "Unit tests must execute without skips",
        "unit.trx",
        "Integration tests must execute without skips",
        "integration.trx",
        "Fault injection must execute without skips",
        "tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj",
        "nuget-lock-provenance-gate.py",
        "test-evidence-gate.py",
        "--forbid-skipped",
        "bronze-restore-drill:",
        "SQL + Bronze restore drill with negative storage faults",
        "scale-harness:",
        "local-scale.sh",
        "Gate Power BI source structure",
        "powerbi-static-gate.py",
        "Gate HML contracts without fabricating approvals",
        "hml-config-gate.py",
        "release-promotion:",
        "bronze-restore-drill, scale-harness",
        "fetch-depth: 0",
        "release-source-gate.py",
        "build-release-source-bundle.sh",
        "Download all prerequisite release evidence",
        "release-evidence-gate.py",
        "release-static-gates",
        ".local/release-static/technical-closure.txt",
        "RELEASE_EVIDENCE.json",
        "SCALE_COLLISION_MODULO=37",
        "SCALE_BIRTH_SHIFT_MODULO=29",
        "Gate authorization matrix",
        "authorization-matrix-gate.py",
        "security-surface-gate.py",
        "compatibility-matrix-gate.py",
        "observability-contract-gate.py",
        "hml-staleness-gate.py",
        "environment-preflight-gate.py",
        "governance-readiness-gate.py",
        "scheduler-contract-gate.py",
        "sql-performance-evidence-gate.py",
        "api-projection-evidence-gate.py",
        "contract-backward-compatibility-gate.py",
        "json-schema-meta-gate.py",
        "architecture-dependency-gate.py",
        "OpenAPI runtime conformance must execute for all 19 operations",
        "openapi-runtime.trx",
        "ddl-destructive-change-gate.py",
        "dependency-drift-gate.py",
        "coverage-evidence-gate.py",
        "deterministic-build:",
        "deterministic-build-gate.sh",
        "security-analysis:",
        "github/codeql-action/init@99df26d4f13ea111d4ec1a7dddef6063f76b97e9",
        "sarif-security-gate.py",
        "actions/attest@508db95dd578ae2727ebd6217d5ba78e4fbda05d",
        "id-token: write",
        "attestations: write",
    ], "CI de promoção fail-closed + evidência de execução")
    require(LOCAL_RUNTIME_SH.read_text(encoding="utf-8"), ["Jornada_Runtime_Smoke.sql"], "local runtime smoke sh")
    require(LOCAL_RUNTIME_PS.read_text(encoding="utf-8"), ["Jornada_Runtime_Smoke.sql"], "local runtime smoke ps1")
    require(LOCAL_FAULT_SH.read_text(encoding="utf-8"), ["Jornada.Integration.Tests/Jornada.Integration.Tests.csproj", "TestCategory=FaultInjection"], "fault injection local sh no projeto Integration")
    require(LOCAL_FAULT_PS.read_text(encoding="utf-8"), ["Jornada.Integration.Tests/Jornada.Integration.Tests.csproj", "TestCategory=FaultInjection"], "fault injection local ps1 no projeto Integration")

    print(f"TECHNICAL CLOSURE GATE: OK ({len(procedures)} closure procedures; {audited_multiwrite} multi-write procedures audited; {len(cases)} phone vectors; {len(email_cases)} email vectors; 51110-51119 contracts; schema 3.62/3.69 readiness; Git source provenance; zero-skip unit/integration/fault gates; fault/restore/scale/PowerBI gates wired; executable HML baseline/linkage contracts; deterministic property/replay/concurrency tests; governed Possibilities; aggregate release evidence; authorization/security/compatibility/observability contracts; Bronze deep+GC dry-run; upgrade invariants; HML staleness; governance/scheduler/environment contracts; SQL/API HML evidence gates; runtime OpenAPI conformance; extended backward compatibility; JSON Schema meta-validation; architecture dependency gate; fail-closed analyzer policy; in-memory unit/runtime API infrastructure; observable best-effort cleanup; backward contract+DDL+dependency drift gates; source/action pin security; coverage ratchet; deterministic build; CodeQL/SARIF+NuGet vulnerability gates; OIDC attestation wired; tag promotion fail-closed; runtime SQL smoke wired)")


if __name__ == "__main__":
    main()
