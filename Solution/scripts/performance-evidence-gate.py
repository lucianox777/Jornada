#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ALLOWED_STATUS = {"PUBLICADO", "CONCLUIDO_SEM_PUBLICACAO"}


def as_nonnegative_int(data: dict, key: str) -> int:
    value = data.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ValueError(f"{key} deve ser inteiro >= 0; recebido {value!r}")
    return value


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida evidência estrutural do harness de escala sem inventar thresholds de HML.")
    ap.add_argument("report", help="JSON gerado por local-scale.sh")
    ap.add_argument("--minimum-eligible", type=int, default=1)
    ap.add_argument("--baseline", help="JSON versionado de baseline HML; PENDENTE valida apenas estrutura")
    ap.add_argument("--require-baseline-approved", action="store_true", help="falha enquanto o baseline HML não estiver APROVADO")
    ap.add_argument("--summary", help="grava JSON de validação")
    args = ap.parse_args()

    path = Path(args.report).resolve()
    if not path.is_file():
        raise SystemExit(f"ERRO: relatório de escala ausente: {path}")
    data = json.loads(path.read_text(encoding="utf-8"))
    runner = data.get("runner")
    if not isinstance(runner, dict):
        raise SystemExit("ERRO: bloco runner ausente")

    errors = []
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
    except ValueError as exc:
        errors.append(str(exc))
        people = pending = param_ms = runner_ms = eligible = evaluated = resolved = unresolved = conflicts = no_candidate = 0

    status = runner.get("status")
    if status not in ALLOWED_STATUS:
        errors.append(f"status do linkage_run inesperado: {status!r}")
    if people <= 0 or pending <= 0:
        errors.append("corpus de escala deve conter pessoas Gold e pendentes sem CPF")
    if eligible < args.minimum_eligible:
        errors.append(f"elegíveis={eligible}; mínimo={args.minimum_eligible}")
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

    baseline_status = None
    baseline_profile = None
    if args.baseline:
        baseline_path = Path(args.baseline).resolve()
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
                    if args.require_baseline_approved and baseline_status != "APROVADO":
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
                                def positive_int_limit(name):
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
    elif args.require_baseline_approved:
        errors.append("--require-baseline-approved exige --baseline")

    summary = {
        "schemaVersion": 1,
        "source": str(path),
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
        "note": "Sem baseline APROVADO, o gate valida apenas coerência da evidência. Com baseline APROVADO, também aplica os limites versionados de HML.",
    }
    if args.summary:
        out = Path(args.summary)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    if errors:
        for error in errors:
            print(f"ERRO: {error}", file=sys.stderr)
        return 2
    print(
        "PERFORMANCE EVIDENCE GATE: OK "
        f"(eligible={eligible}; evaluated={evaluated}; parametersMs={param_ms}; runnerMs={runner_ms})"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (json.JSONDecodeError, OSError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(2)
