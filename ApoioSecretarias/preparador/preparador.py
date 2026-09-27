#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
import tempfile
import zipfile
from datetime import datetime
from pathlib import Path
from jsonschema import Draft202012Validator, FormatChecker
from jsonschema.exceptions import ValidationError
from csv_to_json import convert

MAX_MANIFEST_BYTES = 64 * 1024
FORMAT_VERSION = 2


def _unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"manifest.json: campo duplicado: {key}")
        result[key] = value
    return result


def _read_manifest(path):
    raw = Path(path).read_bytes()
    if len(raw) > MAX_MANIFEST_BYTES:
        raise ValueError("manifest.json excede 64 KiB")
    try:
        manifest = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique_pairs)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("manifest.json inválido ou não UTF-8") from exc
    if not isinstance(manifest, dict):
        raise ValueError("manifest.json deve ser objeto JSON")
    return manifest


def _validate_manifest(manifest, mapping, records):
    """Replica as invariantes de entrada de IngestionPackageInspector do receptor.

    Este pré-voo não homologa o contrato factual nem substitui a validação
    autenticada do receptor; rejeita antes de criar qualquer ZIP incompatível.
    """
    if type(manifest.get("formatoVersao")) is not int or manifest["formatoVersao"] != FORMAT_VERSION:
        raise ValueError(f"formatoVersao deve ser {FORMAT_VERSION}")
    version = manifest.get("pessoaSchemaVersao")
    if type(version) is not int or version < 1 or version != mapping["pessoaSchemaVersao"]:
        raise ValueError("pessoaSchemaVersao inválida ou diferente do mapeamento")

    gestor, origin = mapping["gestor"], manifest.get("codigoSistemaOrigem")
    if not isinstance(gestor, str) or not re.fullmatch(r"[A-Z0-9_-]{2,60}", gestor):
        raise ValueError("Código de gestor inválido")
    if not isinstance(origin, str) or not re.fullmatch(r"[A-Z0-9_-]{1,80}", origin):
        raise ValueError("codigoSistemaOrigem deve conter 1 a 80 caracteres A-Z/0-9/_/-")

    reference = manifest.get("dataReferencia")
    if not isinstance(reference, str) or not re.fullmatch(
        r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})",
        reference,
    ):
        raise ValueError("dataReferencia exige data/hora ISO 8601 com fuso explícito")
    try:
        parsed_reference = datetime.fromisoformat(reference.replace("Z", "+00:00"))
        if parsed_reference.utcoffset() is None:
            raise ValueError("dataReferencia sem fuso")
    except ValueError as exc:
        raise ValueError("dataReferencia inválida") from exc

    base = manifest.get("codigoBasePessoaOrigem")
    if base is not None:
        if version < 4 or not isinstance(base, str) or not re.fullmatch(r"[A-Z0-9_-]{1,120}", base):
            raise ValueError("codigoBasePessoaOrigem exige Pessoa v4+ e código válido")

    context_fields = ("natureza", "codigoTipo", "tipoVersao")
    present = sum(manifest.get(key) is not None for key in context_fields)
    if present not in (0, 3):
        raise ValueError("natureza, codigoTipo e tipoVersao devem ser informados juntos")
    if records and present != 3:
        raise ValueError("registros.jsonl com conteúdo exige natureza, codigoTipo e tipoVersao")
    if present:
        if manifest["natureza"] not in ("BENEFICIO", "SERVICO"):
            raise ValueError("natureza inválida")
        tipo = manifest["codigoTipo"]
        if not isinstance(tipo, str) or not re.fullmatch(r"[A-Z0-9]{4}", tipo):
            raise ValueError("codigoTipo deve conter 4 caracteres A-Z/0-9")
        versao_tipo = manifest["tipoVersao"]
        if type(versao_tipo) is not int or versao_tipo < 1:
            raise ValueError("tipoVersao deve ser inteiro positivo")



# Esta lista é um inventário técnico derivado, não uma aprovação institucional.
FACTUAL_INDEX = Path(__file__).resolve().parents[1] / "config/governance/factual-schema-sources.json"


def _strict_record_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"registros.jsonl: chave duplicada: {key}")
        result[key] = value
    return result


def _reject_json_constant(value):
    raise ValueError(f"registros.jsonl: constante JSON inválida: {value}")


