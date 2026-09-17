#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import json
import math
import os
from pathlib import Path

DEFAULT_MAX_FRACTION = 0.25
DEFAULT_P95_ABSOLUTE = 1000
DEFAULT_TARGET_POPULATION_FLOOR = 1_000_000
DEFAULT_PROJECTION_Z = 1.959963984540054
TRANSPORTABILITY_VERSION = "BLOCKING_TRANSPORTABILITY_V1"
TRANSPORTABILITY_METHOD = "WILSON_UPPER_95_FROM_EMPIRICAL_COMPOSITE_KEY_SHARE"


def _number(value: object, label: str, errors: list[str]) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"{label} deve ser numérico")
        return None
    return float(value)


def _positive_int(value: object, label: str, errors: list[str]) -> int | None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        errors.append(f"{label} deve ser inteiro > 0")
        return None
    return value


def _wilson_upper(successes: float, trials: int, z: float = DEFAULT_PROJECTION_Z) -> float:
    if trials <= 0:
        raise ValueError("trials deve ser > 0")
    if successes < 0 or successes > trials:
        raise ValueError("successes deve estar em [0,trials]")
    proportion = successes / trials
    z2 = z * z
    denominator = 1.0 + z2 / trials
    center = proportion + z2 / (2.0 * trials)
    adjustment = z * math.sqrt((proportion * (1.0 - proportion) + z2 / (4.0 * trials)) / trials)
    return min(1.0, (center + adjustment) / denominator)


def _project_p95(observed_p95: float, reference_population: int, target_population: int) -> dict:
    point = observed_p95 * target_population / reference_population
    conservative = math.ceil(_wilson_upper(observed_p95, reference_population) * target_population)
    return {
        "observedP95": observed_p95,
        "pointProjectedP95": round(point, 4),
        "projectedP95": conservative,
    }


def annotate_transportability(
    data: dict,
    reference_population: int,
    target_population: int,
    p95_absolute: int,
) -> dict:
    if reference_population <= 0:
        raise ValueError("reference_population deve ser > 0")
    if target_population < reference_population:
        raise ValueError("target_population não pode ser menor que reference_population")

    summary = data.get("summary")
    passes = data.get("passes")
    if not isinstance(summary, dict) or not isinstance(passes, list):
        raise ValueError("relatório de blocking inválido para projeção")

    union_p95 = summary.get("p95UnionCandidateCount")
    if isinstance(union_p95, bool) or not isinstance(union_p95, (int, float)):
        raise ValueError("summary.p95UnionCandidateCount inválido para projeção")

    projected_passes: list[dict] = []
    for index, item in enumerate(passes):
        if not isinstance(item, dict):
            raise ValueError(f"passes[{index}] inválido para projeção")
        p95 = item.get("p95CandidateCount")
        if isinstance(p95, bool) or not isinstance(p95, (int, float)):
            raise ValueError(f"passes[{index}].p95CandidateCount inválido para projeção")
        projected_passes.append(
            {
                "passId": item.get("passId"),
                "fields": item.get("fields") or [],
                **_project_p95(float(p95), reference_population, target_population),
            }
        )

    data["transportability"] = {
        "version": TRANSPORTABILITY_VERSION,
        "projectionMethod": TRANSPORTABILITY_METHOD,
        "confidenceZ": DEFAULT_PROJECTION_Z,
        "referencePopulationSize": reference_population,
        "targetPopulationSize": target_population,
        "p95AbsoluteLimit": p95_absolute,
        "union": _project_p95(float(union_p95), reference_population, target_population),
        "passes": projected_passes,
        "interpretation": {
            "pointProjectedP95": "Escala proporcional diagnóstica do p95 observado; não é usada isoladamente para promoção.",
            "projectedP95": "Limite conservador: upper bound de Wilson 95% da participação empírica da chave composta, aplicado à população-alvo.",
            "scope": "A projeção preserva a distribuição conjunta observada do passe; não assume independência entre atributos. A referência externa deve entrar no corpus que originou a auditoria.",
        },
    }
    return data


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


