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
TRANSPORTABILITY_VERSION = "BLOCKING_TRANSPORTABILITY_V1"
TRANSPORTABILITY_METHOD = "EMPIRICAL_JOINT_P95_SHARE_V1"


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


def _project_metric(observed: float, reference_population: int, target_population: int) -> float:
    return round(observed * target_population / reference_population, 4)


def _projection_row(
    observed_p95: float,
    observed_max: float,
    reference_population: int,
    target_population: int,
) -> dict:
    return {
        "observedP95": observed_p95,
        "observedMax": observed_max,
        "observedP95Share": round(observed_p95 / reference_population, 12),
        "projectedP95": _project_metric(observed_p95, reference_population, target_population),
        "projectedObservedMax": _project_metric(observed_max, reference_population, target_population),
    }


def annotate_transportability(
    data: dict,
    reference_population: int,
    target_population: int | None,
    p95_absolute: int,
) -> dict:
    if reference_population <= 0:
        raise ValueError("reference_population deve ser > 0")
    if target_population is not None and target_population < reference_population:
        raise ValueError("target_population não pode ser menor que reference_population")

    summary = data.get("summary")
    passes = data.get("passes")
    if not isinstance(summary, dict) or not isinstance(passes, list):
        raise ValueError("relatório de blocking inválido para projeção")

    block: dict = {
        "version": TRANSPORTABILITY_VERSION,
        "projectionMethod": TRANSPORTABILITY_METHOD,
        "status": "TARGET_NOT_DECLARED" if target_population is None else "PROJECTED",
        "referencePopulationSize": reference_population,
        "targetPopulationSize": target_population,
        "p95AbsoluteLimit": p95_absolute,
        "interpretation": {
            "observedP95": "p95 do fan-out efetivamente medido pelo Runner no corpus de referência.",
            "projectedP95": "Projeção da participação empírica conjunta do bloco p95 para uma população-alvo explicitamente declarada; não deriva marginais como se fossem independentes.",
            "projectedObservedMax": "Stress diagnóstico que mantém a maior concentração observada na referência; não é tratado como p95 nem como intervalo de confiança.",
            "scope": "Evidência de engenharia. Sem população-alvo declarada, não há alegação de transportabilidade para Produção/HML representativa.",
        },
    }

    if target_population is None:
        data["transportability"] = block
        return data

    union_p95 = summary.get("p95UnionCandidateCount")
    union_max = summary.get("maxUnionCandidateCount")
    if isinstance(union_p95, bool) or not isinstance(union_p95, (int, float)):
        raise ValueError("summary.p95UnionCandidateCount inválido para projeção")
    if isinstance(union_max, bool) or not isinstance(union_max, (int, float)):
        raise ValueError("summary.maxUnionCandidateCount inválido para projeção")

    block["union"] = _projection_row(
        float(union_p95), float(union_max), reference_population, target_population
    )
    projected_passes: list[dict] = []
    for index, item in enumerate(passes):
        if not isinstance(item, dict):
            raise ValueError(f"passes[{index}] inválido para projeção")
        p95 = item.get("p95CandidateCount")
        maximum = item.get("maxCandidateCount")
        if isinstance(p95, bool) or not isinstance(p95, (int, float)):
            raise ValueError(f"passes[{index}].p95CandidateCount inválido para projeção")
        if isinstance(maximum, bool) or not isinstance(maximum, (int, float)):
            raise ValueError(f"passes[{index}].maxCandidateCount inválido para projeção")
        projected_passes.append(
            {
                "passId": item.get("passId"),
                "fields": item.get("fields") or [],
                **_projection_row(float(p95), float(maximum), reference_population, target_population),
            }
        )
    block["passes"] = projected_passes
    data["transportability"] = block
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
        errors.append(f"{label}.p95CandidateCount={p95:g} excede teto absoluto p95={p95_absolute}")


