#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

DEFAULT_MAX_POPULATION_FRACTION = 0.25
DEFAULT_MAX_P95_CANDIDATES = 2500


def _load(path: Path) -> dict:
    if not path.is_file():
        raise ValueError(f"arquivo ausente: {path}")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON raiz deve ser objeto: {path}")
    return value


def _nonnegative_number(value: object, label: str, errors: list[str]) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"{label} deve ser numérico")
        return 0.0
    result = float(value)
    if not math.isfinite(result) or result < 0:
        errors.append(f"{label} deve ser finito e >= 0")
        return 0.0
    return result


def validate_blocking_pass_fanout(
    audit: dict,
    gold_population: int,
    *,
    max_population_fraction: float = DEFAULT_MAX_POPULATION_FRACTION,
    max_p95_candidates: int = DEFAULT_MAX_P95_CANDIDATES,
    emit_metrics: bool = False,
) -> tuple[list[str], dict]:
    errors: list[str] = []
    if gold_population <= 0:
        errors.append("gold_population deve ser > 0")
    if not 0 < max_population_fraction < 1:
        errors.append("max_population_fraction deve estar entre 0 e 1")
    if max_p95_candidates <= 0:
        errors.append("max_p95_candidates deve ser > 0")

    if audit.get("purpose") != "DEV_HML_ONLY_READ_ONLY_BLOCKING_PASS_EVIDENCE":
        errors.append("purpose da auditoria de blocking por passe é inválido")

    summary = audit.get("summary")
    if not isinstance(summary, dict):
        errors.append("summary da auditoria de blocking por passe ausente")
        summary = {}
    passes = audit.get("passes")
    if not isinstance(passes, list) or not passes:
        errors.append("passes da auditoria de blocking por passe ausentes")
        passes = []

    fraction_limit = max(1, math.floor(gold_population * max_population_fraction)) if gold_population > 0 else 0
    p95_limit = min(fraction_limit, max_p95_candidates) if fraction_limit > 0 else max_p95_candidates

    union_mean = _nonnegative_number(summary.get("meanUnionCandidateCount"), "summary.meanUnionCandidateCount", errors)
    union_p95 = _nonnegative_number(summary.get("p95UnionCandidateCount"), "summary.p95UnionCandidateCount", errors)
    union_max = _nonnegative_number(summary.get("maxUnionCandidateCount"), "summary.maxUnionCandidateCount", errors)

    if union_p95 > p95_limit:
        errors.append(
            f"p95 da união dos passes excede o limite: {union_p95:g} > {p95_limit} "
            f"(população={gold_population}; fração={max_population_fraction:.0%}; teto_absoluto={max_p95_candidates})"
        )
    if union_max > fraction_limit:
        errors.append(
            f"máximo da união dos passes excede {max_population_fraction:.0%} da população: "
            f"{union_max:g} > {fraction_limit}"
        )

    pass_metrics: list[dict] = []
    for index, item in enumerate(passes):
        if not isinstance(item, dict):
            errors.append(f"passes[{index}] deve ser objeto")
            continue
        pass_id = item.get("passId")
        if not isinstance(pass_id, str) or not pass_id:
            pass_id = f"passes[{index}]"
            errors.append(f"passes[{index}].passId ausente")
        fields = item.get("fields")
        if not isinstance(fields, list) or not fields:
            fields = []
            errors.append(f"{pass_id}.fields ausente")
        mean = _nonnegative_number(item.get("meanCandidateCount"), f"{pass_id}.meanCandidateCount", errors)
        p95 = _nonnegative_number(item.get("p95CandidateCount"), f"{pass_id}.p95CandidateCount", errors)
        maximum = _nonnegative_number(item.get("maxCandidateCount"), f"{pass_id}.maxCandidateCount", errors)
        if p95 > p95_limit:
            errors.append(f"passe {pass_id} excede p95 permitido: {p95:g} > {p95_limit}")
        if maximum > fraction_limit:
            errors.append(
                f"passe {pass_id} excede máximo de {max_population_fraction:.0%} da população: "
                f"{maximum:g} > {fraction_limit}"
            )
        metrics = {
            "passId": pass_id,
            "fields": fields,
            "meanCandidateCount": mean,
            "p95CandidateCount": p95,
            "maxCandidateCount": maximum,
            "passRecallPct": item.get("passRecallPct"),
            "truthInsidePass": item.get("truthInsidePass"),
            "incrementalTruthRecovered": item.get("incrementalTruthRecovered"),
            "candidatePairs": item.get("candidatePairs"),
        }
        pass_metrics.append(metrics)

    metrics = {
        "goldPopulation": gold_population,
        "maxPopulationFraction": max_population_fraction,
        "maxP95CandidatesAbsolute": max_p95_candidates,
        "fractionLimitCandidates": fraction_limit,
        "effectiveP95LimitCandidates": p95_limit,
        "union": {
            "meanCandidateCount": union_mean,
            "p95CandidateCount": union_p95,
            "maxCandidateCount": union_max,
            "truthInsideUnion": summary.get("truthInsideUnion"),
            "unionRecallPct": summary.get("unionRecallPct"),
            "candidatePairs": summary.get("unionCandidatePairs"),
            "overlapCandidatePairs": summary.get("overlapCandidatePairs"),
        },
        "passes": pass_metrics,
    }

    if emit_metrics:
        print(
            "BLOCKING PASS FAN-OUT union "
            f"mean={union_mean:g} p95={union_p95:g} max={union_max:g} "
            f"population={gold_population} p95_limit={p95_limit} max_limit={fraction_limit}"
        )
        for item in pass_metrics:
            fields_text = "+".join(str(field) for field in item["fields"])
            print(
                "BLOCKING PASS FAN-OUT pass "
                f"id={item['passId']} fields={fields_text} "
                f"mean={item['meanCandidateCount']:g} p95={item['p95CandidateCount']:g} "
                f"max={item['maxCandidateCount']:g} recall={item['passRecallPct']} "
                f"incremental_truth={item['incrementalTruthRecovered']}"
            )

    return errors, metrics


