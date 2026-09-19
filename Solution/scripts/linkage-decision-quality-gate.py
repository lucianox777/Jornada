#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path


def _int(d: dict, key: str, errors: list[str]) -> int:
    value = d.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        errors.append(f"decisionQuality.{key} deve ser inteiro >= 0")
        return 0
    return value


def _num(d: dict, key: str, errors: list[str], *, nullable: bool = False) -> float | None:
    value = d.get(key)
    if value is None and nullable:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        errors.append(f"decisionQuality.{key} deve ser numérico" + (" ou null" if nullable else ""))
        return None
    return float(value)


def validate(report: dict) -> list[str]:
    errors: list[str] = []
    quality = report.get("decisionQuality")
    if not isinstance(quality, dict):
        return ["decisionQuality ausente ou inválido"]

    total = _int(quality, "totalScale", errors)
    resolved = _int(quality, "resolved", errors)
    correct = _int(quality, "resolvedCorrect", errors)
    false_positives = _int(quality, "falsePositives", errors)
    conflicts = _int(quality, "conflicts", errors)
    top2 = _int(quality, "conflictsTruthTop2", errors)
    first = _int(quality, "conflictsTruthFirstWithoutTie", errors)
    tie = _int(quality, "conflictsTruthInTop2Tie", errors)
    second = _int(quality, "conflictsTruthSecondWithoutTie", errors)
    outside = _int(quality, "conflictsTruthOutsideTop2", errors)
    dual = _int(quality, "dualThresholdConflicts", errors)

    best_9999 = _int(quality, "conflictsBestPosteriorGe9999", errors)
    second_999 = _int(quality, "conflictsSecondPosteriorGe999", errors)
    second_9999 = _int(quality, "conflictsSecondPosteriorGe9999", errors)

    out_total = _int(quality, "birthOutOfUniverseEveryTenthTotal", errors)
    out_resolved = _int(quality, "birthOutOfUniverseEveryTenthResolved", errors)
    out_correct = _int(quality, "birthOutOfUniverseEveryTenthResolvedCorrect", errors)
    out_conflicts = _int(quality, "birthOutOfUniverseEveryTenthConflicts", errors)
    out_unresolved = _int(quality, "birthOutOfUniverseEveryTenthUnresolved", errors)

    in_total = _int(quality, "birthInUniverseOtherRowsTotal", errors)
    in_resolved = _int(quality, "birthInUniverseOtherRowsResolved", errors)
    in_correct = _int(quality, "birthInUniverseOtherRowsResolvedCorrect", errors)
    in_conflicts = _int(quality, "birthInUniverseOtherRowsConflicts", errors)
    in_unresolved = _int(quality, "birthInUniverseOtherRowsUnresolved", errors)

    upper = _num(quality, "zeroFpUpper95PctRuleOfThree", errors, nullable=True)
    margin_min = _num(quality, "conflictMarginMin", errors, nullable=True)
    margin_avg = _num(quality, "conflictMarginAvg", errors, nullable=True)
    margin_max = _num(quality, "conflictMarginMax", errors, nullable=True)

    if resolved != correct + false_positives:
        errors.append("resolved deve fechar resolvedCorrect + falsePositives")

    if first + tie + second != top2:
        errors.append("conflictsTruthTop2 deve fechar firstWithoutTie + top2Tie + secondWithoutTie")
    if first + tie + second + outside != conflicts:
        errors.append("conflitos devem ser particionados integralmente por posição da verdade")
    if dual > conflicts:
        errors.append("dualThresholdConflicts não pode exceder conflicts")

    for label, value in (
        ("conflictsBestPosteriorGe9999", best_9999),
        ("conflictsSecondPosteriorGe999", second_999),
        ("conflictsSecondPosteriorGe9999", second_9999),
    ):
        if value > conflicts:
            errors.append(f"{label} não pode exceder conflicts")
    if second_9999 > second_999:
        errors.append("conflictsSecondPosteriorGe9999 não pode exceder conflictsSecondPosteriorGe999")

    if out_total + in_total != total:
        errors.append("coortes de nascimento sintético devem particionar totalScale")
    if out_resolved + out_conflicts + out_unresolved != out_total:
        errors.append("coorte every-tenth deve fechar resolved + conflicts + unresolved")
    if in_resolved + in_conflicts + in_unresolved != in_total:
        errors.append("coorte other-rows deve fechar resolved + conflicts + unresolved")
    if out_correct > out_resolved or in_correct > in_resolved:
        errors.append("resolvedCorrect de coorte não pode exceder resolved")

    if false_positives == 0 and resolved > 0:
        expected = 300.0 / resolved
        if upper is None or abs(upper - expected) > 0.01:
            errors.append("zeroFpUpper95PctRuleOfThree deve ser 300/resolved quando falsePositives=0")
    elif upper is not None:
        errors.append("zeroFpUpper95PctRuleOfThree deve ser null quando não há condição de zero FP com n>0")

    if conflicts == 0:
        if any(v is not None for v in (margin_min, margin_avg, margin_max)):
            errors.append("margens de conflito devem ser null quando conflicts=0")
    elif None not in (margin_min, margin_avg, margin_max):
        assert margin_min is not None and margin_avg is not None and margin_max is not None
        if not (margin_min <= margin_avg <= margin_max):
            errors.append("conflictMarginMin <= conflictMarginAvg <= conflictMarginMax deve valer")

    return errors


