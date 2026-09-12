#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import json
import re
import sys
import uuid
from pathlib import Path

ALLOWED_STATUS = {"PUBLICADO", "CONCLUIDO_SEM_PUBLICACAO"}
SCALE_REPORT_VERSION = "LINKAGE_SCALE_EVIDENCE_V1"
GIT_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def as_nonnegative_int(data: dict, key: str) -> int:
    value = data.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ValueError(f"{key} deve ser inteiro >= 0; recebido {value!r}")
    return value


def as_positive_int(data: dict, key: str) -> int:
    value = as_nonnegative_int(data, key)
    if value <= 0:
        raise ValueError(f"{key} deve ser inteiro > 0; recebido {value!r}")
    return value


def nonblank(value: object) -> bool:
    return isinstance(value, str) and bool(value.strip())


def sha256(value: object) -> bool:
    return isinstance(value, str) and SHA256_RE.fullmatch(value.lower()) is not None


def validate_scale_provenance(data: dict, errors: list[str]) -> None:
    if data.get("reportVersion") != SCALE_REPORT_VERSION:
        errors.append(f"reportVersion deve ser {SCALE_REPORT_VERSION}")

    git_sha = data.get("gitCommitSha")
    if not isinstance(git_sha, str) or GIT_SHA_RE.fullmatch(git_sha.lower()) is None:
        errors.append("gitCommitSha deve ser SHA-1 Git de 40 caracteres hexadecimais")

    scope = data.get("runtimeScope")
    if not isinstance(scope, dict):
        errors.append("runtimeScope ausente ou inválido")
        return
    if scope.get("mode") != "MODEL_VALIDATION":
        errors.append("runtimeScope.mode deve ser MODEL_VALIDATION para evidência de escala")

    selected = scope.get("selectedModel")
    if not isinstance(selected, dict):
        errors.append("runtimeScope.selectedModel ausente ou inválido")
    else:
        try:
            uuid.UUID(str(selected.get("modelId")))
        except (ValueError, TypeError, AttributeError):
            errors.append("runtimeScope.selectedModel.modelId deve ser UUID válido")
        version = selected.get("version")
        if isinstance(version, bool) or not isinstance(version, int) or version <= 0:
            errors.append("runtimeScope.selectedModel.version deve ser inteiro > 0")
        elif version != data.get("modelVersion"):
            errors.append("runtimeScope.selectedModel.version diverge de modelVersion")
        if not nonblank(selected.get("algorithmVersion")):
            errors.append("runtimeScope.selectedModel.algorithmVersion ausente")

    blocking = scope.get("blocking")
    if not isinstance(blocking, dict):
        errors.append("runtimeScope.blocking ausente ou inválido")
        return
    blocking_mode = blocking.get("mode")
    if blocking_mode not in {"LEGACY", "RULESET"}:
        errors.append(f"runtimeScope.blocking.mode inválido: {blocking_mode!r}")
        return

    governed_fields = (
        "ruleSetVersion",
        "ruleSetFingerprintSha256",
        "projectionSchemaVersion",
        "projectionFingerprintSha256",
    )
    if blocking_mode == "LEGACY":
        if any(blocking.get(key) is not None for key in governed_fields):
            errors.append("blocking LEGACY não pode declarar proveniência RULESET/projeção")
        return

    if not nonblank(blocking.get("ruleSetVersion")):
        errors.append("runtimeScope.blocking.ruleSetVersion ausente em modo RULESET")
    if not sha256(blocking.get("ruleSetFingerprintSha256")):
        errors.append("runtimeScope.blocking.ruleSetFingerprintSha256 inválido")
    projection_version = blocking.get("projectionSchemaVersion")
    if isinstance(projection_version, bool) or not isinstance(projection_version, int) or projection_version <= 0:
        errors.append("runtimeScope.blocking.projectionSchemaVersion deve ser inteiro > 0")
    if not sha256(blocking.get("projectionFingerprintSha256")):
        errors.append("runtimeScope.blocking.projectionFingerprintSha256 inválido")


