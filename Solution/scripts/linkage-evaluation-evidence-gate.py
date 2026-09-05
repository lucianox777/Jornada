#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

REQUIRED_SAFEGUARDS = {
    "read-only against Jornada operational tables",
    "does not create linkage_run",
    "does not write IDENTITY_MAP/vinculo_fonte",
    "does not update Gold",
    "V2 is experimental evidence only",
}


def number(obj: dict, key: str, errors: list[str], prefix: str, minimum: float = 0.0, maximum: float | None = None) -> float:
    value = obj.get(key)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"{prefix}.{key} deve ser número")
        return 0.0
    value = float(value)
    if not math.isfinite(value) or value < minimum or (maximum is not None and value > maximum):
        errors.append(f"{prefix}.{key} fora da faixa permitida")
    return value


def load(path: Path) -> dict:
    if not path.is_file():
        raise ValueError(f"arquivo ausente: {path}")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON raiz deve ser objeto: {path}")
    return value


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida evidência do Jornada.Linkage.Evaluation e, opcionalmente, critérios HML aprovados.")
    ap.add_argument("report")
    ap.add_argument("--policy")
    ap.add_argument("--require-policy-approved", action="store_true")
    ap.add_argument("--summary")
    args = ap.parse_args()
    errors: list[str] = []
    try:
        report = load(Path(args.report).resolve())
    except (ValueError, json.JSONDecodeError, OSError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        return 2

    if report.get("purpose") != "DEV_HML_ONLY_NO_PUBLICATION":
        errors.append("purpose deve ser DEV_HML_ONLY_NO_PUBLICATION")
    safeguards = set(report.get("safeguards") or [])
    missing_safeguards = sorted(REQUIRED_SAFEGUARDS - safeguards)
    if missing_safeguards:
        errors.append("salvaguardas ausentes: " + ", ".join(missing_safeguards))

    inp = report.get("input")
    if not isinstance(inp, dict):
        errors.append("input ausente")
        inp = {}
    labeled = number(inp, "labeledNoCpfPairs", errors, "input", 1)
    cpf_pairs = number(inp, "cpfAnchoredIndependentPairs", errors, "input", 1)
    number(inp, "birthWindowDays", errors, "input", 1)
    number(inp, "smoothingAlpha", errors, "input", 0.000001)

    blocking = report.get("blocking")
    if not isinstance(blocking, dict):
        errors.append("blocking ausente")
        blocking = {}
    block_metrics: dict[str, dict[str, float]] = {}
    for name in ("v1", "v2Candidate"):
        obj = blocking.get(name)
        if not isinstance(obj, dict):
            errors.append(f"blocking.{name} ausente")
            obj = {}
        metrics = {
            "sampleSize": number(obj, "sampleSize", errors, f"blocking.{name}", 1),
            "trueUuidInsideBlock": number(obj, "trueUuidInsideBlock", errors, f"blocking.{name}", 0),
            "recall": number(obj, "recall", errors, f"blocking.{name}", 0, 1),
            "meanCandidates": number(obj, "meanCandidates", errors, f"blocking.{name}", 0),
            "medianCandidates": number(obj, "medianCandidates", errors, f"blocking.{name}", 0),
            "p95Candidates": number(obj, "p95Candidates", errors, f"blocking.{name}", 0),
            "maxCandidates": number(obj, "maxCandidates", errors, f"blocking.{name}", 0),
        }
        if metrics["trueUuidInsideBlock"] > metrics["sampleSize"]:
            errors.append(f"blocking.{name}.trueUuidInsideBlock excede sampleSize")
        block_metrics[name] = metrics
    if block_metrics["v1"]["sampleSize"] != labeled or block_metrics["v2Candidate"]["sampleSize"] != labeled:
        errors.append("sampleSize de blocking deve coincidir com labeledNoCpfPairs")
    delta_recall = number(blocking, "deltaRecall", errors, "blocking", -1, 1)
    expected_delta = block_metrics["v2Candidate"]["recall"] - block_metrics["v1"]["recall"]
    if abs(delta_recall - expected_delta) > 1e-9:
        errors.append("blocking.deltaRecall não coincide com recall(V2)-recall(V1)")

    transport = report.get("mTransportability")
    if not isinstance(transport, dict):
        errors.append("mTransportability ausente")
        transport = {}
    cpf = transport.get("cpfAnchored") if isinstance(transport.get("cpfAnchored"), dict) else {}
    nocpf = transport.get("noCpfLabeled") if isinstance(transport.get("noCpfLabeled"), dict) else {}
    distance = transport.get("distance") if isinstance(transport.get("distance"), dict) else {}
    cpf_sample = number(cpf, "sampleSize", errors, "mTransportability.cpfAnchored", 1)
    nocpf_sample = number(nocpf, "sampleSize", errors, "mTransportability.noCpfLabeled", 1)
    if cpf_sample != cpf_pairs:
        errors.append("mTransportability.cpfAnchored.sampleSize difere de input.cpfAnchoredIndependentPairs")
    if nocpf_sample != labeled:
        errors.append("mTransportability.noCpfLabeled.sampleSize difere de input.labeledNoCpfPairs")
    tv_name = number(distance, "nomeTotalVariation", errors, "mTransportability.distance", 0, 1)
    tv_mother = number(distance, "nomeMaeTotalVariation", errors, "mTransportability.distance", 0, 1)
    birth_delta = number(distance, "dataNascimentoExactAbsoluteDelta", errors, "mTransportability.distance", 0, 1)

    v1_mean = block_metrics["v1"]["meanCandidates"]
    v2_mean = block_metrics["v2Candidate"]["meanCandidates"]
    expansion_ratio = (v2_mean / v1_mean) if v1_mean > 0 else (1.0 if v2_mean == 0 else math.inf)

    policy_status = None
    decision = None
    if args.policy:
        try:
            policy = load(Path(args.policy).resolve())
            policy_status = policy.get("status")
            decision = policy.get("decision")
            if policy.get("schemaVersion") != 1:
                errors.append("policy.schemaVersion deve ser 1")
            if policy_status not in {"PENDENTE", "APROVADO"}:
                errors.append(f"policy.status inválido: {policy_status!r}")
            if args.require_policy_approved and policy_status != "APROVADO":
                errors.append("política HML ainda não está APROVADA")
            if policy_status == "APROVADO":
                checks = [
                    (labeled >= float(policy["minimumLabeledNoCpfPairs"]), "amostra SEM_CPF rotulada abaixo do mínimo aprovado"),
                    (cpf_pairs >= float(policy["minimumCpfAnchoredIndependentPairs"]), "amostra CPF-ancorada abaixo do mínimo aprovado"),
                    (block_metrics["v1"]["recall"] >= float(policy["minimumV1Recall"]), "recall V1 abaixo do mínimo aprovado"),
                    (block_metrics["v2Candidate"]["recall"] >= float(policy["minimumV2CandidateRecall"]), "recall V2 candidato abaixo do mínimo aprovado"),
                    (expansion_ratio <= float(policy["maximumCandidateExpansionRatio"]), "expansão média de candidatos acima do máximo aprovado"),
                    (tv_name <= float(policy["maximumNomeTotalVariation"]), "distância TV de nome acima do máximo aprovado"),
                    (tv_mother <= float(policy["maximumNomeMaeTotalVariation"]), "distância TV de nome da mãe acima do máximo aprovado"),
                    (birth_delta <= float(policy["maximumDataNascimentoExactAbsoluteDelta"]), "delta de nascimento exato acima do máximo aprovado"),
                ]
                errors.extend(message for ok, message in checks if not ok)
                if decision not in {"MANTER_V1", "SUBMETER_V2_PARA_REVISAO_NORMATIVA"}:
                    errors.append("policy APROVADA possui decisão inválida/ausente")
        except (ValueError, KeyError, TypeError, json.JSONDecodeError, OSError) as exc:
            errors.append(f"política inválida: {exc}")
    elif args.require_policy_approved:
        errors.append("--require-policy-approved exige --policy")

    summary = {
        "schemaVersion": 1,
        "status": "FAIL" if errors else "PASS",
        "source": str(Path(args.report).resolve()),
        "policyStatus": policy_status,
        "decision": decision,
        "errors": errors,
        "metrics": {
            "labeledNoCpfPairs": labeled,
            "cpfAnchoredIndependentPairs": cpf_pairs,
            "v1Recall": block_metrics["v1"]["recall"],
            "v2CandidateRecall": block_metrics["v2Candidate"]["recall"],
            "candidateExpansionRatio": expansion_ratio if math.isfinite(expansion_ratio) else None,
            "nomeTotalVariation": tv_name,
            "nomeMaeTotalVariation": tv_mother,
            "dataNascimentoExactAbsoluteDelta": birth_delta,
        },
        "note": "O gate qualifica evidência contra política aprovada; nunca promove V2 ou altera parâmetros automaticamente.",
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
        "LINKAGE EVALUATION EVIDENCE GATE: OK "
        f"(labels={int(labeled)}; v1Recall={block_metrics['v1']['recall']:.6f}; v2Recall={block_metrics['v2Candidate']['recall']:.6f}; policy={policy_status or 'NONE'})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