def _fixture() -> dict:
    return {
        "decisionQuality": {
            "totalScale": 20,
            "resolved": 5,
            "resolvedCorrect": 5,
            "falsePositives": 0,
            "conflicts": 12,
            "conflictsTruthTop2": 11,
            "conflictsTruthFirstWithoutTie": 7,
            "conflictsTruthInTop2Tie": 1,
            "conflictsTruthSecondWithoutTie": 3,
            "conflictsTruthOutsideTop2": 1,
            "dualThresholdConflicts": 12,
            "conflictsBestPosteriorGe9999": 10,
            "conflictsSecondPosteriorGe999": 9,
            "conflictsSecondPosteriorGe9999": 8,
            "birthOutOfUniverseEveryTenthTotal": 2,
            "birthOutOfUniverseEveryTenthResolved": 2,
            "birthOutOfUniverseEveryTenthResolvedCorrect": 2,
            "birthOutOfUniverseEveryTenthConflicts": 0,
            "birthOutOfUniverseEveryTenthUnresolved": 0,
            "birthInUniverseOtherRowsTotal": 18,
            "birthInUniverseOtherRowsResolved": 3,
            "birthInUniverseOtherRowsResolvedCorrect": 3,
            "birthInUniverseOtherRowsConflicts": 12,
            "birthInUniverseOtherRowsUnresolved": 3,
            "zeroFpUpper95PctRuleOfThree": 60.0,
            "conflictMarginMin": 0.0,
            "conflictMarginAvg": 4.5,
            "conflictMarginMax": 9.0,
        }
    }


def self_test() -> None:
    valid = _fixture()
    errors = validate(valid)
    if errors:
        raise RuntimeError(f"self-test rejeitou fixture válida: {errors}")

    broken = json.loads(json.dumps(valid))
    broken["decisionQuality"]["conflictsTruthFirstWithoutTie"] = 8
    if not validate(broken):
        raise RuntimeError("self-test deveria rejeitar partição inconsistente dos conflitos")

    broken = json.loads(json.dumps(valid))
    broken["decisionQuality"]["birthOutOfUniverseEveryTenthTotal"] = 3
    if not validate(broken):
        raise RuntimeError("self-test deveria rejeitar partição inconsistente das coortes")

    broken = json.loads(json.dumps(valid))
    broken["decisionQuality"]["zeroFpUpper95PctRuleOfThree"] = 3.0
    if not validate(broken):
        raise RuntimeError("self-test deveria rejeitar limite da regra do três incorreto")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Valida completude diagnóstica da qualidade de decisão do linkage SCALE."
    )
    parser.add_argument("report", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        self_test()
        print("LINKAGE DECISION QUALITY GATE SELF-TEST: OK")
        return 0

    if args.report is None:
        parser.error("informe o relatório JSON ou use --self-test")

    data = json.loads(args.report.read_text(encoding="utf-8"))
    errors = validate(data)
    if errors:
        for error in errors:
            print(f"ERRO: {error}")
        return 1

    q = data["decisionQuality"]
    print(
        "LINKAGE DECISION QUALITY GATE: OK "
        f"(resolved={q['resolved']}; fp={q['falsePositives']}; "
        f"conflicts={q['conflicts']}; truth_first={q['conflictsTruthFirstWithoutTie']}; "
        f"truth_second={q['conflictsTruthSecondWithoutTie']}; "
        f"truth_tie={q['conflictsTruthInTop2Tie']}; truth_outside={q['conflictsTruthOutsideTop2']})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