def _validate_factual_records(records, people, manifest, schema_path):
    """Fail-closed, com contrato factual versionado e hash fixado fora do ZIP."""
    if not records:
        return
    if schema_path is None:
        raise ValueError("registros.jsonl exige --registro-schema e contrato factual versionado")
    if any(manifest.get(key) is None for key in ("natureza", "codigoTipo", "tipoVersao")):
        raise ValueError("registros.jsonl exige natureza, codigoTipo e tipoVersao")
    try:
        index = json.loads(FACTUAL_INDEX.read_text(encoding="utf-8"))
        contracts = index["contracts"]
        matches = [entry for entry in contracts if
                   entry["natureza"] == manifest["natureza"] and
                   entry["codigoTipo"] == manifest["codigoTipo"] and
                   entry["tipoVersao"] == manifest["tipoVersao"]]
        if len(matches) != 1:
            raise ValueError("contrato factual não reconhecido na versão declarada")
        entry = matches[0]
        approved_path = (FACTUAL_INDEX.parents[2] / entry["path"]).resolve()
        if Path(schema_path).resolve() != approved_path:
            raise ValueError("registro-schema fora do contrato versionado fixado")
        raw_schema = approved_path.read_bytes()
        digest = hashlib.sha256(raw_schema).hexdigest()
        if digest != entry["sha256"]:
            raise ValueError("SHA-256 do contrato factual diverge do inventário")
        schema = json.loads(raw_schema.decode("utf-8"), object_pairs_hook=_strict_record_pairs,
                            parse_constant=_reject_json_constant)
        Draft202012Validator.check_schema(schema)
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
    except (OSError, UnicodeError, KeyError, TypeError, json.JSONDecodeError) as exc:
        raise ValueError("contrato factual indisponível ou inventário inválido") from exc

    people_ids = {json.loads(line)["idPessoaEntrega"] for line in people.decode("utf-8").splitlines()}
    try:
        lines = records.decode("utf-8-sig").splitlines()
    except UnicodeError as exc:
        raise ValueError("registros.jsonl não é UTF-8") from exc
    for line_number, line in enumerate(lines, 1):
        if not line.strip():
            raise ValueError(f"registros.jsonl linha {line_number}: linha vazia")
        try:
            record = json.loads(line, object_pairs_hook=_strict_record_pairs,
                                parse_constant=_reject_json_constant)
        except json.JSONDecodeError as exc:
            raise ValueError(f"registros.jsonl linha {line_number}: JSON inválido") from exc
        if not isinstance(record, dict):
            raise ValueError(f"registros.jsonl linha {line_number}: objeto JSON obrigatório")
        try:
            validator.validate(record)
        except ValidationError as exc:
            # Não ecoar o conteúdo da linha, CPF ou outros atributos ao CLI.
            raise ValueError(f"registros.jsonl linha {line_number}: contrato factual inválido") from exc
        if record["idPessoaEntrega"] not in people_ids:
            raise ValueError(f"registros.jsonl linha {line_number}: idPessoaEntrega sem pessoa na entrega")



def preparar(args):
    manifest = _read_manifest(args.manifest)
    people, mapping = convert(args.csv, args.mapeamento, args.schema)
    records = b"" if not args.registros else Path(args.registros).read_bytes()
    if len(records) > 100 * 1024 * 1024:
        raise ValueError("registros.jsonl maior que 100 MiB")
    _validate_manifest(manifest, mapping, records)
    _validate_factual_records(records, people, manifest, getattr(args, "registro_schema", None))
    gestor, origin = mapping["gestor"], manifest["codigoSistemaOrigem"]
    entries = {
        "manifest.json": (json.dumps(manifest, separators=(",", ":"), ensure_ascii=False) + "\n").encode(),
        "pessoas.jsonl": people,
        "registros.jsonl": records
    }
    target_dir = Path(args.saida)
    target_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=target_dir, suffix=".zip", delete=False) as temp:
        temp_path = Path(temp.name)
    try:
        with zipfile.ZipFile(temp_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            for name, data in entries.items():
                entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                entry.external_attr = 0o600 << 16
                z.writestr(entry, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        sha = hashlib.sha256(temp_path.read_bytes()).hexdigest()
        target = target_dir / f"ENTREGA_{gestor}_{origin}_v{manifest['formatoVersao']}_{sha}.zip"
        if target.exists():
            if hashlib.sha256(target.read_bytes()).hexdigest() != sha:
                raise ValueError("Colisão de nome; destino preservado")
        else:
            temp_path.replace(target)
        return target
    finally:
        if temp_path.exists():
            temp_path.unlink()

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for field in ("csv", "mapeamento", "manifest", "schema", "saida"):
        parser.add_argument("--" + field, required=True)
    parser.add_argument("--registros")
    parser.add_argument("--registro-schema", help="Schema factual versionado da Solução de Apoio; obrigatório para registros não vazios.")
    args = parser.parse_args()
    try:
        print(preparar(args))
    except (ValueError, OSError) as error:
        parser.exit(2, f"PREPARO_REJEITADO: {error}\n")
