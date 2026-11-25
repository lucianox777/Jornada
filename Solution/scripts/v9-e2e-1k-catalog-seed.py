#!/usr/bin/env python3
"""Derive the catalog/credential-only fixture from the reviewed canonical seed.

The ordinary DEV seed creates people, direct Gold rows and a prior linkage run.
Those must never enter the 1k FULL universe. Do not execute the entire seed
and then delete data: prepare a clean catalog-only SQL batch before ingestion.
"""
import argparse
import hashlib
from pathlib import Path
import re

SEED_BLOB = "2e25aaf1b779f16a6b5135afe2c8e3cf825c9722"
LEGACY = "-- Contrato Pessoa v1 substitui os checkpoints v2-v6 de desenvolvimento."
CREDENTIALS = "-- Credenciais no banco guardam somente secret_ref."
PERSON = "-- Pessoas Golden sintéticas iniciais:"
EMPTY = """IF EXISTS(SELECT 1 FROM silver.pessoa_observacao)
   OR EXISTS(SELECT 1 FROM silver.pessoa_origem)
   OR EXISTS(SELECT 1 FROM gold.pessoa)
   OR EXISTS(SELECT 1 FROM identidade.pessoa)
   OR EXISTS(SELECT 1 FROM identidade.linkage_run)
   OR EXISTS(SELECT 1 FROM ingestao.entrega)
   OR EXISTS(SELECT 1 FROM bronze.entrega_arquivo)
   OR EXISTS(SELECT 1 FROM identidade.blocking_chave_v9)
   OR EXISTS(SELECT 1 FROM identidade.blocking_chave_v9_cobertura)
    THROW 52986,'V9_E2E_CATALOG_REQUIRES_EMPTY_ATTEMPT',1;
"""
GUARD = """-- Generated for one fresh DEV attempt, not the ordinary local seed.
IF @attempt IS NULL OR @attempt='00000000-0000-0000-0000-000000000000'
   OR DB_NAME() COLLATE Latin1_General_100_BIN2 <>
      N'JornadaV9Dev_'+REPLACE(LOWER(CONVERT(nvarchar(36),@attempt)),N'-',N'')
   OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0
       AND name=N'Jornada.EnvironmentProfile'
       AND CONVERT(nvarchar(200),value) COLLATE Latin1_General_100_BIN2=N'Development')
   OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0
       AND name=N'Jornada.V9.DevAttempt'
       AND CONVERT(nvarchar(200),value) COLLATE Latin1_General_100_BIN2=LOWER(CONVERT(nvarchar(36),@attempt)))
    THROW 52985,'V9_E2E_CATALOG_REQUIRES_DISPOSABLE_DEV',1;
"""


def derive(raw):
    if hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != SEED_BLOB:
        raise ValueError("V9_E2E_CATALOG_SEED_DRIFT")
    source = raw.decode("utf-8-sig")
    markers = (LEGACY, CREDENTIALS, PERSON)
    if any(source.count(x) != 1 for x in markers):
        raise ValueError("V9_E2E_CATALOG_BOUNDARY_INVALID")
    legacy, credentials, person = (source.index(x) for x in markers)
    if not 0 < legacy < credentials < person:
        raise ValueError("V9_E2E_CATALOG_BOUNDARY_INVALID")
    # Legacy reconciliation is not needed in a fresh database. Only
    # reference catalog and test credential definitions are copied.
    payload = source[:legacy] + source[credentials:person]
    if re.search(r"(?im)^\s*(?:INSERT(?:\s+INTO)?|MERGE|DELETE\s+FROM)\s+(?:silver|gold|identidade|ingestao|bronze)\.", payload):
        raise ValueError("V9_E2E_CATALOG_PERSON_WRITE_FORBIDDEN")
    begin = "BEGIN TRANSACTION;"
    if payload.count(begin) != 1:
        raise ValueError("V9_E2E_CATALOG_TRANSACTION_INVALID")
    # The eventual caller binds @attempt as a typed uniqueidentifier
    # parameter. The same empty-state guard runs before and after the seed.
    return (payload.replace(begin, GUARD + begin + "\n" + EMPTY, 1)
            + "\n" + EMPTY + "\nCOMMIT TRANSACTION;\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.seed.is_symlink() or not args.seed.is_file() or args.seed.stat().st_size > 1024 * 1024:
            raise ValueError("V9_E2E_CATALOG_SEED_INVALID")
        payload = derive(args.seed.read_bytes())
        with args.output.open("x", encoding="utf-8", newline="\n") as stream:
            stream.write(payload)
    except (ValueError, OSError):
        raise SystemExit("V9_E2E_CATALOG_SEED_REJECTED") from None
    print("V9_E2E_CATALOG_BATCH_PREPARED_NOT_EXECUTED")


if __name__ == "__main__":
    main()
