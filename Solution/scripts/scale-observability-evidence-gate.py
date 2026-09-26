#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import importlib.util
import json
from pathlib import Path
from types import ModuleType

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

MIN_COORDINATION_WAIT_FRACTION = 0.50


def _load_fanout_gate() -> ModuleType:
    path = Path(__file__).with_name("blocking-pass-fanout-gate.py")
    spec = importlib.util.spec_from_file_location("jornada_blocking_pass_fanout_gate", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"não foi possível carregar {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


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


def validate(data: dict) -> list[str]:
    errors: list[str] = []

    gold_people = _nonnegative_int(data.get("goldPeople"), "goldPeople", errors)
    pending = _nonnegative_int(data.get("pendingWithoutCpf"), "pendingWithoutCpf", errors)

    pressure = data.get("blockingPressure")
    pass_count = 0
    if not isinstance(pressure, dict):
        errors.append("blockingPressure ausente ou inválido")
    else:
        pass_count = _nonnegative_int(
            pressure.get("ruleSetPassCount"), "blockingPressure.ruleSetPassCount", errors
        )
        rows = _nonnegative_int(pressure.get("blockingRows"), "blockingPressure.blockingRows", errors)
        keys = _nonnegative_int(pressure.get("distinctKeys"), "blockingPressure.distinctKeys", errors)
        max_people = _nonnegative_int(
            pressure.get("maxPeoplePerKey"), "blockingPressure.maxPeoplePerKey", errors
        )

        runtime_scope = data.get("runtimeScope")
        blocking = runtime_scope.get("blocking") if isinstance(runtime_scope, dict) else None
        if isinstance(blocking, dict) and blocking.get("mode") == "RULESET" and pass_count <= 0:
            errors.append("ruleset ativo deve possuir ao menos um passe observado")
        if rows > 0 and keys <= 0:
            errors.append("blockingRows > 0 exige distinctKeys > 0")
        if keys > 0 and max_people <= 0:
            errors.append("distinctKeys > 0 exige maxPeoplePerKey > 0")
        if gold_people > 0 and max_people > gold_people:
            errors.append("blockingPressure.maxPeoplePerKey não pode exceder goldPeople")

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
                if gold_people > 0 and max_per_value > gold_people:
                    errors.append(
                        f"blockingPressure.attributes[{index}].maxPeoplePerValue não pode exceder goldPeople"
                    )
                by_name[attribute] = (distinct, max_per_value)

            for attribute in sorted(REQUIRED_DIVERSE_ATTRIBUTES):
                metrics = by_name.get(attribute)
                if metrics is None:
                    errors.append(f"atributo de diversidade ausente em blockingPressure: {attribute}")
                    continue
                distinct, _ = metrics
                if gold_people > 1 and distinct <= 1:
                    errors.append(f"{attribute} degenerado: distinctValues deve ser > 1")

    audit_sample = _nonnegative_int(
        data.get("blockingAuditSampleSize"), "blockingAuditSampleSize", errors
    )
    pass_pressure = data.get("blockingPassPressure")
    if not isinstance(pass_pressure, dict):
        errors.append("blockingPassPressure ausente ou inválido")
    else:
        summary = pass_pressure.get("summary")
        if not isinstance(summary, dict):
            errors.append("blockingPassPressure.summary ausente ou inválido")
        else:
            actual_sample = _nonnegative_int(
                summary.get("sampleSize"), "blockingPassPressure.summary.sampleSize", errors
            )
            actual_pass_count = _nonnegative_int(
                summary.get("ruleSetPassCount"),
                "blockingPassPressure.summary.ruleSetPassCount",
                errors,
            )
            if audit_sample != actual_sample:
                errors.append("blockingAuditSampleSize deve coincidir com blockingPassPressure.summary.sampleSize")
            if pass_count != actual_pass_count:
                errors.append(
                    "blockingPressure.ruleSetPassCount deve coincidir com blockingPassPressure.summary.ruleSetPassCount"
                )

        if gold_people > 0:
            fanout = _load_fanout_gate()
            for error in fanout.validate(
                pass_pressure,
                gold_people,
                fanout.DEFAULT_MAX_FRACTION,
                fanout.DEFAULT_P95_ABSOLUTE,
            ):
                errors.append(f"blockingPassPressure: {error}")

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

    # Orçamento predeclarado persistido no modelo. No SCALE, e' limite de
    # evidencia sintetica externa, NAO gate TEST de promocao nem FPR populacional.
    scale_fp_bp = _nonnegative_int(
        data.get("scaleFpBudgetBasisPoints"), "scaleFpBudgetBasisPoints", errors
    )
    if scale_fp_bp > 10000:
        errors.append("scaleFpBudgetBasisPoints deve ser <= 10000")
    if data.get("scaleFpBudgetSource") != "MODEL_TEST_BP_SYNTHETIC_SCALE_MONITOR":
        errors.append("scaleFpBudgetSource deve identificar orçamento congelado do modelo")

    quality = data.get("decisionQuality")
    if not isinstance(quality, dict):
        errors.append("decisionQuality ausente ou inválido")
    else:
        total = _nonnegative_int(quality.get("totalScale"), "decisionQuality.totalScale", errors)
        resolved = _nonnegative_int(quality.get("resolved"), "decisionQuality.resolved", errors)
        correct = _nonnegative_int(quality.get("resolvedCorrect"), "decisionQuality.resolvedCorrect", errors)
        false_positives = _nonnegative_int(
            quality.get("falsePositives"), "decisionQuality.falsePositives", errors
        )
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
        # Nao retornar ao gate absoluto de zero FP: verificar o limite discreto
        # correspondente ao orçamento do modelo, informando FP e recall observados.
        allowed_fp = (total * scale_fp_bp + 9999) // 10000 if scale_fp_bp else 0
        if false_positives > allowed_fp:
            errors.append(
                f"decisionQuality.falsePositives={false_positives} excede o "
                f"teto sintetico discreto={allowed_fp} para "
                f"n={total}, budget={scale_fp_bp} bp"
            )
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
        hold_ms = _nonnegative_int(
            coordination.get("holderDelayMilliseconds"),
            "coordinationProbe.holderDelayMilliseconds",
            errors,
        )
        if hold_ms <= 0:
            errors.append("coordinationProbe.holderDelayMilliseconds deve ser > 0")
        minimum_wait_ms = int(hold_ms * MIN_COORDINATION_WAIT_FRACTION)
        for key, expected_resource in RESOURCES.items():
            probe = coordination.get(key)
            if not isinstance(probe, dict):
                errors.append(f"coordinationProbe.{key} ausente ou inválido")
                continue
            if probe.get("resource") != expected_resource:
                errors.append(f"coordinationProbe.{key}.resource divergente")
            if probe.get("contentionConfirmed") is not True:
                errors.append(f"coordinationProbe.{key}.contentionConfirmed deve ser true")
            lock_result = probe.get("lockResult")
            if isinstance(lock_result, bool) or not isinstance(lock_result, int):
                errors.append(f"coordinationProbe.{key}.lockResult deve ser inteiro")
            elif lock_result != 1:
                errors.append(
                    f"coordinationProbe.{key}.lockResult={lock_result} não comprova espera por contenção; esperado 1"
                )
            wait_ms = _nonnegative_int(
                probe.get("waitMilliseconds"),
                f"coordinationProbe.{key}.waitMilliseconds",
                errors,
            )
            if hold_ms > 0 and wait_ms < minimum_wait_ms:
                errors.append(
                    f"coordinationProbe.{key}.waitMilliseconds={wait_ms} abaixo do mínimo de contenção "
                    f"{minimum_wait_ms} ms ({MIN_COORDINATION_WAIT_FRACTION:.0%} de {hold_ms} ms)"
                )

    return errors


def _valid_fixture() -> dict:
    return {
        "goldPeople": 100,
        "pendingWithoutCpf": 20,
        "scaleFpBudgetBasisPoints": 100,
        "scaleFpBudgetSource": "MODEL_TEST_BP_SYNTHETIC_SCALE_MONITOR",
        "runtimeScope": {"blocking": {"mode": "RULESET"}},
        "blockingPressure": {
            "ruleSetPassCount": 2,
            "blockingRows": 500,
            "distinctKeys": 50,
            "maxPeoplePerKey": 99,
            "attributes": [
                {"attribute": "name_first", "rows": 100, "distinctValues": 20, "maxPeoplePerValue": 80},
                {"attribute": "name_phonetic_ptbr", "rows": 100, "distinctValues": 15, "maxPeoplePerValue": 90},
                {"attribute": "mother_name_first", "rows": 100, "distinctValues": 18, "maxPeoplePerValue": 85},
                {"attribute": "mother_name_phonetic_ptbr", "rows": 100, "distinctValues": 14, "maxPeoplePerValue": 95},
                {"attribute": "birth_month", "rows": 100, "distinctValues": 12, "maxPeoplePerValue": 99},
            ],
        },
        "blockingAuditSampleSize": 20,
        "blockingPassPressure": {
            "summary": {
                "sampleSize": 20,
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
        },
        "runner": {"evaluated": 20, "resolved": 15, "unresolved": 4, "conflicts": 1},
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
                "contentionConfirmed": True,
                "lockResult": 1,
                "waitMilliseconds": 2500,
            },
            "corpus": {
                "resource": "Jornada.Pipeline.Corpus",
                "contentionConfirmed": True,
                "lockResult": 1,
                "waitMilliseconds": 2500,
            },
        },
    }


