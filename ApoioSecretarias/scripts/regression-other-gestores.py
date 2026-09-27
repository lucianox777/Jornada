#!/usr/bin/env python3
"""Gate 6: regressão independente de integridade e validação dos gestores remanescentes.

Não é teste HTTP/SQL: executa schemas v1-v5 e verifica os 21 hashes do inventário.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

MAIN = Path(__file__).resolve().parents[2] / "Solution"
GESTORES = ("SMADS", "SMDET", "SMS")


def check_contracts() -> None:
    inventory = json.loads((MAIN / "config/governance/schema-approvals.json").read_text(encoding="utf-8"))
    by_path = {item["path"]: item for item in inventory["contracts"]}
    assert len(by_path) == len(inventory["contracts"]), "Inventário com entradas duplicadas"
    total = 0
    for gestor in GESTORES:
        for version in range(1, 6):
            path = f"config/contracts/gestores/{gestor}/pessoa/v{version}/pessoa.schema.json"
            assert path in by_path, f"Contrato faltante no inventário: {path}"
            raw = (MAIN / path).read_bytes()
            actual_hash = hashlib.sha256(raw).hexdigest()
            assert actual_hash == by_path[path]["sha256"], f"Contrato divergente: {path}"
            schema = json.loads(raw)
            Draft202012Validator.check_schema(schema)
            validator = Draft202012Validator(schema, format_checker=FormatChecker())
            person = {
                "codigoPessoaOrigem": f"SINTETICO-{gestor}-{version}",
                "idPessoaEntrega": f"SINTETICO-{gestor}-{version}",
                "cpf": "11144477735",
                "cpfAusenteMotivo": None,
                "nomeCompleto": "Pessoa Sintetica Controle",
                "dataNascimento": "1980-01-02",
                "nomeMae": "Nome Mae Sintetica",
            }
            validator.validate({key: val for key, val in person.items() if key in schema["properties"]})
            if version >= 3:
                person.pop("nomeMae")
                validator.validate({key: val for key, val in person.items() if key in schema["properties"]})
            total += 1
        for version in (1, 2):
            path = f"config/contracts/gestores/{gestor}/pessoa/v{version}/pessoa.json"
            assert path in by_path, f"Metadado faltante: {path}"
            raw = (MAIN / path).read_bytes()
            assert hashlib.sha256(raw).hexdigest() == by_path[path]["sha256"], path
            assert json.loads(raw)["gestor"] == gestor, path
            total += 1
    assert total == 21, f"Esperados 21 arquivos, obtidos {total}"
    print("OK: 21 contratos/metadados dos Gestores SMADS, SMDET, SMS; schemas v1-v5 validados")


if __name__ == "__main__":
    check_contracts()
