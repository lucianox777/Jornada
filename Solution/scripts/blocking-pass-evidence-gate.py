#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_POLICY = ROOT / "config" / "hml" / "blocking-fanout-policy.json"


def _number(value: object, label: str, errors: list[str]) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"{label} deve ser numérico")
        return None
    return float(value)


def _nonnegative_int(value: object, label: str, errors: list[str]) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        errors.append(f"{label} deve ser inteiro >= 0")
        return 0
    return value


def load_policy(path: Path) -> dict:
    policy = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(policy, dict):
        raise SystemExit("ERRO: política de fan-out deve ser objeto JSON")
    version = policy.get("policyVersion")
    fraction = policy.get("maxPopulationFraction")
    absolute = policy.get("maxP95CandidateCount")
    if not isinstance(version, str) or not version.strip():
        raise SystemExit("ERRO: policyVersion ausente")
    if isinstance(fraction, bool) or not isinstance(fraction, (int, float)) or not 0 < float(fraction) <= 1:
        raise SystemExit("ERRO: maxPopulationFraction deve estar em (0,1]")
    if isinstance(absolute, bool) or not isinstance(absolute, int) or absolute <= 0:
        raise SystemExit("ERRO: maxP95CandidateCount deve ser inteiro > 0")
    return policy


def effective_limit(gold_population: int, policy: dict) -> tuple[int, int, int]:
    fraction_limit = max(1, math.floor(gold_population * float(policy["maxPopulationFraction"])))
    absolute_limit = int(policy["maxP95CandidateCount"])
    return min(fraction_limit, absolute_limit), fraction_limit, absolute_limit


def validate(
    audit: dict,
    gold_population: int,
    policy: dict,
    expected_sample_size: int | None = None,
    expected_model_version: int | None = None,
) -> tuple[list[str], dict]:
    errors: list[str] = []
    if gold_population <= 0:
        errors.append("gold_population deve ser > 0")
        return errors, {}

    if audit.get("purpose") != "DEV_HML_ONLY_READ_ONLY_BLOCKING_PASS_EVIDENCE":
        errors.append("purpose da auditoria de passes inválido")

    model = audit.get("model")
    if not isinstance(model, dict):
        errors.append("model ausente na auditoria de passes")
        model = {}
    if expected_model_version is not None:
        observed_model_version = model.get("modelVersion")
        if observed_model_version != expected_model_version:
            errors.append(
                f"modelVersion divergente: observado={observed_model_version}; esperado={expected_model_version}"
            )

    summary = audit.get("summary")
    if not isinstance(summary, dict):
        errors.append("summary ausente na auditoria de passes")
        summary = {}

    sample_size = _nonnegative_int(summary.get("sampleSize"), "summary.sampleSize", errors)
    if sample_size <= 0:
        errors.append("summary.sampleSize deve ser > 0")
    if expected_sample_size is not None and sample_size != expected_sample_size:
        errors.append(f"summary.sampleSize={sample_size} difere do esperado={expected_sample_size}")

    pass_count = _nonnegative_int(summary.get("ruleSetPassCount"), "summary.ruleSetPassCount", errors)
    passes = audit.get("passes")
    if not isinstance(passes, list):
        errors.append("passes deve ser lista")
        passes = []
    if pass_count <= 0 or len(passes) != pass_count:
        errors.append("ruleSetPassCount deve ser > 0 e coincidir com passes")

    limit, fraction_limit, absolute_limit = effective_limit(gold_population, policy)

    mean_union = _number(summary.get("meanUnionCandidateCount"), "summary.meanUnionCandidateCount", errors)
    p95_union = _nonnegative_int(summary.get("p95UnionCandidateCount"), "summary.p95UnionCandidateCount", errors)
    max_union = _nonnegative_int(summary.get("maxUnionCandidateCount"), "summary.maxUnionCandidateCount", errors)
    if mean_union is not None and mean_union > limit:
        errors.append(
            f"fan-out médio da união={mean_union:g} excede limite efetivo={limit}"
        )
    if p95_union > limit:
        errors.append(f"fan-out p95 da união={p95_union} excede limite efetivo={limit}")
    if gold_population > 1 and max_union >= gold_population:
        errors.append(
            f"fan-out máximo da união={max_union} alcança a população Gold={gold_population}"
        )

    seen: set[str] = set()
    pass_metrics: list[dict] = []
    for index, item in enumerate(passes):
        label = f"passes[{index}]"
        if not isinstance(item, dict):
            errors.append(f"{label} deve ser objeto")
            continue
        pass_id = item.get("passId")
        fields = item.get("fields")
        if not isinstance(pass_id, str) or not pass_id:
            errors.append(f"{label}.passId ausente")
            pass_id = f"#{index}"
        elif pass_id in seen:
            errors.append(f"passId duplicado: {pass_id}")
        else:
            seen.add(pass_id)
        if not isinstance(fields, list) or not fields or not all(isinstance(x, str) and x for x in fields):
            errors.append(f"{label}.fields inválido")
            fields = []
        pass_sample = _nonnegative_int(item.get("sampleSize"), f"{label}.sampleSize", errors)
        if pass_sample != sample_size:
            errors.append(f"{pass_id}.sampleSize={pass_sample} difere de summary.sampleSize={sample_size}")
        mean_value = _number(item.get("meanCandidateCount"), f"{label}.meanCandidateCount", errors)
        p95_value = _nonnegative_int(item.get("p95CandidateCount"), f"{label}.p95CandidateCount", errors)
        max_value = _nonnegative_int(item.get("maxCandidateCount"), f"{label}.maxCandidateCount", errors)
        if mean_value is not None and mean_value > limit:
            errors.append(f"passe {pass_id}: média={mean_value:g} excede limite efetivo={limit}")
        if p95_value > limit:
            errors.append(f"passe {pass_id}: p95={p95_value} excede limite efetivo={limit}")
        if gold_population > 1 and max_value >= gold_population:
            errors.append(
                f"passe {pass_id}: máximo={max_value} alcança a população Gold={gold_population}"
            )
        pass_metrics.append(
            {
                "passId": pass_id,
                "fields": fields,
                "meanCandidateCount": mean_value,
                "p95CandidateCount": p95_value,
                "maxCandidateCount": max_value,
            }
        )

    evidence = {
        "policyVersion": policy["policyVersion"],
        "goldPopulation": gold_population,
        "maxPopulationFraction": float(policy["maxPopulationFraction"]),
        "fractionLimit": fraction_limit,
        "absoluteP95Limit": absolute_limit,
        "effectiveCandidateLimit": limit,
        "sampleSize": sample_size,
        "modelVersion": model.get("modelVersion"),
        "union": {
            "meanCandidateCount": mean_union,
            "p95CandidateCount": p95_union,
            "maxCandidateCount": max_union,
        },
        "passes": pass_metrics,
    }
    return errors, evidence


