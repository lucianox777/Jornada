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


def _nonnegative_int(value: object, label: str, errors: list[str]) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        errors.append(f"{label} deve ser inteiro >= 0")
        return 0
    return value


def validate(data: dict) -> list[str]:
    errors: list[str] = []

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

        attributes = pressure.get("attributes")
        if not isinstance(attributes, list) or not attributes:
            errors.append("blockingPressure.attributes deve conter ao menos um atributo")
        else:
            seen: set[str] = set()
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
                _nonnegative_int(item.get("distinctValues"), f"blockingPressure.attributes[{index}].distinctValues", errors)
                _nonnegative_int(item.get("maxPeoplePerValue"), f"blockingPressure.attributes[{index}].maxPeoplePerValue", errors)

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
        "runtimeScope": {"blocking": {"mode": "RULESET"}},
        "blockingPressure": {
            "ruleSetPassCount": 3,
            "blockingRows": 100,
            "distinctKeys": 25,
            "maxPeoplePerKey": 12,
            "attributes": [
                {"attribute": "NOME", "rows": 60, "distinctValues": 20, "maxPeoplePerValue": 8},
                {"attribute": "DATA_NASCIMENTO", "rows": 40, "distinctValues": 10, "maxPeoplePerValue": 12},
            ],
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

    print("SCALE OBSERVABILITY EVIDENCE GATE SELF-TEST: OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Valida métricas observáveis de pressão de blocking e coordenação do harness de escala.")
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
