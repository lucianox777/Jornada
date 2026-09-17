#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

DEFAULT_MAX_FRACTION = 0.25
DEFAULT_P95_ABSOLUTE = 1000


def _number(value: object, label: str, errors: list[str]) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"{label} deve ser numérico")
        return None
    return float(value)


def _validate_metrics(
    metrics: dict,
    label: str,
    population: int,
    max_fraction: float,
    p95_absolute: int,
    errors: list[str],
) -> None:
    mean = _number(metrics.get("meanCandidateCount"), f"{label}.meanCandidateCount", errors)
    p95 = _number(metrics.get("p95CandidateCount"), f"{label}.p95CandidateCount", errors)
    maximum = _number(metrics.get("maxCandidateCount"), f"{label}.maxCandidateCount", errors)
    if mean is None or p95 is None or maximum is None:
        return
    if min(mean, p95, maximum) < 0:
        errors.append(f"{label}: fan-out não pode ser negativo")
        return
    if mean > p95 or p95 > maximum:
        errors.append(f"{label}: esperado mean <= p95 <= max")
    fraction_limit = population * max_fraction
    if mean > fraction_limit:
        errors.append(
            f"{label}.meanCandidateCount={mean:g} excede {max_fraction:.0%} da população Gold ({fraction_limit:g})"
        )
    if p95 > fraction_limit:
        errors.append(
            f"{label}.p95CandidateCount={p95:g} excede {max_fraction:.0%} da população Gold ({fraction_limit:g})"
        )
    if maximum > fraction_limit:
        errors.append(
            f"{label}.maxCandidateCount={maximum:g} excede {max_fraction:.0%} da população Gold ({fraction_limit:g})"
        )
    if p95 > p95_absolute:
        errors.append(
            f"{label}.p95CandidateCount={p95:g} excede teto absoluto p95={p95_absolute}"
        )


def validate(data: dict, population: int, max_fraction: float, p95_absolute: int) -> list[str]:
    errors: list[str] = []
    if population <= 0:
        return ["population deve ser > 0"]
    if not 0 < max_fraction < 1:
        return ["max-fraction deve estar entre 0 e 1"]
    if p95_absolute <= 0:
        return ["p95-absolute deve ser > 0"]

    summary = data.get("summary")
    if not isinstance(summary, dict):
        return ["summary ausente ou inválido"]

    union_metrics = {
        "meanCandidateCount": summary.get("meanUnionCandidateCount"),
        "p95CandidateCount": summary.get("p95UnionCandidateCount"),
        "maxCandidateCount": summary.get("maxUnionCandidateCount"),
    }
    _validate_metrics(union_metrics, "union", population, max_fraction, p95_absolute, errors)

    passes = data.get("passes")
    if not isinstance(passes, list) or not passes:
        errors.append("passes ausente ou vazio")
        return errors

    pass_count = summary.get("ruleSetPassCount")
    if isinstance(pass_count, bool) or not isinstance(pass_count, int) or pass_count != len(passes):
        errors.append("summary.ruleSetPassCount deve coincidir com o número de passes")

    seen: set[str] = set()
    for index, item in enumerate(passes):
        if not isinstance(item, dict):
            errors.append(f"passes[{index}] inválido")
            continue
        pass_id = item.get("passId")
        if not isinstance(pass_id, str) or not pass_id:
            errors.append(f"passes[{index}].passId ausente")
            label = f"passes[{index}]"
        else:
            label = f"pass[{pass_id}]"
            if pass_id in seen:
                errors.append(f"passId duplicado: {pass_id}")
            seen.add(pass_id)
        _validate_metrics(item, label, population, max_fraction, p95_absolute, errors)

    return errors


def print_metrics(data: dict, population: int, max_fraction: float, p95_absolute: int) -> None:
    summary = data["summary"]
    print(
        "BLOCKING FAN-OUT union: "
        f"mean={summary['meanUnionCandidateCount']} "
        f"p95={summary['p95UnionCandidateCount']} "
        f"max={summary['maxUnionCandidateCount']} "
        f"population={population} fractionLimit={population * max_fraction:g} p95Absolute={p95_absolute}"
    )
    for item in data["passes"]:
        fields = "+".join(item.get("fields") or [])
        print(
            f"BLOCKING FAN-OUT pass={item.get('passId')} fields={fields}: "
            f"mean={item.get('meanCandidateCount')} p95={item.get('p95CandidateCount')} "
            f"max={item.get('maxCandidateCount')} candidatePairs={item.get('candidatePairs')} "
            f"truthInside={item.get('truthInsidePass')} incrementalTruth={item.get('incrementalTruthRecovered')}"
        )


def self_test() -> int:
    valid = {
        "summary": {
            "ruleSetPassCount": 2,
            "meanUnionCandidateCount": 12.0,
            "p95UnionCandidateCount": 20,
            "maxUnionCandidateCount": 25,
        },
        "passes": [
            {
                "passId": "P001",
                "fields": ["name_first", "birth_year"],
                "meanCandidateCount": 8.0,
                "p95CandidateCount": 15,
                "maxCandidateCount": 20,
            },
            {
                "passId": "P002",
                "fields": ["mother_name_last"],
                "meanCandidateCount": 7.0,
                "p95CandidateCount": 12,
                "maxCandidateCount": 18,
            },
        ],
    }
    if validate(valid, 100, 0.25, 1000):
        raise RuntimeError("self-test: evidência válida rejeitada")

    bad = copy.deepcopy(valid)
    bad["passes"][0]["meanCandidateCount"] = 99.0
    bad["passes"][0]["p95CandidateCount"] = 99
    bad["passes"][0]["maxCandidateCount"] = 99
    if not validate(bad, 100, 0.25, 1000):
        raise RuntimeError("self-test: passe com 99% da população deveria falhar")

    bad = copy.deepcopy(valid)
    bad["summary"]["meanUnionCandidateCount"] = 24.0
    bad["summary"]["p95UnionCandidateCount"] = 26
    bad["summary"]["maxUnionCandidateCount"] = 26
    if not validate(bad, 100, 0.25, 1000):
        raise RuntimeError("self-test: união acima da fração máxima deveria falhar")

    bad = copy.deepcopy(valid)
    bad["summary"]["meanUnionCandidateCount"] = 900.0
    bad["summary"]["p95UnionCandidateCount"] = 1001
    bad["summary"]["maxUnionCandidateCount"] = 1200
    if not validate(bad, 10000, 0.25, 1000):
        raise RuntimeError("self-test: p95 acima do teto absoluto deveria falhar")

    print("BLOCKING PASS FAN-OUT GATE SELF-TEST: OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Valida fan-out real do ruleset de blocking por passe e na união.")
    parser.add_argument("report", nargs="?", type=Path)
    parser.add_argument("--population", type=int)
    parser.add_argument("--max-fraction", type=float, default=DEFAULT_MAX_FRACTION)
    parser.add_argument("--p95-absolute", type=int, default=DEFAULT_P95_ABSOLUTE)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if args.report is None or args.population is None:
        parser.error("report e --population são obrigatórios fora de --self-test")

    with args.report.open(encoding="utf-8") as handle:
        data = json.load(handle)
    errors = validate(data, args.population, args.max_fraction, args.p95_absolute)
    if errors:
        for error in errors:
            print(f"ERRO: {error}")
        return 1
    print_metrics(data, args.population, args.max_fraction, args.p95_absolute)
    print("BLOCKING PASS FAN-OUT GATE: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
