#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

RESOURCES = {
    "exclusiveRequest": "Jornada.Pipeline.ExclusiveRequest",
    "corpus": "Jornada.Pipeline.Corpus",
}

REQUIRED_DIVERSE_ATTRIBUTES = {
    "name_first",
    "name_phonetic_ptbr",
    "mother_name_first",
    "mother_name_phonetic_ptbr",
}

# O harness SCALE deve detectar chaves patológicas antes que elas cheguem a um
# blocking quadrático. Rejeitar apenas uma chave 100% universal deixa passar
# distribuições quase universais (por exemplo 8.000 de 10.000 Pessoas).
# 25% é deliberadamente folgado para atributos legítimos de baixa cardinalidade
# do corpus sintético e, ao mesmo tempo, forte o bastante para capturar colapso
# de normalização/fonética ou um gerador degenerado.
MAX_PEOPLE_PER_KEY_FRACTION = 0.25


def _nonnegative_int(value: object, label: str, errors: list[str]) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        errors.append(f"{label} deve ser inteiro >= 0")
        return 0
    return value


def _number(value: object, label: str, errors: list[str]) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"{label} deve ser numérico")
        return None
    return float(value)


def _exceeds_population_fraction(value: int, population: int) -> bool:
    return population > 0 and value / population > MAX_PEOPLE_PER_KEY_FRACTION