def _validate_projection_metric(
    projection: object,
    observed_p95: float,
    reference_population: int,
    target_population: int,
    p95_absolute: int,
    label: str,
    errors: list[str],
) -> None:
    if not isinstance(projection, dict):
        errors.append(f"transportability.{label} ausente ou inválido")
        return
    expected = _project_p95(observed_p95, reference_population, target_population)
    observed = _number(projection.get("observedP95"), f"transportability.{label}.observedP95", errors)
    point = _number(projection.get("pointProjectedP95"), f"transportability.{label}.pointProjectedP95", errors)
    projected = _number(projection.get("projectedP95"), f"transportability.{label}.projectedP95", errors)
    if observed is not None and abs(observed - expected["observedP95"]) > 1e-6:
        errors.append(f"transportability.{label}.observedP95 divergente da auditoria")
    if point is not None and abs(point - expected["pointProjectedP95"]) > 0.0001:
        errors.append(f"transportability.{label}.pointProjectedP95 divergente da projeção")
    if projected is not None:
        if projected != expected["projectedP95"]:
            errors.append(f"transportability.{label}.projectedP95 divergente da projeção conservadora")
        if projected > p95_absolute:
            errors.append(
                f"transportability.{label}.projectedP95={projected:g} excede teto absoluto p95={p95_absolute} "
                f"na população-alvo {target_population}"
            )


def _validate_transportability(
    data: dict,
    population: int,
    target_population: int | None,
    p95_absolute: int,
    errors: list[str],
) -> None:
    block = data.get("transportability")
    if block is None:
        if target_population is not None:
            errors.append("transportability ausente para projeção obrigatória")
        return
    if not isinstance(block, dict):
        errors.append("transportability inválido")
        return
    if block.get("version") != TRANSPORTABILITY_VERSION:
        errors.append(f"transportability.version deve ser {TRANSPORTABILITY_VERSION}")
    if block.get("projectionMethod") != TRANSPORTABILITY_METHOD:
        errors.append(f"transportability.projectionMethod deve ser {TRANSPORTABILITY_METHOD}")

    reference = _positive_int(block.get("referencePopulationSize"), "transportability.referencePopulationSize", errors)
    target = _positive_int(block.get("targetPopulationSize"), "transportability.targetPopulationSize", errors)
    if reference is None or target is None:
        return
    if reference != population:
        errors.append("transportability.referencePopulationSize deve coincidir com population")
    if target < reference:
        errors.append("transportability.targetPopulationSize não pode ser menor que a referência")
    if target_population is not None and target != target_population:
        errors.append("transportability.targetPopulationSize divergente do alvo exigido")

    summary = data.get("summary")
    passes = data.get("passes")
    if not isinstance(summary, dict) or not isinstance(passes, list):
        return
    union_p95 = _number(summary.get("p95UnionCandidateCount"), "summary.p95UnionCandidateCount", errors)
    if union_p95 is not None:
        _validate_projection_metric(
            block.get("union"), union_p95, reference, target, p95_absolute, "union", errors
        )

    projected_passes = block.get("passes")
    if not isinstance(projected_passes, list):
        errors.append("transportability.passes ausente ou inválido")
        return
    by_id = {
        item.get("passId"): item
        for item in projected_passes
        if isinstance(item, dict) and isinstance(item.get("passId"), str)
    }
    if len(by_id) != len(projected_passes):
        errors.append("transportability.passes deve possuir passId único e válido")
    for index, item in enumerate(passes):
        if not isinstance(item, dict):
            continue
        pass_id = item.get("passId")
        p95 = _number(item.get("p95CandidateCount"), f"passes[{index}].p95CandidateCount", errors)
        if not isinstance(pass_id, str) or p95 is None:
            continue
        projection = by_id.get(pass_id)
        _validate_projection_metric(
            projection, p95, reference, target, p95_absolute, f"pass[{pass_id}]", errors
        )
    if len(projected_passes) != len(passes):
        errors.append("transportability.passes deve coincidir com o número de passes auditados")


