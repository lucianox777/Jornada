#!/usr/bin/env python3
"""Guard the independent linkage workflow SQL password boundary without DB access."""
from __future__ import annotations

import re
from pathlib import Path

PATH = Path(__file__).resolve().parents[2] / ".github/workflows/linkage-independent-validation.yml"
PREFIX = 'SQLCMDPASSWORD="$SQL_PASSWORD" docker compose --env-file .env exec -T -e SQLCMDPASSWORD sqlserver'
SQLCMD = "/opt/mssql-tools18/bin/sqlcmd"


def check(source: str) -> None:
    if '-e "SQLCMDPASSWORD=' in source or "-P " in source:
        raise AssertionError("Linkage workflow must not expose SQL credentials in Docker argv")
    calls = [
        line.strip() for line in source.splitlines()
        if "docker compose" in line and SQLCMD in line
    ]
    if len(calls) != 2:
        raise AssertionError(f"Expected two read-only SQL probes, got {len(calls)}")
    for line in calls:
        if PREFIX not in line or "-S localhost -U sa -C" not in line:
            raise AssertionError("Read-only SQL probe must forward only the variable name")
        if "SQLCMDPASSWORD=$" in line:
            raise AssertionError("Do not pass the password as a Docker argument")
    if source.count('SQL_PASSWORD="$(awk -F=') != 2:
        raise AssertionError("Each read-only probe must obtain the current local credential")
    for required in (
        "CALIBRATION PRECEDES VALIDATION CORPUS: OK",
        "LinkageParameters__Operation=GENERATE_DRAFT",
        "Prove recalibration excludes validation Gold",
        "po_val.codigo_pessoa_origem LIKE N'SCALE-VAL-%'",
    ):
        if required not in source:
            raise AssertionError(f"Lost independent linkage gate: {required}")


def self_test(source: str) -> None:
    mutations = (
        (PREFIX, PREFIX.replace("-e SQLCMDPASSWORD", '-e "SQLCMDPASSWORD=$SQL_PASSWORD"')),
        (PREFIX, PREFIX.replace('SQLCMDPASSWORD="$SQL_PASSWORD" ', "")),
    )
    for original, replacement in mutations:
        changed = source.replace(original, replacement, 1)
        if changed == source:
            raise AssertionError("Source mutation anchor disappeared")
        try:
            check(changed)
        except AssertionError:
            continue
        raise AssertionError("Linkage workflow credential gate accepted an unsafe mutation")


if __name__ == "__main__":
    workflow = PATH.read_text(encoding="utf-8")
    check(workflow)
    self_test(workflow)
    print("LINKAGE WORKFLOW SQLCMD SECRET TRANSPORT: OK (2 probes, 2 negative mutations)")
