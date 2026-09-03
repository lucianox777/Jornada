#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import re
import sys
from pathlib import Path

SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
PARAM_STATUSES = {"CALIBRAR", "DECIDIR", "PENDENTE", "PENDENTE_HML", "APROVADO"}
TOP_STATUSES = {"PENDENTE", "APROVADO"}
LINKAGE_DECISIONS = {"SEM_DECISAO", "MANTER_V1", "SUBMETER_V2_PARA_REVISAO_NORMATIVA"}

REQUIRED_PARAMETER_IDS = {
    "ApiRateLimiting.StandardPermitLimit",
    "ApiRateLimiting.IdentityPermitLimit",
    "ApiRateLimiting.IngestionPermitLimit",
    "ApiRateLimiting.EdgeMultiplier",
    "Staging.CleanupIntervalMinutes",
    "Staging.MaxAgeHours",
    "Processor.PollingMilliseconds",
    "Processor.MaxPessoasPorEntrega",
    "Processor.MaxRegistrosPorEntrega",
    "Processor.LeaseDurationSeconds",
    "Processor.HeartbeatSeconds",
    "Processor.MaxProcessingAttempts",
    "Processor.RetryBaseSeconds",
    "Processor.RetryMaxSeconds",
    "Linkage.Thresholds",
    "ProbabilisticLinkage.CommandTimeoutSeconds",
    "ProbabilisticLinkage.FreezeUniverseCommandTimeoutSeconds",
    "LinkageParameters.SmoothingAlpha",
    "Linkage.UniverseHighWatermark",
    "Identity.PendingAging",
    "ItemProcessed.DetailRetentionDays",
    "Bronze.RetentionDays",
    "Bronze.OrphanGraceHours",
    "ReverseProxy.KnownProxiesKnownNetworks",
    "BronzeStorage.RootPath",
    "PipelineWatchdog.IntervalMinutes",
    "PipelineWatchdog.LinkageRunMaxMinutes",
    "PipelineWatchdog.ModelGenerationMaxMinutes",
    "PipelineWatchdog.ExpiredLeaseGraceMinutes",
    "PipelineWatchdog.PendingBacklogMaxAgeMinutes",
    "PipelineWatchdog.InitialLoadMaxHours",
}


def load_json(path: Path) -> dict:
    if not path.is_file():
        raise ValueError(f"arquivo ausente: {path}")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON raiz deve ser objeto: {path}")
    return value


def valid_iso_utc(value: object) -> bool:
    if not isinstance(value, str) or not value.endswith("Z"):
        return False
    try:
        dt.datetime.fromisoformat(value[:-1] + "+00:00")
        return True
    except ValueError:
        return False


