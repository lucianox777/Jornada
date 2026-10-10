#!/usr/bin/env python3
"""Verify the committed demographic bootstrap snapshot without recalculating it."""
import hashlib
import json
import argparse
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "data" / "reference" / "synthetic-birth-sp"

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--emit-sql', help='Write idempotent SQL import script to this path')
    args = parser.parse_args()
    manifest = json.loads((BASE / "manifest.json").read_text(encoding="utf-8"))
    output = manifest["output"]
    name = output["path"]
    if Path(name).name != name:
        raise ValueError("Snapshot path must be a filename in the reference directory")
    raw = (BASE / name).read_bytes()
    actual = hashlib.sha256(raw).hexdigest().upper()
    if actual != output["sha256"].upper():
        raise ValueError(f"Snapshot SHA-256 mismatch: {actual}")
    document = json.loads(raw)
    if document.get("schema_version") != output["schemaVersion"]:
        raise ValueError("Snapshot schema mismatch")
    rows = document.get("rows")
    if not isinstance(rows, list) or len(rows) != output["rowCount"]:
        raise ValueError("Snapshot row count mismatch")
    if not rows or any(not isinstance(row, dict) or
                       not isinstance(row.get("date"), str) or
                       type(row.get("births")) is not int or row["births"] <= 0
                       for row in rows):
        raise ValueError("Snapshot rows invalid")
    if len({row["date"] for row in rows}) != len(rows):
        raise ValueError("Duplicate birth dates")
    if args.emit_sql:
        source = next(x for x in manifest['sources'] if x['kind'] == 'IBGE_PROJECAO_POPULACAO_REVISAO_2024')
        if source['geography'] != 'UF_SP':
            raise ValueError('Unexpected reference geography')
        date.fromisoformat(source['referenceDate'])
        if any(date.fromisoformat(row['date']).isoformat() != row['date'] for row in rows):
            raise ValueError('Invalid canonical birth date')
        def quote(value):
            return "N'" + str(value).replace("'", "''") + "'"
        # Concatenate short Unicode literals into NVARCHAR(MAX), avoiding the 4000-character limit.
        payload = raw.decode('utf-8')
        chunks = [payload[i:i+2000] for i in range(0, len(payload), 2000)]
        lines = ['SET NOCOUNT ON;', 'SET XACT_ABORT ON;', 'DECLARE @json NVARCHAR(MAX) = CONVERT(NVARCHAR(MAX), N\'\');']
        lines += ['SET @json = @json + ' + quote(chunk) + ';' for chunk in chunks]
        lines += [
            'DECLARE @id BIGINT;',
            'EXEC ref.sp_carregar_distribuicao_nascimento_json',
            ' @codigo=' + quote(manifest['referenceCode']) + ',',
            ' @fonte=' + quote(source['kind']) + ',',
            ' @geografia=' + quote(source['geography']) + ',',
            " @data_referencia='" + source['referenceDate'] + "',",
            " @metodo=N'FROZEN_PROJECTION_2024_E2_V1',",
            " @fonte_arquivo_sha256='" + actual + "',",
            ' @linhas_esperadas=' + str(len(rows)) + ',',
            ' @peso_total_esperado=' + str(sum(r['births'] for r in rows)) + ',',
            ' @json=@json, @distribuicao_versao_id=@id OUTPUT;',
            'IF EXISTS (SELECT 1 FROM ref.distribuicao_nascimento_versao WHERE distribuicao_versao_id=@id AND status=N\'CARREGANDO\')',
            ' EXEC ref.sp_publicar_distribuicao_nascimento @id, ' + str(len(rows)) + ', ' + str(sum(r['births'] for r in rows)) + ';',
            'IF NOT EXISTS (SELECT 1 FROM ref.distribuicao_nascimento_versao WHERE distribuicao_versao_id=@id AND status=N\'PUBLICADA\') THROW 52260,\'Frozen reference not published\',1;',
            'PRINT N\'FROZEN DEMOGRAPHIC REFERENCE PUBLISHED OR VERIFIED\';',
        ]
        Path(args.emit_sql).write_text('\\n'.join(lines) + '\\n', encoding='utf-8')
    print(f"FROZEN BIRTH REFERENCE OK: {manifest['referenceCode']} "
          f"rows={len(rows)} sha256={actual} total_weight={sum(r['births'] for r in rows)}")
    print("No projection recalculated; no database modified.")

if __name__ == "__main__":
    main()
