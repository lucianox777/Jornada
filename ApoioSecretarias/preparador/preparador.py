#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
import tempfile
import zipfile
from pathlib import Path
from csv_to_json import convert

def preparar(args):
    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    people, mapping = convert(args.csv, args.mapeamento, args.schema)
    if manifest["pessoaSchemaVersao"] != mapping["pessoaSchemaVersao"]:
        raise ValueError("Versão Pessoa não corresponde ao mapeamento")
    gestor, origin = mapping["gestor"], manifest["codigoSistemaOrigem"]
    if not all(re.fullmatch(r"[A-Z0-9_-]{2,60}", x) for x in (gestor, origin)):
        raise ValueError("Código de gestor ou origem inválido")
    records = b"" if not args.registros else Path(args.registros).read_bytes()
    if len(records) > 100 * 1024 * 1024:
        raise ValueError("registros.jsonl maior que 100 MiB")
    for line in records.decode("utf-8-sig").splitlines():
        if line.strip() and not isinstance(json.loads(line), dict):
            raise ValueError("Registro não é objeto JSON")
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
    args = parser.parse_args()
    try:
        print(preparar(args))
    except (ValueError, OSError) as error:
        parser.exit(2, f"PREPARO_REJEITADO: {error}\n")