def validate_baseline(
    data: dict,
    baseline_path: Path | None,
    require_baseline_approved: bool,
    people: int,
    pending: int,
    param_ms: int,
    runner_ms: int,
    errors: list[str],
) -> tuple[str | None, str | None]:
    baseline_status = None
    baseline_profile = None
    if baseline_path is not None:
        if not baseline_path.is_file():
            errors.append(f"baseline ausente: {baseline_path}")
        else:
            try:
                baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
                if not isinstance(baseline, dict) or baseline.get("schemaVersion") != 1:
                    errors.append("baseline deve ser objeto com schemaVersion=1")
                else:
                    baseline_status = baseline.get("status")
                    if baseline_status not in {"PENDENTE", "APROVADO"}:
                        errors.append(f"baseline.status inválido: {baseline_status!r}")
                    if require_baseline_approved and baseline_status != "APROVADO":
                        errors.append("baseline HML ainda não está APROVADO")
                    if baseline_status == "APROVADO":
                        profile = data.get("profile")
                        profiles = baseline.get("profiles")
                        if not isinstance(profiles, dict) or profile not in profiles:
                            errors.append(f"baseline APROVADO não possui limites para profile={profile!r}")
                        else:
                            baseline_profile = profile
                            limits = profiles[profile]
                            if not isinstance(limits, dict):
                                errors.append(f"baseline.profiles.{profile} deve ser objeto")
                            else:
                                def positive_int_limit(name: str) -> int | None:
                                    value = limits.get(name)
                                    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                                        errors.append(f"baseline {profile}.{name} deve ser inteiro > 0")
                                        return None
                                    return value

                                pmax = positive_int_limit("parametersGenerateMillisecondsMax")
                                rmax = positive_int_limit("runnerMillisecondsMax")
                                if pmax is not None and param_ms > pmax:
                                    errors.append(f"parametersGenerateMilliseconds={param_ms} excede baseline {pmax}")
                                if rmax is not None and runner_ms > rmax:
                                    errors.append(f"runnerMilliseconds={runner_ms} excede baseline {rmax}")
                                for name, measured in (("minimumGoldPeople", people), ("minimumPendingWithoutCpf", pending)):
                                    if name in limits:
                                        value = limits[name]
                                        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                                            errors.append(f"baseline {profile}.{name} deve ser inteiro > 0")
                                        elif measured < value:
                                            errors.append(f"{name} medido={measured} abaixo do baseline mínimo={value}")
            except (json.JSONDecodeError, OSError) as exc:
                errors.append(f"baseline inválido: {exc}")
    elif require_baseline_approved:
        errors.append("--require-baseline-approved exige --baseline")
    return baseline_status, baseline_profile


def validate_report(
    data: dict,
    minimum_eligible: int,
    baseline_path: Path | None = None,
    require_baseline_approved: bool = False,
) -> dict:
    runner = data.get("runner")
    if not isinstance(runner, dict):
        return {
            "schemaVersion": 2,
            "reportVersion": data.get("reportVersion"),
            "status": "FAIL",
            "errors": ["bloco runner ausente"],
        }

    errors: list[str] = []
    validate_scale_provenance(data, errors)
    try:
        people = as_nonnegative_int(data, "goldPeople")
        pending = as_nonnegative_int(data, "pendingWithoutCpf")
        param_ms = as_nonnegative_int(data, "parametersGenerateMilliseconds")
        runner_ms = as_nonnegative_int(data, "runnerMilliseconds")
        eligible = as_nonnegative_int(runner, "eligible")
        evaluated = as_nonnegative_int(runner, "evaluated")
        resolved = as_nonnegative_int(runner, "resolved")
        unresolved = as_nonnegative_int(runner, "unresolved")
        conflicts = as_nonnegative_int(runner, "conflicts")
        no_candidate = as_nonnegative_int(runner, "noCandidateInBirthDateBlock")
        as_positive_int(data, "modelVersion")
    except ValueError as exc:
        errors.append(str(exc))
        people = pending = param_ms = runner_ms = eligible = evaluated = resolved = unresolved = conflicts = no_candidate = 0

    status = runner.get("status")
    if status not in ALLOWED_STATUS:
        errors.append(f"status do linkage_run inesperado: {status!r}")
    if people <= 0 or pending <= 0:
        errors.append("corpus de escala deve conter pessoas Gold e pendentes sem CPF")
    if eligible < minimum_eligible:
        errors.append(f"elegíveis={eligible}; mínimo={minimum_eligible}")
    if evaluated > eligible:
        errors.append(f"avaliados={evaluated} > elegíveis={eligible}")
    if resolved + unresolved + conflicts > evaluated:
        errors.append("resolvidos+não_resolvidos+conflitos excedem avaliados")
    if no_candidate > evaluated:
        errors.append("sem_candidato_no_bloco excede avaliados")
    if param_ms <= 0 or runner_ms <= 0:
        errors.append("durações precisam ser > 0 para constituir evidência")
    if not data.get("correlationId"):
        errors.append("correlationId ausente")

    baseline_status, baseline_profile = validate_baseline(
        data,
        baseline_path,
        require_baseline_approved,
        people,
        pending,
        param_ms,
        runner_ms,
        errors,
    )

    return {
        "schemaVersion": 2,
        "reportVersion": data.get("reportVersion"),
        "sourceGitCommitSha": data.get("gitCommitSha"),
        "runtimeScope": data.get("runtimeScope"),
        "status": "FAIL" if errors else "PASS",
        "errors": errors,
        "baselineStatus": baseline_status,
        "baselineProfile": baseline_profile,
        "metrics": {
            "goldPeople": people,
            "pendingWithoutCpf": pending,
            "parametersGenerateMilliseconds": param_ms,
            "runnerMilliseconds": runner_ms,
            "eligible": eligible,
            "evaluated": evaluated,
            "resolved": resolved,
            "unresolved": unresolved,
            "conflicts": conflicts,
            "noCandidateInBirthDateBlock": no_candidate,
        },
        "note": "O gate valida coerência e proveniência da evidência de escala. Sem baseline APROVADO, o gate valida apenas coerência da evidência. Com baseline APROVADO, também aplica os limites versionados de HML.",
    }


