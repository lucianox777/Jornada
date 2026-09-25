#!/usr/bin/env python3
"""Source-only contract for the isolated schema 3.70 CI SQL commands."""
from __future__ import annotations

import re
from pathlib import Path

WORKFLOW = Path(__file__).resolve().parents[2] / ".github/workflows/schema-consolidation-370.yml"
CALL = re.compile(
    r'(?:^|\$\()\s*SQLCMDPASSWORD="\$(JORNADA_TEST_SQL_PASSWORD|JORNADA_SQL_PASSWORD)" '
    r'(?:/opt/mssql-tools18/bin/)?sqlcmd -S localhost -U sa'
)


def check(source: str) -> None:
    if " -P " in source or "-e SQLCMDPASSWORD=" in source:
        raise AssertionError("Schema workflow must not embed SQL credentials in argv")
    if "JORNADA_TEST_SQL_PASSWORD: Jornada_" not in source:
        raise AssertionError("Missing ephemeral SQL password")
    if source.count('export JORNADA_SQL_PASSWORD="$JORNADA_TEST_SQL_PASSWORD"') != 2:
        raise AssertionError("Upgrade and ledger steps must retain their migration alias")
    commands = [
        line.strip() for line in source.splitlines()
        if "SQLCMDPASSWORD=" in line and "sqlcmd -S " in line
    ]
    if len(commands) != 19:
        raise AssertionError(f"Expected 19 actual SQL commands, got {len(commands)}")
    counts = {"JORNADA_TEST_SQL_PASSWORD": 0, "JORNADA_SQL_PASSWORD": 0}
    for line in commands:
        match = CALL.search(line)
        if not match:
            raise AssertionError(f"SQL secret not scoped to child process: {line}")
        if "-C" not in line:
            raise AssertionError("Missing SQL trust option")
        counts[match.group(1)] += 1
    if counts != {"JORNADA_TEST_SQL_PASSWORD": 12, "JORNADA_SQL_PASSWORD": 7}:
        raise AssertionError(f"Unexpected credential distribution: {counts}")
    for required in (
        "database/Jornada_Dev_DdlFingerprint.sql",
        "database/baselines/Jornada_Fase1_v3.65.sql",
        "scripts/apply-migrations.sh",
        "checksum divergente para migração já aplicada",
    ):
        if required not in source:
            raise AssertionError(f"Lost schema-equivalence gate: {required}")


def self_test(source: str) -> None:
    mutations = (
        (
            'SQLCMDPASSWORD="$JORNADA_TEST_SQL_PASSWORD" /opt/mssql-tools18/bin/sqlcmd ',
            '/opt/mssql-tools18/bin/sqlcmd -P "$JORNADA_TEST_SQL_PASSWORD" ',
        ),
        ('SQLCMDPASSWORD="$JORNADA_SQL_PASSWORD" sqlcmd ', 'sqlcmd '),
        (
            'export JORNADA_SQL_PASSWORD="$JORNADA_TEST_SQL_PASSWORD"',
            'export JORNADA_SQL_WRONG_PASSWORD="$JORNADA_TEST_SQL_PASSWORD"',
        ),
    )
    for original, replacement in mutations:
        changed = source.replace(original, replacement, 1)
        if changed == source:
            raise AssertionError(f"Source mutation anchor absent: {original}")
        try:
            check(changed)
        except AssertionError:
            continue
        raise AssertionError("Schema credential gate accepted an insecure mutation")


if __name__ == "__main__":
    content = WORKFLOW.read_text(encoding="utf-8")
    check(content)
    self_test(content)
    print("SCHEMA CI SQLCMD CREDENTIAL TRANSPORT: OK (19 calls, 3 negative mutations)")
