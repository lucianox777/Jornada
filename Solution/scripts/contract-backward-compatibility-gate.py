#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path

PRE_DEPLOYMENT_MODE = "PRE_DEPLOYMENT_NO_HISTORICAL_BASELINE"


def fail(message: str) -> None:
    raise SystemExit(f"CONTRACT COMPATIBILITY POLICY GATE: FAIL: {message}")


def read_release_info(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" not in line or line.lstrip().startswith("#"):
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip()
    return values


def load_policy(path: Path) -> dict:
    if not path.is_file():
        fail(f"policy de compatibilidade ausente: {path}")
    try:
        policy = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        fail(f"policy de compatibilidade inválida: {exc}")
    if policy.get("schemaVersion") != 1:
        fail("schemaVersion inválido")
    return policy


def evaluate(policy: dict, predecessor_tag: str) -> dict:
    if policy.get("predecessorTag") != predecessor_tag:
        fail(
            "predecessorTag divergente: "
            f"{policy.get('predecessorTag')} != {predecessor_tag}"
        )

    deployed = policy.get("deployed")
    mode = policy.get("mode")

    if deployed is False:
        if mode != PRE_DEPLOYMENT_MODE:
            fail(
                "projeto pré-implantação deve declarar explicitamente "
                f"mode={PRE_DEPLOYMENT_MODE}"
            )
        return {
            "status": "PASS",
            "deployed": False,
            "mode": mode,
            "predecessorTag": predecessor_tag,
            "historicalCompatibilityComparison": "NOT_APPLICABLE",
            "reason": (
                "Ainda não existe integração de dados ou contrato implantado que constitua "
                "baseline histórica. O CI protege a especificação corrente e seus invariantes, "
                "sem congelar versões internas anteriores."
            ),
            "preserve": policy.get("preserveBeforeDeployment", []),
            "notRequired": policy.get("notRequiredBeforeDeployment", []),
        }

    if deployed is True:
        fail(
            "deployed=true exige baseline operacional explícita e um gate pós-implantação "
            "versionado. Não é permitido reutilizar automaticamente tags internas de projeto "
            "como baseline de produção."
        )

    fail("campo deployed deve ser booleano")


def selftest() -> None:
    base = {
        "schemaVersion": 1,
        "predecessorTag": "v0",
        "mode": PRE_DEPLOYMENT_MODE,
        "deployed": False,
    }
    result = evaluate(base, "v0")
    if result.get("historicalCompatibilityComparison") != "NOT_APPLICABLE":
        fail("self-test não desativou comparação histórica pré-implantação")

    invalid = dict(base)
    invalid["mode"] = "NO_BREAKING_CHANGE_EXPECTED"
    try:
        evaluate(invalid, "v0")
    except SystemExit:
        pass
    else:
        fail("self-test aceitou modo histórico em projeto pré-implantação")

    deployed = dict(base)
    deployed["deployed"] = True
    try:
        evaluate(deployed, "v0")
    except SystemExit:
        pass
    else:
        fail("self-test aceitou deployed=true sem baseline pós-implantação")

    print("CONTRACT COMPATIBILITY POLICY GATE SELF-TEST: OK")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=".")
    parser.add_argument("--release-info", default="RELEASE_INFO.txt")
    parser.add_argument("--policy")
    parser.add_argument("--summary")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        selftest()
        return

    repo = Path(args.repo).resolve()
    release_info = read_release_info(repo / args.release_info)
    predecessor_tag = release_info.get("source_git_predecessor_tag")
    if not predecessor_tag:
        fail("source_git_predecessor_tag ausente")

    policy_path = (
        Path(args.policy)
        if args.policy
        else repo / "Solution/config/release/contract-compatibility-policy.json"
    )
    if not policy_path.is_absolute():
        policy_path = repo / policy_path

    result = evaluate(load_policy(policy_path), predecessor_tag)

    if args.summary:
        summary_path = Path(args.summary)
        summary_path.parent.mkdir(parents=True, exist_ok=True)
        summary_path.write_text(
            json.dumps(result, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )

    print(
        "CONTRACT COMPATIBILITY POLICY GATE: OK "
        "(pré-implantação; comparação histórica não aplicável)"
    )


if __name__ == "__main__":
    main()
