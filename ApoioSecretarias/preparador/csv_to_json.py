import csv
import hashlib
import json
import re
from datetime import date
from pathlib import Path
from jsonschema import Draft202012Validator, FormatChecker

REQUIRED = {"idPessoaEntrega", "nomeCompleto", "dataNascimento"}
ALLOWED = REQUIRED | {"codigoPessoaOrigem", "cpf", "cpfAusenteMotivo", "nomeMae", "sourceTransactionId"}

def convert(csv_file, mapping_file, schema_file):
    mapping = json.loads(Path(mapping_file).read_text(encoding="utf-8"))
    source = Path(schema_file).read_bytes()
    if hashlib.sha256(source).hexdigest() != mapping["schemaSha256"]:
        raise ValueError("SHA-256 do schema diverge do contrato")
    schema = json.loads(source)
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    columns = mapping["colunas"]
    if not REQUIRED.issubset(columns) or set(columns) - ALLOWED:
        raise ValueError("Mapeamento incompleto ou desconhecido")
    if Path(csv_file).stat().st_size > 100 * 1024 * 1024:
        raise ValueError("CSV excede 100 MiB")
    output = []
    with open(csv_file, encoding="utf-8-sig", newline="") as source:
        reader = csv.DictReader(source, delimiter=mapping.get("delimitador", ";"), strict=True)
        headers = reader.fieldnames or []
        if len(headers) != len(set(headers)) or not set(columns.values()).issubset(headers):
            raise ValueError("Cabeçalhos duplicados ou faltantes")
        for number, row in enumerate(reader, 2):
            if number > 100001 or None in row or any(x is None for x in row.values()):
                raise ValueError(f"Linha {number}: colunas ou limite inválidos")
            person = {key: row[value].strip() for key, value in columns.items() if row[value].strip()}
            if not all(person.get(key) for key in REQUIRED):
                raise ValueError(f"Linha {number}: campos obrigatórios vazios")
            if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", person["dataNascimento"]):
                raise ValueError(f"Linha {number}: data inválida")
            date.fromisoformat(person["dataNascimento"])
            if person.get("cpf"):
                if not re.fullmatch(r"[0-9.\- ]+", person["cpf"]):
                    raise ValueError(f"Linha {number}: CPF inválido")
                person["cpf"] = re.sub(r"[.\- ]", "", person["cpf"])
                if not re.fullmatch(r"\d{11}", person["cpf"]) or person.get("cpfAusenteMotivo"):
                    raise ValueError(f"Linha {number}: CPF ou motivo contraditório")
            else:
                # O contrato Pessoa v4 distingue CPF explicitamente nulo de atributo ausente.
                # Nunca inferir o motivo: o CSV deve declará-lo na coluna mapeada.
                person["cpf"] = None
                if not person.get("cpfAusenteMotivo"):
                    raise ValueError(f"Linha {number}: falta motivo de ausência de CPF declarado")
            validator.validate(person)
            output.append(json.dumps(person, ensure_ascii=False, separators=(",", ":")))
    if not output:
        raise ValueError("CSV sem pessoas")
    return ("\n".join(output) + "\n").encode("utf-8"), mapping