def self_test() -> None:
    valid = {
        "purpose": "DEV_HML_ONLY_READ_ONLY_BLOCKING_PASS_EVIDENCE",
        "summary": {
            "meanUnionCandidateCount": 12.5,
            "p95UnionCandidateCount": 20,
            "maxUnionCandidateCount": 40,
            "truthInsideUnion": 99,
            "unionRecallPct": 99.0,
            "unionCandidatePairs": 1250,
            "overlapCandidatePairs": 100,
        },
        "passes": [
            {
                "passId": "P001",
                "fields": ["birth_month", "birth_year"],
                "meanCandidateCount": 10.0,
                "p95CandidateCount": 18,
                "maxCandidateCount": 35,
                "passRecallPct": 95.0,
                "truthInsidePass": 95,
                "incrementalTruthRecovered": 95,
                "candidatePairs": 1000,
            }
        ],
    }
    errors, _ = validate_blocking_pass_fanout(valid, 1000)
    if errors:
        raise RuntimeError(f"self-test rejeitou evidência válida: {errors}")

    bad = json.loads(json.dumps(valid))
    bad["passes"][0]["maxCandidateCount"] = 999
    errors, _ = validate_blocking_pass_fanout(bad, 1000)
    if not errors:
        raise RuntimeError("self-test deveria rejeitar passe com máximo quase igual à população")

    bad = json.loads(json.dumps(valid))
    bad["summary"]["p95UnionCandidateCount"] = 3000
    errors, _ = validate_blocking_pass_fanout(bad, 1000000)
    if not errors:
        raise RuntimeError("self-test deveria rejeitar p95 acima do teto absoluto")


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida fan-out real por passe de blocking contra limites relativos e absolutos.")
    ap.add_argument("audit", nargs="?")
    ap.add_argument("--gold-population", type=int)
    ap.add_argument("--max-population-fraction", type=float, default=DEFAULT_MAX_POPULATION_FRACTION)
    ap.add_argument("--max-p95-candidates", type=int, default=DEFAULT_MAX_P95_CANDIDATES)
    ap.add_argument("--summary")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    if args.self_test:
        self_test()
        print("BLOCKING PASS FAN-OUT GATE SELF-TEST: OK")
        return 0
    if not args.audit or args.gold_population is None:
        ap.error("audit e --gold-population são obrigatórios fora de --self-test")

    try:
        audit_path = Path(args.audit).resolve()
        audit = _load(audit_path)
        errors, metrics = validate_blocking_pass_fanout(
            audit,
            args.gold_population,
            max_population_fraction=args.max_population_fraction,
            max_p95_candidates=args.max_p95_candidates,
            emit_metrics=True,
        )
    except (ValueError, json.JSONDecodeError, OSError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        return 2

    result = {
        "schemaVersion": 1,
        "status": "FAIL" if errors else "PASS",
        "source": str(audit_path),
        "errors": errors,
        "metrics": metrics,
    }
    if args.summary:
        output = Path(args.summary)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if errors:
        for error in errors:
            print(f"ERRO: {error}", file=sys.stderr)
        return 2
    print("BLOCKING PASS FAN-OUT GATE: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