def validate(data: dict) -> list[str]:
    errors: list[str] = []

    gold_people = _nonnegative_int(data.get("goldPeople"), "goldPeople", errors)
    pending = _nonnegative_int(data.get("pendingWithoutCpf"), "pendingWithoutCpf", errors)

    pressure = data.get("blockingPressure")
    if not isinstance(pressure, dict):
        errors.append("blockingPressure ausente ou inválido")
    else:
        pass_count = _nonnegative_int(pressure.get("ruleSetPassCount"), "blockingPressure.ruleSetPassCount", errors)
        rows = _nonnegative_int(pressure.get("blockingRows"), "blockingPressure.blockingRows", errors)
        keys = _nonnegative_int(pressure.get("distinctKeys"), "blockingPressure.distinctKeys", errors)
        max_people = _nonnegative_int(pressure.get("maxPeoplePerKey"), "blockingPressure.maxPeoplePerKey", errors)

        runtime_scope = data.get("runtimeScope")
        blocking = runtime_scope.get("blocking") if isinstance(runtime_scope, dict) else None
        if isinstance(blocking, dict) and blocking.get("mode") == "RULESET" and pass_count <= 0:
            errors.append("ruleset ativo deve possuir ao menos um passe observado")
        if rows > 0 and keys <= 0:
            errors.append("blockingRows > 0 exige distinctKeys > 0")
        if keys > 0 and max_people <= 0:
            errors.append("distinctKeys > 0 exige maxPeoplePerKey > 0")
        if gold_people > 1 and max_people >= gold_people:
            errors.append(
                "blockingPressure.maxPeoplePerKey não pode abranger toda a população Gold SCALE"
            )
        elif _exceeds_population_fraction(max_people, gold_people):
            errors.append(
                "blockingPressure.maxPeoplePerKey excede "
                f"{MAX_PEOPLE_PER_KEY_FRACTION:.0%} da população Gold SCALE "
                f"({max_people}/{gold_people})"
            )

        attributes = pressure.get("attributes")
        if not isinstance(attributes, list) or not attributes:
            errors.append("blockingPressure.attributes deve conter ao menos um atributo")
        else:
            seen: set[str] = set()
            by_name: dict[str, tuple[int, int]] = {}
            for index, item in enumerate(attributes):
                if not isinstance(item, dict):
                    errors.append(f"blockingPressure.attributes[{index}] inválido")
                    continue
                attribute = item.get("attribute")
                if not isinstance(attribute, str) or not attribute.strip():
                    errors.append(f"blockingPressure.attributes[{index}].attribute ausente")
                    continue
                if attribute in seen:
                    errors.append(f"atributo duplicado em blockingPressure: {attribute}")
                seen.add(attribute)
                _nonnegative_int(item.get("rows"), f"blockingPressure.attributes[{index}].rows", errors)
                distinct = _nonnegative_int(
                    item.get("distinctValues"),
                    f"blockingPressure.attributes[{index}].distinctValues",
                    errors,
                )
                max_per_value = _nonnegative_int(
                    item.get("maxPeoplePerValue"),
                    f"blockingPressure.attributes[{index}].maxPeoplePerValue",
                    errors,
                )
                by_name[attribute] = (distinct, max_per_value)

            for attribute in sorted(REQUIRED_DIVERSE_ATTRIBUTES):
                metrics = by_name.get(attribute)
                if metrics is None:
                    errors.append(f"atributo de diversidade ausente em blockingPressure: {attribute}")
                    continue
                distinct, max_per_value = metrics
                if gold_people > 1 and distinct <= 1:
                    errors.append(f"{attribute} degenerado: distinctValues deve ser > 1")
                if gold_people > 1 and max_per_value >= gold_people:
                    errors.append(f"{attribute} degenerado: uma chave não pode conter toda a população")
                elif _exceeds_population_fraction(max_per_value, gold_people):
                    errors.append(
                        f"{attribute} concentrado demais: maxPeoplePerValue={max_per_value} "
                        f"excede {MAX_PEOPLE_PER_KEY_FRACTION:.0%} de goldPeople={gold_people}"
                    )

    runner = data.get("runner")
    if not isinstance(runner, dict):
        errors.append("runner ausente ou inválido")
        runner_evaluated = runner_resolved = runner_unresolved = runner_conflicts = 0
    else:
        runner_evaluated = _nonnegative_int(runner.get("evaluated"), "runner.evaluated", errors)
        runner_resolved = _nonnegative_int(runner.get("resolved"), "runner.resolved", errors)
        runner_unresolved = _nonnegative_int(runner.get("unresolved"), "runner.unresolved", errors)
        runner_conflicts = _nonnegative_int(runner.get("conflicts"), "runner.conflicts", errors)
        if runner_evaluated != runner_resolved + runner_unresolved + runner_conflicts:
            errors.append("runner: evaluated deve fechar resolved + unresolved + conflicts")

    quality = data.get("decisionQuality")
    if not isinstance(quality, dict):
        errors.append("decisionQuality ausente ou inválido")
    else:
        total = _nonnegative_int(quality.get("totalScale"), "decisionQuality.totalScale", errors)
        resolved = _nonnegative_int(quality.get("resolved"), "decisionQuality.resolved", errors)
        correct = _nonnegative_int(quality.get("resolvedCorrect"), "decisionQuality.resolvedCorrect", errors)
        false_positives = _nonnegative_int(quality.get("falsePositives"), "decisionQuality.falsePositives", errors)
        conflicts = _nonnegative_int(quality.get("conflicts"), "decisionQuality.conflicts", errors)
        conflicts_truth_top2 = _nonnegative_int(
            quality.get("conflictsTruthTop2"), "decisionQuality.conflictsTruthTop2", errors
        )
        unresolved = _nonnegative_int(quality.get("unresolved"), "decisionQuality.unresolved", errors)
        truth_first = _nonnegative_int(
            quality.get("unresolvedTruthFirstWithoutTie"),
            "decisionQuality.unresolvedTruthFirstWithoutTie",
            errors,
        )
        truth_tie = _nonnegative_int(
            quality.get("unresolvedTruthInTop2Tie"),
            "decisionQuality.unresolvedTruthInTop2Tie",
            errors,
        )
        truth_second = _nonnegative_int(
            quality.get("unresolvedTruthSecondWithoutTie"),
            "decisionQuality.unresolvedTruthSecondWithoutTie",
            errors,
        )
        truth_outside = _nonnegative_int(
            quality.get("unresolvedTruthOutsideTop2"),
            "decisionQuality.unresolvedTruthOutsideTop2",
            errors,
        )
        unresolved_ties = _nonnegative_int(
            quality.get("unresolvedTieTop2"), "decisionQuality.unresolvedTieTop2", errors
        )
        ppv = _number(quality.get("ppvPct"), "decisionQuality.ppvPct", errors)
        sensitivity = _number(quality.get("sensitivityPct"), "decisionQuality.sensitivityPct", errors)

        if total != pending:
            errors.append("decisionQuality.totalScale deve coincidir com pendingWithoutCpf")
        if resolved != correct + false_positives:
            errors.append("decisionQuality.resolved deve fechar resolvedCorrect + falsePositives")
        if false_positives != 0:
            errors.append("decisionQuality.falsePositives deve ser zero no ensaio sintético conservador")
        if runner_evaluated != total:
            errors.append("runner.evaluated deve coincidir com decisionQuality.totalScale")
        if runner_resolved != resolved:
            errors.append("runner.resolved deve coincidir com decisionQuality.resolved")
        if runner_conflicts != conflicts:
            errors.append("runner.conflicts deve coincidir com decisionQuality.conflicts")
        if runner_unresolved != unresolved:
            errors.append("runner.unresolved deve coincidir com decisionQuality.unresolved")
        if resolved + conflicts + unresolved != total:
            errors.append("decisionQuality deve fechar resolved + conflicts + unresolved")
        if correct + false_positives != resolved:
            errors.append("decisionQuality de resolvidos não fecha")
        if truth_first + truth_tie + truth_second + truth_outside != unresolved:
            errors.append(
                "decisionQuality de não resolvidos deve particionar exatamente firstWithoutTie + top2Tie + secondWithoutTie + outsideTop2"
            )
        if truth_tie > unresolved_ties:
            errors.append("verdade em empate top-2 não pode exceder o total de empates top-2")
        if unresolved_ties > unresolved:
            errors.append("empates top-2 não podem exceder os não resolvidos")
        if conflicts_truth_top2 > conflicts:
            errors.append("conflictsTruthTop2 não pode exceder conflicts")
        if ppv is not None and resolved > 0:
            expected_ppv = 100.0 * correct / resolved
            if abs(ppv - expected_ppv) > 0.01:
                errors.append("decisionQuality.ppvPct divergente da contabilidade")
        if sensitivity is not None and total > 0:
            expected_sensitivity = 100.0 * correct / total
            if abs(sensitivity - expected_sensitivity) > 0.01:
                errors.append("decisionQuality.sensitivityPct divergente da contabilidade")

    coordination = data.get("coordinationProbe")
    if not isinstance(coordination, dict):
        errors.append("coordinationProbe ausente ou inválido")
    else:
        hold_ms = _nonnegative_int(coordination.get("holderDelayMilliseconds"), "coordinationProbe.holderDelayMilliseconds", errors)
        if hold_ms <= 0:
            errors.append("coordinationProbe.holderDelayMilliseconds deve ser > 0")
        for key, expected_resource in RESOURCES.items():
            probe = coordination.get(key)
            if not isinstance(probe, dict):
                errors.append(f"coordinationProbe.{key} ausente ou inválido")
                continue
            if probe.get("resource") != expected_resource:
                errors.append(f"coordinationProbe.{key}.resource divergente")
            lock_result = probe.get("lockResult")
            if isinstance(lock_result, bool) or not isinstance(lock_result, int):
                errors.append(f"coordinationProbe.{key}.lockResult deve ser inteiro")
            elif lock_result < 0:
                errors.append(f"coordinationProbe.{key}.lockResult={lock_result} indica falha de aquisição")
            _nonnegative_int(probe.get("waitMilliseconds"), f"coordinationProbe.{key}.waitMilliseconds", errors)

    return errors