def validate(
    data: dict,
    population: int,
    max_fraction: float,
    p95_absolute: int,
    target_population: int | None = None,
) -> list[str]:
    errors: list[str] = []
    if population <= 0:
        return ["population deve ser > 0"]
    if not 0 < max_fraction < 1:
        return ["max-fraction deve estar entre 0 e 1"]
    if p95_absolute <= 0:
        return ["p95-absolute deve ser > 0"]
    if target_population is not None and target_population < population:
        return ["target-population não pode ser menor que population"]

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

    _validate_transportability(data, population, target_population, p95_absolute, errors)
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
    transportability = data.get("transportability")
    if isinstance(transportability, dict):
        target = transportability.get("targetPopulationSize")
        union = transportability.get("union") or {}
        print(
            "BLOCKING TRANSPORTABILITY union: "
            f"observedP95={union.get('observedP95')} pointProjectedP95={union.get('pointProjectedP95')} "
            f"projectedP95={union.get('projectedP95')} targetPopulation={target}"
        )
        for item in transportability.get("passes") or []:
            fields = "+".join(item.get("fields") or [])
            print(
                f"BLOCKING TRANSPORTABILITY pass={item.get('passId')} fields={fields}: "
                f"observedP95={item.get('observedP95')} pointProjectedP95={item.get('pointProjectedP95')} "
                f"projectedP95={item.get('projectedP95')} targetPopulation={target}"
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

    projected = annotate_transportability(copy.deepcopy(valid), 100, 1000, 1000)
    projection_errors = validate(projected, 100, 0.25, 1000, target_population=1000)
    if projection_errors:
        raise RuntimeError(f"self-test: projeção conservadora válida rejeitada: {projection_errors}")
    if projected["transportability"]["union"]["observedP95"] != 20:
        raise RuntimeError("self-test: observedP95 não foi preservado")
    if projected["transportability"]["union"]["projectedP95"] <= projected["transportability"]["union"]["pointProjectedP95"]:
        raise RuntimeError("self-test: projectedP95 conservador deveria superar a projeção pontual")

    oversized_projection = annotate_transportability(copy.deepcopy(valid), 100, 100_000, 1000)
    if not validate(oversized_projection, 100, 0.25, 1000, target_population=100_000):
        raise RuntimeError("self-test: projeção para população-alvo excessiva deveria falhar")

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


def _resolve_target_population(reference_population: int, cli_value: int | None) -> int:
    if cli_value is not None:
        return cli_value
    raw = os.environ.get("JORNADA_BLOCKING_TARGET_POPULATION")
    if raw:
        try:
            return int(raw)
        except ValueError as exc:
            raise ValueError("JORNADA_BLOCKING_TARGET_POPULATION deve ser inteiro") from exc
    return max(reference_population, DEFAULT_TARGET_POPULATION_FLOOR)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Valida fan-out real do ruleset por passe e projeta transportabilidade para população-alvo."
    )
    parser.add_argument("report", nargs="?", type=Path)
    parser.add_argument("--population", type=int)
    parser.add_argument("--target-population", type=int)
    parser.add_argument("--max-fraction", type=float, default=DEFAULT_MAX_FRACTION)
    parser.add_argument("--p95-absolute", type=int, default=DEFAULT_P95_ABSOLUTE)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if args.report is None or args.population is None:
        parser.error("report e --population são obrigatórios fora de --self-test")

    try:
        target_population = _resolve_target_population(args.population, args.target_population)
        if target_population < args.population:
            parser.error("--target-population não pode ser menor que --population")
    except ValueError as exc:
        parser.error(str(exc))

    with args.report.open(encoding="utf-8") as handle:
        data = json.load(handle)
    annotate_transportability(data, args.population, target_population, args.p95_absolute)
    args.report.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    errors = validate(
        data,
        args.population,
        args.max_fraction,
        args.p95_absolute,
        target_population=target_population,
    )
    if errors:
        print_metrics(data, args.population, args.max_fraction, args.p95_absolute)
        for error in errors:
            print(f"ERRO: {error}")
        return 1
    print_metrics(data, args.population, args.max_fraction, args.p95_absolute)
    print("BLOCKING PASS FAN-OUT GATE: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
