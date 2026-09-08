#!/usr/bin/env python3
"""Fail-closed gate between Minimal API declarations and the published V1 OpenAPI."""
from __future__ import annotations
import argparse
import json
import re
import sys
from pathlib import Path

MAP_RE = re.compile(r'app\s*\.\s*Map(Get|Post|Put|Delete|Patch)\s*\(\s*"([^"]+)"', re.IGNORECASE | re.MULTILINE)
APP_MAP_RE = re.compile(r'app\s*\.\s*(Map[A-Za-z0-9_]*)\s*\(', re.IGNORECASE | re.MULTILINE)
SUPPORTED_MAP_CALLS = {"mapget", "mappost", "mapput", "mapdelete", "mappatch"}
CONSTRAINT_RE = re.compile(r'\{([^}:]+):[^}]+\}')
HTTP_METHODS = {"get", "post", "put", "delete", "patch", "options", "head", "trace"}
# Explicit registration is required. A new Map* extension cannot silently bypass the gate.
REGISTERED_MODULES = {"mapprogressiveoriginapi": "ProgressiveOriginApi.cs"}


def normalize_path(path: str) -> str:
    return CONSTRAINT_RE.sub(r"{\1}", path.rstrip("/") or "/")


def module_operations(program: Path, name: str) -> tuple[set[tuple[str, str]], list[str]]:
    filename = REGISTERED_MODULES[name]
    path = program.parent / filename
    if not path.is_file():
        return set(), [f"módulo registrado ausente: {filename}"]
    text = path.read_text(encoding="utf-8-sig")
    errors: list[str] = []
    # O módulo possui exatamente uma rota POST e declara o caminho como constante.
    if name == "mapprogressiveoriginapi":
        route = re.search(r'public\s+const\s+string\s+Route\s*=\s*"([^"]+)"', text)
        definition = re.search(r'public\s+static\s+IEndpointRouteBuilder\s+MapProgressiveOriginApi\s*\(\s*this\s+IEndpointRouteBuilder\s+app\s*\)', text)
        calls = APP_MAP_RE.findall(text)
        if not definition or not route or len(calls) != 1 or calls[0].lower() != "mappost" or not re.search(r'app\s*\.\s*MapPost\s*\(\s*Route\s*,', text):
            errors.append("módulo progressivo contém mapeamento ausente, ambíguo ou não inventariado")
            return set(), errors
        if not route.group(1).startswith("/api/v1/"):
            errors.append("rota progressiva fora do namespace V1")
            return set(), errors
        return {("post", normalize_path(route.group(1)))}, errors
    return set(), [f"módulo sem verificador: {filename}"]


def code_operations(program: Path) -> tuple[set[tuple[str, str]], list[str]]:
    text = program.read_text(encoding="utf-8-sig")
    errors: list[str] = []
    calls = [name.lower() for name in APP_MAP_RE.findall(text)]
    unsupported = sorted(set(calls) - SUPPORTED_MAP_CALLS - set(REGISTERED_MODULES))
    errors.extend(f"mapeamento Minimal API não suportado pelo gate: {name}" for name in unsupported)
    operations = {(method.lower(), normalize_path(path)) for method, path in MAP_RE.findall(text)}
    if len(calls) != len(MAP_RE.findall(text)) + sum(calls.count(name) for name in REGISTERED_MODULES):
        errors.append("mapeamento não literal ou não inventariado em Program.cs")
    for name in REGISTERED_MODULES:
        if name in calls:
            if calls.count(name) != 1:
                errors.append(f"módulo registrado mais de uma vez: {name}")
                continue
            extra, module_errors = module_operations(program, name)
            errors.extend(module_errors)
            if operations & extra:
                errors.append(f"rota duplicada entre host e módulo: {name}")
            operations.update(extra)
    if not operations:
        errors.append("nenhuma rota Minimal API reconhecida em Program.cs")
    return operations, errors


def contract_operations(openapi: Path) -> tuple[set[tuple[str, str]], list[str]]:
    document = json.loads(openapi.read_text(encoding="utf-8"))
    if not str(document.get("openapi", "")).startswith("3."):
        raise ValueError("openapi deve declarar uma versão 3.x")
    operations: set[tuple[str, str]] = set()
    operation_ids: set[str] = set()
    errors: list[str] = []
    for path, item in document.get("paths", {}).items():
        if not isinstance(item, dict):
            errors.append(f"path inválido: {path}")
            continue
        for method, operation in item.items():
            method_lower = method.lower()
            if method_lower not in HTTP_METHODS:
                continue
            operations.add((method_lower, normalize_path(path)))
            if not isinstance(operation, dict):
                errors.append(f"operação inválida: {method_upper(method_lower)} {path}")
                continue
            responses = operation.get("responses")
            if not isinstance(responses, dict) or not responses:
                errors.append(f"sem responses: {method_upper(method_lower)} {path}")
            operation_id = operation.get("operationId")
            if operation_id:
                if operation_id in operation_ids:
                    errors.append(f"operationId duplicado: {operation_id}")
                operation_ids.add(operation_id)
    return operations, errors


def method_upper(method: str) -> str:
    return method.upper()


def main() -> int:
    solution_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--program", type=Path, default=solution_root / "src/Jornada.Api/Program.cs")
    parser.add_argument("--openapi", type=Path, default=solution_root / "openapi/jornada-v1.openapi.json")
    args = parser.parse_args()
    program = args.program.expanduser().resolve()
    openapi = args.openapi.expanduser().resolve()
    if not program.is_file() or not openapi.is_file():
        print("ERRO: Program.cs ou OpenAPI não encontrado.", file=sys.stderr)
        return 2
    code, code_errors = code_operations(program)
    contract, errors = contract_operations(openapi)
    errors.extend(code_errors)
    missing = sorted(code - contract)
    extra = sorted(contract - code)
    if missing:
        errors.extend(f"rota no código ausente do contrato: {m.upper()} {p}" for m, p in missing)
    if extra:
        errors.extend(f"rota no contrato ausente do código: {m.upper()} {p}" for m, p in extra)
    if errors:
        print("OPENAPI CONTRACT GATE: FALHOU", file=sys.stderr)
        for error in errors:
            print(f" - {error}", file=sys.stderr)
        return 1
    print(f"OPENAPI CONTRACT GATE: OK ({len(code)} métodos/rotas)")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