def print_evidence(evidence: dict) -> None:
    print(
        "Blocking fan-out policy: "
        f"version={evidence['policyVersion']} population={evidence['goldPopulation']} "
        f"fraction_limit={evidence['fractionLimit']} absolute_p95_limit={evidence['absoluteP95Limit']} "
        f"effective_limit={evidence['effectiveCandidateLimit']}"
    )
    union = evidence["union"]
    print(
        "Blocking fan-out union: "
        f"mean={union['meanCandidateCount']:g} p95={union['p95CandidateCount']} max={union['maxCandidateCount']}"
    )
    for item in evidence["passes"]:
        fields = "+".join(item["fields"])
        print(
            f"Blocking fan-out pass {item['passId']} [{fields}]: "
            f"mean={item['meanCandidateCount']:g} p95={item['p95CandidateCount']} max={item['maxCandidateCount']}"
        )


def self_test() -> int:
    policy = {
        "policyVersion": "BLOCKING_FANOUT_POLICY_V1",
        "maxPopulationFraction": 0.25,
        "maxP95CandidateCount": 1000,
    }
    valid = {
        "purpose": "DEV_HML_ONLY_READ_ONLY_BLOCKING_PASS_EVIDENCE",
        "model": {"modelVersion": 4},
        "summary": {
            "sampleSize": 100,
            "ruleSetPassCount": 1,
            "meanUnionCandidateCount": 10.0,
            "p95UnionCandidateCount": 20,
            "maxUnionCandidateCount": 40,
        },
        "passes": [
            {
                "passId": "P001",
                "fields": ["birth_month", "birth_year"],
                "sampleSize": 100,
                "meanCandidateCount": 10.0,
                "p95CandidateCount": 20,
                "maxCandidateCount": 40,
            }
        ],
    }
    errors, _ = validate(valid, 100, policy, expected_sample_size=100, expected_model_version=4)
    if errors:
        raise RuntimeError(f"self-test: evidência válida rejeitada: {errors}")

    bad = copy.deepcopy(valid)
    bad["passes"][0]["p95CandidateCount"] = 26
    errors, _ = validate(bad, 100, policy)
    if not errors:
        raise RuntimeError("self-test: p95 acima de 25% deveria falhar")

    bad = copy.deepcopy(valid)
    bad["summary"]["meanUnionCandidateCount"] = 26.0
    errors, _ = validate(bad, 100, policy)
    if not errors:
        raise RuntimeError("self-test: média acima de 25% deveria falhar")

    bad = copy.deepcopy(valid)
    bad["summary"]["maxUnionCandidateCount"] = 100
    errors, _ = validate(bad, 100, policy)
    if not errors:
        raise RuntimeError("self-test: união universal deveria falhar")

    large = copy.deepcopy(valid)
    large["summary"]["p95UnionCandidateCount"] = 1001
    large["passes"][0]["p95CandidateCount"] = 1001
    errors, evidence = validate(large, 1_000_000, policy)
    if not errors or evidence["effectiveCandidateLimit"] != 1000:
        raise RuntimeError("self-test: teto absoluto deveria limitar populações grandes")

    print("BLOCKING PASS EVIDENCE GATE SELF-TEST: OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Valida fan-out do blocking real por passe usando teto proporcional e p95 absoluto."
    )
    parser.add_argument("audit", nargs="?")
    parser.add_argument("--gold-population", type=int)
    parser.add_argument("--policy", default=str(DEFAULT_POLICY))
    parser.add_argument("--expected-sample-size", type=int)
    parser.add_argument("--expected-model-version", type=int)
    parser.add_argument("--summary")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if not args.audit:
        parser.error("audit é obrigatório fora de --self-test")
    if args.gold_population is None or args.gold_population <= 0:
        parser.error("--gold-population deve ser > 0")

    audit_path = Path(args.audit)
    audit = json.loads(audit_path.read_text(encoding="utf-8"))
    if not isinstance(audit, dict):
        raise SystemExit("ERRO: auditoria deve ser objeto JSON")
    policy = load_policy(Path(args.policy))
    errors, evidence = validate(
        audit,
        args.gold_population,
        policy,
        expected_sample_size=args.expected_sample_size,
        expected_model_version=args.expected_model_version,
    )
    print_evidence(evidence)
    if args.summary:
        summary_path = Path(args.summary)
        summary_path.parent.mkdir(parents=True, exist_ok=True)
        summary_path.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if errors:
        for error in errors:
            print(f"ERRO: {error}")
        return 2
    print("BLOCKING PASS EVIDENCE GATE: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