def _validate_projection_metric(
    projection: object,
    observed_p95: float,
    observed_max: float,
    reference_population: int,
    target_population: int,
    p95_absolute: int,
    label: str,
    errors: list[str],
) -> None:
    if not isinstance(projection, dict):
        errors.append(f"transportability.{label} ausente ou inválido")
        return
    expected = _projection_row(observed_p95, observed_max, reference_population, target_population)
    for key in ("observedP95", "observedMax", "observedP95Share", "projectedP95", "projectedObservedMax"):
        actual = _number(projection.get(key), f"transportability.{label}.{key}", errors)
        if actual is not None and abs(actual - float(expected[key])) > 1e-6:
            errors.append(f"transportability.{label}.{key} divergente da projeção empírica")
    projected = projection.get("projectedP95")
    if isinstance(projected, (int, float)) and not isinstance(projected, bool) and projected > p95_absolute:
        errors.append(
            f"transportability.{label}.projectedP95={projected:g} excede teto absoluto p95={p95_absolute} "
            f"na população-alvo {target_population}"
        )


def _validate_transportability(
    data: dict,
    population: int,
    target_population: int | None,
    p95_absolute: int,
    require_target_population: bool,
    errors: list[str],
) -> None:
    block = data.get("transportability")
    if not isinstance(block, dict):
        errors.append("transportability ausente ou inválido")
        return
    if block.get("version") != TRANSPORTABILITY_VERSION:
        errors.append(f"transportability.version deve ser {TRANSPORTABILITY_VERSION}")
    if block.get("projectionMethod") != TRANSPORTABILITY_METHOD:
        errors.append(f"transportability.projectionMethod deve ser {TRANSPORTABILITY_METHOD}")

    reference = _positive_int(block.get("referencePopulationSize"), "transportability.referencePopulationSize", errors)
    if reference is None:
        return
    if reference != population:
        errors.append("transportability.referencePopulationSize deve coincidir com population")

    declared_target = block.get("targetPopulationSize")
    if target_population is None:
        if declared_target is not None:
            errors.append("transportability.targetPopulationSize deve ser null quando o alvo não foi declarado")
        if block.get("status") != "TARGET_NOT_DECLARED":
            errors.append("transportability.status deve ser TARGET_NOT_DECLARED sem população-alvo")
        if require_target_population:
            errors.append("população-alvo é obrigatória para alegação de transportabilidade")
        return

    target = _positive_int(declared_target, "transportability.targetPopulationSize", errors)
    if target is None:
        return
    if target != target_population:
        errors.append("transportability.targetPopulationSize divergente do alvo exigido")
    if target < reference:
        errors.append("transportability.targetPopulationSize não pode ser menor que a referência")
    if block.get("status") != "PROJECTED":
        errors.append("transportability.status deve ser PROJECTED quando há população-alvo")

    summary = data.get("summary")
    passes = data.get("passes")
    if not isinstance(summary, dict) or not isinstance(passes, list):
        return
    union_p95 = _number(summary.get("p95UnionCandidateCount"), "summary.p95UnionCandidateCount", errors)
    union_max = _number(summary.get("maxUnionCandidateCount"), "summary.maxUnionCandidateCount", errors)
    if union_p95 is not None and union_max is not None:
        _validate_projection_metric(
            block.get("union"), union_p95, union_max, reference, target, p95_absolute, "union", errors
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
        maximum = _number(item.get("maxCandidateCount"), f"passes[{index}].maxCandidateCount", errors)
        if not isinstance(pass_id, str) or p95 is None or maximum is None:
            continue
        _validate_projection_metric(
            by_id.get(pass_id), p95, maximum, reference, target, p95_absolute, f"pass[{pass_id}]", errors
        )
    if len(projected_passes) != len(passes):
        errors.append("transportability.passes deve coincidir com o número de passes auditados")


def validate(
    data: dict,
    population: int,
    max_fraction: float,
    p95_absolute: int,
    target_population: int | None = None,
    require_target_population: bool = False,
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

    _validate_transportability(
        data,
        population,
        target_population,
        p95_absolute,
        require_target_population,
        errors,
    )
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
    if not isinstance(transportability, dict):
        return
    target = transportability.get("targetPopulationSize")
    if target is None:
        print(
            "BLOCKING TRANSPORTABILITY: targetPopulation=NOT_DECLARED; "
            f"referencePopulation={transportability.get('referencePopulationSize')} status=TARGET_NOT_DECLARED"
        )
        return
    union = transportability.get("union") or {}
    print(
        "BLOCKING TRANSPORTABILITY union: "
        f"observedP95={union.get('observedP95')} projectedP95={union.get('projectedP95')} "
        f"projectedObservedMax={union.get('projectedObservedMax')} targetPopulation={target}"
    )
    for item in transportability.get("passes") or []:
        fields = "+".join(item.get("fields") or [])
        print(
            f"BLOCKING TRANSPORTABILITY pass={item.get('passId')} fields={fields}: "
            f"observedP95={item.get('observedP95')} projectedP95={item.get('projectedP95')} "
            f"projectedObservedMax={item.get('projectedObservedMax')} targetPopulation={target}"
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

    observed_only = annotate_transportability(copy.deepcopy(valid), 100, None, 1000)
    if validate(observed_only, 100, 0.25, 1000):
        raise RuntimeError("self-test: evidência observada válida rejeitada sem alvo inventado")
    if not validate(observed_only, 100, 0.25, 1000, require_target_population=True):
        raise RuntimeError("self-test: gate de transportabilidade deveria exigir alvo explícito")

    projected = annotate_transportability(copy.deepcopy(valid), 100, 1000, 1000)
    errors = validate(projected, 100, 0.25, 1000, target_population=1000, require_target_population=True)
    if errors:
        raise RuntimeError(f"self-test: projeção empírica válida rejeitada: {errors}")
    if projected["transportability"]["union"]["projectedP95"] != 200.0:
        raise RuntimeError("self-test: projectedP95 deve preservar a participação empírica conjunta")

    oversized_projection = annotate_transportability(copy.deepcopy(valid), 100, 10000, 1000)
    if not validate(
        oversized_projection,
        100,
        0.25,
        1000,
        target_population=10000,
        require_target_population=True,
    ):
        raise RuntimeError("self-test: projectedP95 acima do teto deveria falhar")

    bad = copy.deepcopy(valid)
    bad["passes"][0]["meanCandidateCount"] = 99.0
    bad["passes"][0]["p95CandidateCount"] = 99
    bad["passes"][0]["maxCandidateCount"] = 99
    annotate_transportability(bad, 100, None, 1000)
    if not validate(bad, 100, 0.25, 1000):
        raise RuntimeError("self-test: passe com 99% da população deveria falhar")

    bad = copy.deepcopy(valid)
    bad["summary"]["meanUnionCandidateCount"] = 24.0
    bad["summary"]["p95UnionCandidateCount"] = 26
    bad["summary"]["maxUnionCandidateCount"] = 26
    annotate_transportability(bad, 100, None, 1000)
    if not validate(bad, 100, 0.25, 1000):
        raise RuntimeError("self-test: união acima da fração máxima deveria falhar")

    bad = copy.deepcopy(valid)
    bad["summary"]["meanUnionCandidateCount"] = 900.0
    bad["summary"]["p95UnionCandidateCount"] = 1001
    bad["summary"]["maxUnionCandidateCount"] = 1200
    annotate_transportability(bad, 10000, None, 1000)
    if not validate(bad, 10000, 0.25, 1000):
        raise RuntimeError("self-test: p95 observado acima do teto absoluto deveria falhar")

    print("BLOCKING PASS FAN-OUT GATE SELF-TEST: OK")
    return 0


def _resolve_target_population(cli_value: int | None) -> int | None:
    if cli_value is not None:
        return cli_value
    raw = os.environ.get("JORNADA_BLOCKING_TARGET_POPULATION")
    if not raw:
        return None
    try:
        return int(raw)
    except ValueError as exc:
        raise ValueError("JORNADA_BLOCKING_TARGET_POPULATION deve ser inteiro") from exc


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Valida fan-out real do ruleset por passe e, quando declarado, projeta para população-alvo."
    )
    parser.add_argument("report", nargs="?", type=Path)
    parser.add_argument("--population", type=int)
    parser.add_argument("--target-population", type=int)
    parser.add_argument("--require-target-population", action="store_true")
    parser.add_argument("--max-fraction", type=float, default=DEFAULT_MAX_FRACTION)
    parser.add_argument("--p95-absolute", type=int, default=DEFAULT_P95_ABSOLUTE)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if args.report is None or args.population is None:
        parser.error("report e --population são obrigatórios fora de --self-test")

    try:
        target_population = _resolve_target_population(args.target_population)
        if target_population is not None and target_population < args.population:
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
        require_target_population=args.require_target_population,
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
