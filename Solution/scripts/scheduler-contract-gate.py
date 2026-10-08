#!/usr/bin/env python3
"""Fail-closed structural validation for the corporate scheduler contract v2.

No scheduler is installed by this script and pending HML decisions are never
inferred from technical defaults.
"""
from __future__ import annotations

import argparse
import copy
import json
import re
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = {
    "PROCESSOR_WORKER", "OPERATIONS_MAINTENANCE", "BRONZE_MAINTENANCE",
    "LINKAGE_GENERATE_DRAFT", "LINKAGE_VALIDATE", "LINKAGE_ACTIVATE",
    "LINKAGE_INCREMENTAL", "LINKAGE_REPLAY", "LINKAGE_FULL",
    "LINKAGE_MODEL_VALIDATION",
}
SHA = re.compile(r"^[0-9a-f]{64}$")
KINDS = {"CONTINUOUS_WORKER", "RUN_ONCE", "EXCEPTIONAL"}
STATUS = {"PENDENTE_HML", "APROVADO"}


class ContractError(ValueError):
    pass


def require(condition: bool, description: str) -> None:
    if not condition:
        raise ContractError(description)


def seconds(value: object, name: str, *, nullable: bool = False) -> None:
    if nullable and value is None:
        return
    require(type(value) is int and 1 <= value <= 604800,
            f"{name} deve ser inteiro positivo de até 604800 segundos")


def validate(document: object, root: Path, *, require_scheduled: bool = False) -> dict:
    require(isinstance(document, dict), "documento deve ser objeto")
    d = document
    require(d.get("schemaVersion") == 2, "schemaVersion deve ser 2")
    require(d.get("status") in STATUS, "status inválido")
    approved = d["status"] == "APROVADO"
    rows = d.get("jobs")
    require(isinstance(rows, list), "jobs deve ser array")
    by = {}
    for row in rows:
        require(isinstance(row, dict), "job deve ser objeto")
        jid = row.get("id")
        require(isinstance(jid, str) and bool(jid) and jid not in by,
                f"id ausente/duplicado: {jid}")
        by[jid] = row
        relative = row.get("project")
        require(isinstance(relative, str) and relative.endswith(".csproj"),
                f"{jid}: project inválido")
        project = (root / relative).resolve()
        require(project.is_relative_to(root) and project.is_file(),
                f"{jid}: projeto inexistente ou fora do root: {relative}")
        kind = row.get("kind")
        require(kind in KINDS, f"{jid}: kind inválido")
        require(row.get("status") in STATUS, f"{jid}: status inválido")
        require(type(row.get("supportsRunOnce")) is bool,
                f"{jid}: supportsRunOnce deve ser booleano")
        require(row["supportsRunOnce"], f"{jid}: execução finita não suportada")
        expected = "RESIDENT" if kind == "CONTINUOUS_WORKER" else "RUN_ONCE"
        require(row.get("defaultMode") == expected,
                f"{jid}: defaultMode incompatível com kind {kind}")
        require("runOnceMaxSeconds" in row and "maxExecutionSeconds" in row,
                f"{jid}: limites de execução devem ser declarados")
        if kind == "CONTINUOUS_WORKER":
            seconds(row["runOnceMaxSeconds"], f"{jid}.runOnceMaxSeconds")
        else:
            require(row["runOnceMaxSeconds"] is None,
                    f"{jid}: operação finita não possui switch RunOnceMaxSeconds")
        seconds(row["maxExecutionSeconds"], f"{jid}.maxExecutionSeconds", nullable=not approved)
        dependencies = row.get("dependsOn")
        require(isinstance(dependencies, list)
                and all(isinstance(dep, str) for dep in dependencies)
                and len(set(dependencies)) == len(dependencies),
                f"{jid}: dependsOn inválido ou duplicado")
        require(jid not in dependencies, f"{jid}: dependência reflexiva")
        require(not any(term in json.dumps(row).lower()
                        for term in ("password=", "access-key", "secret=", "token=")),
                f"{jid}: contrato contém segredo")
    require(set(by) == REQUIRED,
            f"jobs divergentes missing={sorted(REQUIRED - set(by))} "
            f"extra={sorted(set(by) - REQUIRED)}")
    for jid, row in by.items():
        require(all(dep in by for dep in row["dependsOn"]),
                f"{jid}: dependsOn inexistente")
    visiting, visited = set(), set()

    def visit(jid: str) -> None:
        require(jid not in visiting, f"ciclo de dependências: {jid}")
        if jid in visited:
            return
        visiting.add(jid)
        for dep in by[jid]["dependsOn"]:
            visit(dep)
        visiting.remove(jid)
        visited.add(jid)

    for jid in by:
        visit(jid)
    groups = d.get("mutualExclusionGroups")
    require(isinstance(groups, list), "mutualExclusionGroups deve ser array")
    for group in groups:
        require(isinstance(group, list) and len(group) >= 2
                and all(isinstance(jid, str) and jid in by for jid in group)
                and len(group) == len(set(group)),
                "mutualExclusionGroups possui job inexistente/duplicado")
    approval = d.get("approval")
    if approved:
        for jid, row in by.items():
            require(row["status"] == "APROVADO", f"{jid}: aprovação pendente")
            if row["kind"] != "EXCEPTIONAL":
                require(isinstance(row.get("cadence"), str) and bool(row["cadence"].strip()),
                        f"{jid}: cadence ausente")
            require(isinstance(row.get("owner"), str) and bool(row["owner"].strip()),
                    f"{jid}: owner ausente")
            require(isinstance(row.get("retryPolicy"), dict),
                    f"{jid}: retryPolicy ausente")
        require(isinstance(approval, dict), "approval ausente")
        require(isinstance(approval.get("approvedBy"), str)
                and bool(approval["approvedBy"].strip()), "approval.approvedBy ausente")
        at = approval.get("approvedAtUtc")
        require(isinstance(at, str) and at.endswith("Z"),
                "approval.approvedAtUtc inválido")
        try:
            datetime.fromisoformat(at.replace("Z", "+00:00"))
        except ValueError as exc:
            raise ContractError("approval.approvedAtUtc inválido") from exc
        evidence = approval.get("evidence")
        require(isinstance(evidence, dict)
                and SHA.fullmatch(str(evidence.get("sha256", "")).lower()) is not None,
                "approval.evidence.sha256 inválido")
    else:
        require(approval is None, "PENDENTE_HML não pode declarar approval")
    require(not require_scheduled or approved,
            "scheduler corporativo ainda não está APROVADO/configurado")
    return {"schemaVersion": 2, "status": "PASS", "jobCount": len(rows),
            "approved": approved}