def self_test_report() -> dict:
    fake_sha = "a" * 64
    return {
        "reportVersion": SCALE_REPORT_VERSION,
        "gitCommitSha": "b" * 40,
        "profile": "smoke",
        "goldPeople": 10000,
        "pendingWithoutCpf": 5000,
        "modelVersion": 7,
        "parametersGenerateMilliseconds": 100,
        "runnerMilliseconds": 200,
        "runner": {
            "status": "CONCLUIDO_SEM_PUBLICACAO",
            "eligible": 5000,
            "evaluated": 5000,
            "resolved": 3000,
            "unresolved": 1500,
            "conflicts": 500,
            "noCandidateInBirthDateBlock": 250,
        },
        "runtimeScope": {
            "mode": "MODEL_VALIDATION",
            "selectedModel": {
                "modelId": "00000000-0000-0000-0000-000000000007",
                "version": 7,
                "algorithmVersion": "FS_V2",
            },
            "blocking": {
                "mode": "RULESET",
                "ruleSetVersion": "BLOCKING_V2",
                "ruleSetFingerprintSha256": fake_sha,
                "projectionSchemaVersion": 1,
                "projectionFingerprintSha256": fake_sha,
            },
        },
        "correlationId": "00000000-0000-0000-0000-000000000031",
    }


def run_self_test() -> int:
    valid = self_test_report()
    summary = validate_report(valid, 1)
    if summary["status"] != "PASS":
        raise RuntimeError(f"self-test esperava PASS: {summary['errors']}")

    missing_commit = copy.deepcopy(valid)
    missing_commit["gitCommitSha"] = None
    if validate_report(missing_commit, 1)["status"] != "FAIL":
        raise RuntimeError("self-test esperava rejeição de gitCommitSha ausente")

    mismatched_model = copy.deepcopy(valid)
    mismatched_model["runtimeScope"]["selectedModel"]["version"] = 8
    if validate_report(mismatched_model, 1)["status"] != "FAIL":
        raise RuntimeError("self-test esperava rejeição de modelo divergente")

    missing_ruleset_fingerprint = copy.deepcopy(valid)
    missing_ruleset_fingerprint["runtimeScope"]["blocking"]["ruleSetFingerprintSha256"] = None
    if validate_report(missing_ruleset_fingerprint, 1)["status"] != "FAIL":
        raise RuntimeError("self-test esperava rejeição de fingerprint de ruleset ausente")

    print("PERFORMANCE EVIDENCE GATE SELF-TEST: OK")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida evidência estrutural/proveniência do harness de escala sem inventar thresholds de HML.")
    ap.add_argument("report", nargs="?", help="JSON gerado por local-scale.sh/.ps1")
    ap.add_argument("--minimum-eligible", type=int, default=1)
    ap.add_argument("--baseline", help="JSON versionado de baseline HML; PENDENTE valida apenas estrutura/proveniência")
    ap.add_argument("--require-baseline-approved", action="store_true", help="falha enquanto o baseline HML não estiver APROVADO")
    ap.add_argument("--summary", help="grava JSON de validação")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    if args.self_test:
        return run_self_test()
    if not args.report:
        ap.error("report é obrigatório fora de --self-test")

    path = Path(args.report).resolve()
    if not path.is_file():
        raise SystemExit(f"ERRO: relatório de escala ausente: {path}")
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise SystemExit("ERRO: relatório de escala deve ser objeto JSON")

    baseline_path = Path(args.baseline).resolve() if args.baseline else None
    summary = validate_report(data, args.minimum_eligible, baseline_path, args.require_baseline_approved)
    if args.summary:
        out = Path(args.summary)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    if summary["status"] != "PASS":
        for error in summary["errors"]:
            print(f"ERRO: {error}", file=sys.stderr)
        return 2

    metrics = summary["metrics"]
    print(
        "PERFORMANCE EVIDENCE GATE: OK "
        f"(eligible={metrics['eligible']}; evaluated={metrics['evaluated']}; "
        f"parametersMs={metrics['parametersGenerateMilliseconds']}; runnerMs={metrics['runnerMilliseconds']})"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (json.JSONDecodeError, OSError, RuntimeError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(2)