#!/usr/bin/env python3
"""Bidirectional route gate for Resultado.Api; does not assert HTTP/SQL runtime."""
import importlib.util
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
source = ROOT / "scripts/openapi-contract-gate.py"
spec = importlib.util.spec_from_file_location("jornada_openapi_gate", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def validate(program: Path, contract: Path) -> list[str]:
    code, errors = module.code_operations(program)
    documented, contract_errors = module.contract_operations(contract)
    errors.extend(contract_errors)
    errors.extend(f"rota no código ausente do contrato: {m.upper()} {p}" for m, p in sorted(code - documented))
    errors.extend(f"rota no contrato ausente do código: {m.upper()} {p}" for m, p in sorted(documented - code))
    if not code:
        errors.append("nenhuma rota Resultado.Api encontrada")
    return errors


if __name__ == "__main__":
    errors = validate(ROOT / "src/Jornada.Resultado.Api/Program.cs", ROOT / "openapi/resultado-v1.openapi.json")
    if errors:
        print("RESULTADO OPENAPI GATE: FALHOU", *errors, sep="\n - ", file=sys.stderr)
        raise SystemExit(1)
    print("RESULTADO OPENAPI GATE: OK")