def self_test() -> int:
    valid = {
        "goldPeople": 100,
        "pendingWithoutCpf": 20,
        "runtimeScope": {"blocking": {"mode": "RULESET"}},
        "blockingPressure": {
            "ruleSetPassCount": 3,
            "blockingRows": 100,
            "distinctKeys": 25,
            "maxPeoplePerKey": 12,
            "attributes": [
                {"attribute": "name_first", "rows": 100, "distinctValues": 20, "maxPeoplePerValue": 8},
                {"attribute": "name_phonetic_ptbr", "rows": 100, "distinctValues": 15, "maxPeoplePerValue": 10},
                {"attribute": "mother_name_first", "rows": 100, "distinctValues": 18, "maxPeoplePerValue": 9},
                {"attribute": "mother_name_phonetic_ptbr", "rows": 100, "distinctValues": 14, "maxPeoplePerValue": 11},
                {"attribute": "birth_month", "rows": 100, "distinctValues": 12, "maxPeoplePerValue": 12},
            ],
        },
        "runner": {
            "evaluated": 20,
            "resolved": 15,
            "unresolved": 4,
            "conflicts": 1,
        },
        "decisionQuality": {
            "totalScale": 20,
            "resolved": 15,
            "resolvedCorrect": 15,
            "falsePositives": 0,
            "conflicts": 1,
            "conflictsTruthTop2": 1,
            "unresolved": 4,
            "unresolvedTruthFirstWithoutTie": 1,
            "unresolvedTruthInTop2Tie": 1,
            "unresolvedTruthSecondWithoutTie": 1,
            "unresolvedTruthOutsideTop2": 1,
            "unresolvedTieTop2": 2,
            "ppvPct": 100.0,
            "sensitivityPct": 75.0,
        },
        "coordinationProbe": {
            "holderDelayMilliseconds": 3000,
            "exclusiveRequest": {
                "resource": "Jornada.Pipeline.ExclusiveRequest",
                "lockResult": 1,
                "waitMilliseconds": 2500,
            },
            "corpus": {
                "resource": "Jornada.Pipeline.Corpus",
                "lockResult": 1,
                "waitMilliseconds": 2500,
            },
        },
    }
    if validate(valid):
        raise RuntimeError("self-test: evidência válida foi rejeitada")

    bad = copy.deepcopy(valid)
    bad["coordinationProbe"]["corpus"]["lockResult"] = -1
    if not validate(bad):
        raise RuntimeError("self-test: falha de lock deveria ser rejeitada")

    bad = copy.deepcopy(valid)
    bad["blockingPressure"]["ruleSetPassCount"] = 0
    if not validate(bad):
        raise RuntimeError("self-test: ruleset sem passe deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["decisionQuality"]["resolvedCorrect"] = 14
    bad["decisionQuality"]["falsePositives"] = 1
    bad["decisionQuality"]["ppvPct"] = 93.3333
    if not validate(bad):
        raise RuntimeError("self-test: falso positivo sintético deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["decisionQuality"]["unresolvedTruthOutsideTop2"] = 0
    if not validate(bad):
        raise RuntimeError("self-test: ranking não reconciliado deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["decisionQuality"]["unresolvedTruthInTop2Tie"] = 3
    if not validate(bad):
        raise RuntimeError("self-test: verdade em empate não pode exceder empates observados")

    bad = copy.deepcopy(valid)
    bad["blockingPressure"]["maxPeoplePerKey"] = 100
    bad["blockingPressure"]["attributes"][0]["distinctValues"] = 1
    bad["blockingPressure"]["attributes"][0]["maxPeoplePerValue"] = 100
    if not validate(bad):
        raise RuntimeError("self-test: blocking universal deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["blockingPressure"]["maxPeoplePerKey"] = 26
    if not validate(bad):
        raise RuntimeError("self-test: blocking quase universal deveria exceder o teto proporcional")

    bad = copy.deepcopy(valid)
    bad["blockingPressure"]["attributes"][1]["maxPeoplePerValue"] = 26
    if not validate(bad):
        raise RuntimeError("self-test: atributo de diversidade concentrado deveria ser rejeitado")

    print("SCALE OBSERVABILITY EVIDENCE GATE SELF-TEST: OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Valida pressão de blocking, qualidade sintética e coordenação do harness de escala."
    )
    parser.add_argument("report", nargs="?")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if not args.report:
        parser.error("report é obrigatório fora de --self-test")

    path = Path(args.report)
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise SystemExit("ERRO: relatório deve ser objeto JSON")
    errors = validate(data)
    if errors:
        for error in errors:
            print(f"ERRO: {error}")
        return 2
    print("SCALE OBSERVABILITY EVIDENCE GATE: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())