def validate_evidence(value: object, prefix: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{prefix}.evidence deve ser objeto com artifact+sha256")
        return
    artifact = value.get("artifact")
    sha = value.get("sha256")
    if not isinstance(artifact, str) or not artifact.strip():
        errors.append(f"{prefix}.evidence.artifact ausente")
    if not isinstance(sha, str) or not SHA256_RE.fullmatch(sha.lower()):
        errors.append(f"{prefix}.evidence.sha256 inválido")


def validate_approval_fields(obj: dict, prefix: str, errors: list[str]) -> None:
    if obj.get("approvedValue") is None and prefix.startswith("parameters["):
        errors.append(f"{prefix}.approvedValue ausente")
    if not valid_iso_utc(obj.get("approvedAtUtc")):
        errors.append(f"{prefix}.approvedAtUtc deve ser ISO-8601 UTC terminado em Z")
    if not isinstance(obj.get("approvedBy"), str) or not obj.get("approvedBy", "").strip():
        errors.append(f"{prefix}.approvedBy ausente")
    validate_evidence(obj.get("evidence"), prefix, errors)




def validate_approval_context(obj: dict, prefix: str, errors: list[str]) -> None:
    ctx = obj.get("approvalContext")
    if not isinstance(ctx, dict):
        errors.append(f"{prefix}.approvalContext ausente")
        return
    for field in ("sourceGitTag", "solutionSchema", "environmentProfile", "corpusDefinitionSha256", "technicalFingerprintSha256"):
        value = ctx.get(field)
        if not isinstance(value, str) or not value.strip():
            errors.append(f"{prefix}.approvalContext.{field} ausente")
    sha = ctx.get("corpusDefinitionSha256")
    if isinstance(sha, str) and not SHA256_RE.fullmatch(sha.lower()):
        errors.append(f"{prefix}.approvalContext.corpusDefinitionSha256 inválido")
    technical_sha = ctx.get("technicalFingerprintSha256")
    if isinstance(technical_sha, str) and not SHA256_RE.fullmatch(technical_sha.lower()):
        errors.append(f"{prefix}.approvalContext.technicalFingerprintSha256 inválido")
    if ctx.get("solutionSchema") != "3.68":
        errors.append(f"{prefix}.approvalContext.solutionSchema deve ser 3.68")

def validate_parameters(data: dict, require_approved: bool) -> list[str]:
    errors: list[str] = []
    if data.get("schemaVersion") != 1:
        errors.append("parameters.schemaVersion deve ser 1")
    if data.get("baseNormativa") != "3.62" or data.get("solutionSchema") != "3.68":
        errors.append("parameters deve declarar baseNormativa=3.62 e solutionSchema=3.68")
    top_status = data.get("status")
    if top_status not in TOP_STATUSES:
        errors.append(f"parameters.status inválido: {top_status!r}")
    params = data.get("parameters")
    if not isinstance(params, list):
        return errors + ["parameters.parameters deve ser array"]
    seen: set[str] = set()
    for i, item in enumerate(params):
        prefix = f"parameters[{i}]"
        if not isinstance(item, dict):
            errors.append(f"{prefix} deve ser objeto")
            continue
        pid = item.get("id")
        if not isinstance(pid, str) or not pid:
            errors.append(f"{prefix}.id ausente")
            continue
        if pid in seen:
            errors.append(f"parâmetro duplicado: {pid}")
        seen.add(pid)
        if item.get("status") not in PARAM_STATUSES:
            errors.append(f"{pid}: status inválido {item.get('status')!r}")
        if not isinstance(item.get("component"), str) or not item.get("component"):
            errors.append(f"{pid}: component ausente")
        if "technicalDefault" not in item:
            errors.append(f"{pid}: technicalDefault ausente")
        if item.get("status") == "APROVADO":
            validate_approval_fields(item, prefix, errors)
    missing = sorted(REQUIRED_PARAMETER_IDS - seen)
    extra = sorted(seen - REQUIRED_PARAMETER_IDS)
    if missing:
        errors.append("parâmetros obrigatórios ausentes: " + ", ".join(missing))
    if extra:
        errors.append("parâmetros não catalogados no gate: " + ", ".join(extra))
    all_approved = bool(params) and all(isinstance(p, dict) and p.get("status") == "APROVADO" for p in params)
    if top_status == "APROVADO" and not all_approved:
        errors.append("parameters.status=APROVADO exige todos os parâmetros APROVADO")
    if top_status == "APROVADO":
        validate_approval_context(data, "parameters", errors)
    if require_approved and (top_status != "APROVADO" or not all_approved):
        errors.append("matriz HML ainda não está integralmente APROVADA")
    return errors


def validate_performance_baseline(data: dict, require_approved: bool) -> list[str]:
    errors: list[str] = []
    if data.get("schemaVersion") != 1:
        errors.append("performance-baseline.schemaVersion deve ser 1")
    status = data.get("status")
    if status not in TOP_STATUSES:
        errors.append(f"performance-baseline.status inválido: {status!r}")
    profiles = data.get("profiles")
    if not isinstance(profiles, dict):
        errors.append("performance-baseline.profiles deve ser objeto")
        profiles = {}
    for name, limits in profiles.items():
        if not isinstance(name, str) or not name:
            errors.append("performance-baseline contém nome de perfil inválido")
            continue
        if not isinstance(limits, dict):
            errors.append(f"performance-baseline.profiles.{name} deve ser objeto")
            continue
        for field in ("parametersGenerateMillisecondsMax", "runnerMillisecondsMax"):
            value = limits.get(field)
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                errors.append(f"performance-baseline.profiles.{name}.{field} deve ser inteiro > 0")
        for field in ("minimumGoldPeople", "minimumPendingWithoutCpf"):
            if field in limits:
                value = limits[field]
                if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                    errors.append(f"performance-baseline.profiles.{name}.{field} deve ser inteiro > 0")
    if status == "APROVADO":
        if not profiles:
            errors.append("performance-baseline APROVADO exige ao menos um perfil")
        if not isinstance(data.get("environmentProfile"), str) or not data.get("environmentProfile", "").strip():
            errors.append("performance-baseline.environmentProfile ausente")
        if not valid_iso_utc(data.get("approvedAtUtc")):
            errors.append("performance-baseline.approvedAtUtc inválido")
        if not isinstance(data.get("approvedBy"), str) or not data.get("approvedBy", "").strip():
            errors.append("performance-baseline.approvedBy ausente")
        validate_evidence(data.get("evidence"), "performance-baseline", errors)
        validate_approval_context(data, "performance-baseline", errors)
    if require_approved and status != "APROVADO":
        errors.append("baseline de desempenho ainda não está APROVADO")
    return errors


def validate_linkage_policy(data: dict, require_approved: bool) -> list[str]:
    errors: list[str] = []
    if data.get("schemaVersion") != 1:
        errors.append("linkage-policy.schemaVersion deve ser 1")
    status = data.get("status")
    if status not in TOP_STATUSES:
        errors.append(f"linkage-policy.status inválido: {status!r}")
    decision = data.get("decision")
    if decision not in LINKAGE_DECISIONS:
        errors.append(f"linkage-policy.decision inválida: {decision!r}")
    numeric_fields = (
        "minimumLabeledNoCpfPairs",
        "minimumCpfAnchoredIndependentPairs",
        "minimumV1Recall",
        "minimumV2CandidateRecall",
        "maximumCandidateExpansionRatio",
        "maximumNomeTotalVariation",
        "maximumNomeMaeTotalVariation",
        "maximumDataNascimentoExactAbsoluteDelta",
    )
    if status == "APROVADO":
        if decision == "SEM_DECISAO":
            errors.append("linkage-policy APROVADA não pode ter decision=SEM_DECISAO")
        for field in numeric_fields:
            value = data.get(field)
            if value is None or isinstance(value, bool) or not isinstance(value, (int, float)):
                errors.append(f"linkage-policy.{field} deve ser número quando APROVADA")
                continue
            if field.startswith("minimum") and value < 0:
                errors.append(f"linkage-policy.{field} deve ser >= 0")
            if field.startswith("maximum") and value < 0:
                errors.append(f"linkage-policy.{field} deve ser >= 0")
            if field in {"minimumV1Recall", "minimumV2CandidateRecall", "maximumNomeTotalVariation", "maximumNomeMaeTotalVariation", "maximumDataNascimentoExactAbsoluteDelta"} and value > 1:
                errors.append(f"linkage-policy.{field} deve estar entre 0 e 1")
        if not valid_iso_utc(data.get("approvedAtUtc")):
            errors.append("linkage-policy.approvedAtUtc inválido")
        if not isinstance(data.get("approvedBy"), str) or not data.get("approvedBy", "").strip():
            errors.append("linkage-policy.approvedBy ausente")
        validate_evidence(data.get("evidence"), "linkage-policy", errors)
        validate_approval_context(data, "linkage-policy", errors)
    if require_approved and status != "APROVADO":
        errors.append("política de avaliação de linkage ainda não está APROVADA")
    return errors


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida contratos versionados de homologação HML sem fabricar aprovações.")
    ap.add_argument("--root", default=".", help="raiz da Solution")
    ap.add_argument("--require-approved", action="store_true", help="falha enquanto parâmetros/baseline/política não estiverem aprovados")
    ap.add_argument("--summary")
    args = ap.parse_args()
    root = Path(args.root).resolve()
    paths = {
        "parameters": root / "config/hml/parameters.json",
        "performanceBaseline": root / "config/hml/performance-baseline.json",
        "linkagePolicy": root / "config/hml/linkage-evaluation-policy.json",
    }
    errors: list[str] = []
    try:
        parameters = load_json(paths["parameters"])
        performance = load_json(paths["performanceBaseline"])
        linkage = load_json(paths["linkagePolicy"])
        errors += validate_parameters(parameters, args.require_approved)
        errors += validate_performance_baseline(performance, args.require_approved)
        errors += validate_linkage_policy(linkage, args.require_approved)
    except (ValueError, json.JSONDecodeError, OSError) as exc:
        errors.append(str(exc))
        parameters = performance = linkage = {}

    summary = {
        "schemaVersion": 1,
        "status": "FAIL" if errors else "PASS",
        "requireApproved": args.require_approved,
        "errors": errors,
        "contracts": {k: str(v) for k, v in paths.items()},
        "approvalState": {
            "parameters": parameters.get("status"),
            "performanceBaseline": performance.get("status"),
            "linkagePolicy": linkage.get("status"),
        },
    }
    if args.summary:
        out = Path(args.summary)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if errors:
        for error in errors:
            print(f"ERRO: {error}", file=sys.stderr)
        return 2
    print(
        "HML CONFIG GATE: OK "
        f"(parameters={parameters.get('status')}; performance={performance.get('status')}; linkage={linkage.get('status')}; strict={args.require_approved})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