def self_test(source: dict, root: Path) -> None:
    validate(source, root)
    cases = (
        lambda d: d.update(schemaVersion=1),
        lambda d: d["jobs"][0].pop("supportsRunOnce"),
        lambda d: d["jobs"][0].update(supportsRunOnce=False),
        lambda d: d["jobs"][0].update(defaultMode="RUN_ONCE"),
        lambda d: d["jobs"][0].update(runOnceMaxSeconds=0),
        lambda d: d["jobs"][0].update(maxExecutionSeconds=-1),
        lambda d: d["jobs"][0].update(project="../../outside.csproj"),
        lambda d: d["jobs"][3].update(dependsOn=["LINKAGE_VALIDATE"]),
        lambda d: d["jobs"][0].update(id="LINKAGE_VALIDATE"),
        lambda d: d["jobs"].pop(),
        lambda d: d.update(status="APROVADO"),
        lambda d: d["mutualExclusionGroups"].append(["UNKNOWN", "LINKAGE_FULL"]),
    )
    # The cross-job cycle is genuine: VALIDATE already depends on GENERATE_DRAFT.
    for index, mutate in enumerate(cases):
        case = copy.deepcopy(source)
        mutate(case)
        try:
            validate(case, root)
        except ContractError:
            continue
        raise ContractError(f"self-test {index} não rejeitou mutação inválida")
    # Positive pending HML remains valid but cannot pass release promotion.
    try:
        validate(source, root, require_scheduled=True)
    except ContractError:
        pass
    else:
        if source["status"] != "APROVADO":
            raise ContractError("self-test: promoção aceitou HML pendente")
    print(f"SCHEDULER CONTRACT GATE SELF-TEST: OK ({len(cases)} mutações rejeitadas)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=str(ROOT))
    parser.add_argument("--require-scheduled", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--summary")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    path = root / "config/operations/scheduler-jobs.json"
    try:
        source = json.loads(path.read_text(encoding="utf-8"))
        result = validate(source, root, require_scheduled=args.require_scheduled)
        if args.self_test:
            self_test(source, root)
    except (ContractError, OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"SCHEDULER CONTRACT GATE: FAIL: {exc}") from exc
    if args.summary:
        output = Path(args.summary)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"SCHEDULER CONTRACT GATE: OK ({result['jobCount']} jobs; approved={result['approved']})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