def self_test() -> int:
    valid = _valid_fixture()
    if validate(valid):
        raise RuntimeError("self-test: evidência válida foi rejeitada")

    # Pressão atômica alta permanece diagnóstico; escalabilidade é decidida pelo passe efetivo.
    atomic = copy.deepcopy(valid)
    atomic["blockingPressure"]["maxPeoplePerKey"] = 100
    atomic["blockingPressure"]["attributes"][4]["maxPeoplePerValue"] = 100
    if validate(atomic):
        raise RuntimeError("self-test: concentração atômica válida não deve substituir o gate por passe")

    bad = copy.deepcopy(valid)
    bad["blockingPassPressure"]["passes"][0]["meanCandidateCount"] = 99.0
    bad["blockingPassPressure"]["passes"][0]["p95CandidateCount"] = 99
    bad["blockingPassPressure"]["passes"][0]["maxCandidateCount"] = 99
    if not validate(bad):
        raise RuntimeError("self-test: fan-out real de 99% deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["blockingPassPressure"]["summary"]["sampleSize"] = 19
    if not validate(bad):
        raise RuntimeError("self-test: amostra de blocking não reconciliada deveria ser rejeitada")

    bad = copy.deepcopy(valid)
    bad["coordinationProbe"]["exclusiveRequest"]["lockResult"] = 0
    if not validate(bad):
        raise RuntimeError("self-test: aquisição imediata sem espera deveria ser rejeitada")

    bad = copy.deepcopy(valid)
    bad["coordinationProbe"]["corpus"]["contentionConfirmed"] = False
    if not validate(bad):
        raise RuntimeError("self-test: probe sem confirmação do holder deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["coordinationProbe"]["corpus"]["waitMilliseconds"] = 100
    if not validate(bad):
        raise RuntimeError("self-test: espera curta demais deveria ser rejeitada")

    bad = copy.deepcopy(valid)
    bad["blockingPressure"]["ruleSetPassCount"] = 0
    if not validate(bad):
        raise RuntimeError("self-test: ruleset sem passe deveria ser rejeitado")

    # 20 observacoes a 100 bp admitem 1 FP pelo teto discreto ceil(0,2).
    within_budget = copy.deepcopy(valid)
    within_budget["decisionQuality"]["resolvedCorrect"] = 14
    within_budget["decisionQuality"]["falsePositives"] = 1
    within_budget["decisionQuality"]["ppvPct"] = 93.3333
    within_budget["decisionQuality"]["sensitivityPct"] = 70.0
    if validate(within_budget):
        raise RuntimeError("self-test: 1 FP dentro do budget deveria ser aceito")
    above_budget = copy.deepcopy(within_budget)
    above_budget["decisionQuality"]["resolvedCorrect"] = 13
    above_budget["decisionQuality"]["falsePositives"] = 2
    above_budget["decisionQuality"]["ppvPct"] = 86.6667
    above_budget["decisionQuality"]["sensitivityPct"] = 65.0
    if not any("teto sintetico" in x for x in validate(above_budget)):
        raise RuntimeError("self-test: 2 FP excedendo o teto devem ser rejeitados")
    zero_budget = copy.deepcopy(within_budget)
    zero_budget["scaleFpBudgetBasisPoints"] = 0
    if not any("teto sintetico" in x for x in validate(zero_budget)):
        raise RuntimeError("self-test: 0 bp deve manter limite zero")

    bad = copy.deepcopy(valid)
    bad["decisionQuality"]["unresolvedTruthOutsideTop2"] = 0
    if not validate(bad):
        raise RuntimeError("self-test: ranking não reconciliado deveria ser rejeitado")

    bad = copy.deepcopy(valid)
    bad["decisionQuality"]["unresolvedTruthInTop2Tie"] = 3
    if not validate(bad):
        raise RuntimeError("self-test: verdade em empate não pode exceder empates observados")

    bad = copy.deepcopy(valid)
    bad["blockingPressure"]["attributes"][0]["distinctValues"] = 1
    if not validate(bad):
        raise RuntimeError("self-test: corpus sem diversidade nominal deveria ser rejeitado")

    print("SCALE OBSERVABILITY EVIDENCE GATE SELF-TEST: OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Valida fan-out real do ruleset, saúde da projeção, qualidade sintética e coordenação do harness de escala."
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
    q = data["decisionQuality"]
    budget = data["scaleFpBudgetBasisPoints"]
    print(
        "SCALE OBSERVABILITY EVIDENCE GATE: OK "
        f"(fp={q['falsePositives']}/{q['totalScale']}; budget={budget} bp; "
        f"resolvedCorrect={q['resolvedCorrect']}; "
        f"recall={q['sensitivityPct']}%; conflicts={q['conflicts']})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
