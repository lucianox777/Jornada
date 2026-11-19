#!/usr/bin/env python3
"""Sanitized, standalone campaign comparison; never reads private cohorts."""
import argparse
import json
from pathlib import Path


def read_optional(path):
    if path is None or not path.is_file() or path.is_symlink():
        return None
    if path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("REPORT_INPUT_TOO_LARGE")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError("REPORT_INPUT_NOT_OBJECT")
    return value


def build(args):
    receipt = read_optional(args.closeout)
    audit = read_optional(args.audit)
    validation = read_optional(args.validation)
    independent = read_optional(args.independent_test)
    stages = {k: getattr(args, k) for k in ("prepare", "train", "audit_result", "validation_result", "test_result")}
    winner = receipt.get("winnerSha256") if receipt else None
    finalist_count = receipt.get("validationFinalists") if receipt else None
    outcome = receipt.get("status", "CLOSEOUT_UNAVAILABLE") if receipt else "CLOSEOUT_UNAVAILABLE"
    artifacts = {
        "TRAIN diagnostics": f"v9-v3-tf4-physical-train-{args.sha}-{args.run_id}",
        "AUDIT evidence": f"v9-tf4-independent-audit-{args.sha}-{args.run_id}",
        "VALIDATION evidence": f"v9-tf4-independent-validation-{args.sha}-{args.run_id}",
        "TEST evidence": f"v9-tf4-independent-test-{args.sha}-{args.run_id}",
        "E2E receipt": f"v9-tf4-e2e-closeout-{args.run_id}-{args.attempt}",
    }
    result = {
        "schema": "V9_CAMPAIGN_COMPARATIVE_REPORT_V1",
        "runId": args.run_id, "runAttempt": args.attempt, "codeSha": args.sha,
        "syntheticPopulationRequested": args.population or "DEFAULT_NOT_EXPLICIT",
        "stages": stages, "scientificOutcome": outcome,
        "finalists": finalist_count, "scientificWinnerSha256": winner,
        "testAssessment": receipt.get("scientificTestAssessment") if receipt else None,
        "productionAuthorization": "OPERATOR_MASTER_ONLY_NOT_GRANTED",
        "receiptsPresent": {"closeout": receipt is not None, "audit": audit is not None,
                            "validation": validation is not None, "independentTest": independent is not None},
        "artifacts": artifacts,
        "limitations": [
            "Stage outcomes and receipt references are not a full per-hypothesis metric table.",
            "No missing metrics are inferred; consult immutable TRAIN/AUDIT/VALIDATION/TEST artifacts.",
            "TEST measures the frozen winner and never reselects or revokes it.",
            "Workflow success and scientific winner do not authorize production.",
        ],
    }
    return result


def markdown(report):
    lines = [
        "# V9 — Relatório comparativo da campanha",
        "",
        f"- Execução: {report['runId']} (tentativa {report['runAttempt']})",
        f"- SHA: \`{report['codeSha']}\`",
        f"- População solicitada: {report['syntheticPopulationRequested']}",
        f"- Encerramento científico: **{report['scientificOutcome']}**",
        f"- Finalistas: {report['finalists'] if report['finalists'] is not None else 'não informado'}",
        f"- Vencedor científico (SHA256): {report['scientificWinnerSha256'] or 'nenhum / não comprovado'}",
        "- Autorização de produção: **não concedida; exclusiva do operador master**",
        "",
        "## Comparativo por etapa",
        "| Etapa | Estado do job |",
        "| --- | --- |",
    ]
    for stage, state in report["stages"].items():
        lines.append(f"| {stage} | {state} |")
    lines += ["", "## Evidências e rastreabilidade", "Artefatos da mesma execução no GitHub Actions:"]
    for label, name in report["artifacts"].items():
        lines.append(f"- {label}: \`{name}\`")
    lines += ["", "## Disponibilidade de recibos"]
    for label, present in report["receiptsPresent"].items():
        lines.append(f"- {label}: {'presente' if present else 'indisponível'}")
    lines += ["", "## Limitações e governança"]
    lines.extend("- " + item for item in report["limitations"])
    return "\n".join(lines) + "\n"


def main():
    p = argparse.ArgumentParser()
    for name in ("sha", "run-id", "attempt", "prepare", "train", "audit-result",
                 "validation-result", "test-result"):
        p.add_argument("--" + name, required=True)
    p.add_argument("--population", default="")
    for name in ("closeout", "audit", "validation", "independent-test"):
        p.add_argument("--" + name, type=Path)
    p.add_argument("--out", type=Path, required=True)
    args = p.parse_args()
    report = build(args)
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "campaign-report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (args.out / "campaign-report.md").write_text(markdown(report), encoding="utf-8")
    print("V9_COMPARATIVE_REPORT_GENERATED")


if __name__ == "__main__":
    main()
