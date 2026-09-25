#!/usr/bin/env python3
"""Fail-closed contract for SQL credential transport in the two CI SQL jobs.

The integration and harness jobs themselves exercise real ephemeral SQL Server.
This regression test inspects their commands, never touches Docker or a database.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "ci.yml"
SQLCMD = "/opt/mssql-tools18/bin/sqlcmd"
RUN_ID = "$" + "{{ github.run_id }}"


def job_body(source: str, name: str) -> str:
    match = re.search(rf"(?m)^  {re.escape(name)}:\n", source)
    if match is None:
        raise AssertionError(f"Missing CI job: {name}")
    rest = source[match.end():]
    next_job = re.search(r"(?m)^  [a-z0-9-]+:\n", rest)
    return rest[: next_job.start()] if next_job else rest


def check_job(source: str, name: str, suffix: str, expected_calls: int) -> None:
    body = job_body(source, name)
    per_run = f"Jornada_{RUN_ID}!{suffix}"
    if (
        f"JORNADA_SQL_SA_PASSWORD: '{per_run}'" not in body
        or f"MSSQL_SA_PASSWORD: '{per_run}'" not in body
    ):
        raise AssertionError(f"{name}: Docker service and job must share a per-run password")

    health = [line.strip() for line in body.splitlines() if "--health-cmd" in line]
    if len(health) != 1 or (
        "SQLCMDPASSWORD=$MSSQL_SA_PASSWORD " + SQLCMD
    ) not in health[0] or "-P " in health[0]:
        raise AssertionError(f"{name}: readiness must use its own SQL environment")

    sql_lines = [
        line.strip()
        for line in body.splitlines()
        if "docker exec" in line and SQLCMD in line
    ]
    if len(sql_lines) != expected_calls:
        raise AssertionError(
            f"{name}: expected {expected_calls} real Docker/sqlcmd calls, got {len(sql_lines)}"
        )
    for line in sql_lines:
        if 'SQLCMDPASSWORD="$JORNADA_SQL_SA_PASSWORD" docker exec' not in line:
            raise AssertionError(f"{name}: temporary SQLCMDPASSWORD assignment missing")
        if not re.search(
            r'docker exec\b.*?\s-e SQLCMDPASSWORD "\$CID"\s+' + re.escape(SQLCMD),
            line,
        ):
            raise AssertionError(f"{name}: Docker must receive only -e SQLCMDPASSWORD")
        if "-P " in line or "-e SQLCMDPASSWORD=" in line:
            raise AssertionError(f"{name}: a SQL password was embedded in process argv")
    if any("-P " in line for line in body.splitlines()):
        raise AssertionError(f"{name}: no sqlcmd -P permitted within protected job")


def check(source: str) -> None:
    check_job(source, "integration-sql", "Sql", 6)
    check_job(source, "harness-smoke", "HarnessSql", 13)


def self_test(source: str) -> None:
    mutants = (
        (
            '-e SQLCMDPASSWORD "$CID"',
            '-e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" "$CID"',
        ),
        (
            "SQLCMDPASSWORD=$MSSQL_SA_PASSWORD " + SQLCMD,
            SQLCMD + ' -P "$MSSQL_SA_PASSWORD"',
        ),
        (
            "JORNADA_SQL_SA_PASSWORD: 'Jornada_" + RUN_ID + "!Sql'",
            "JORNADA_WRONG_SQL_ENV: 'Jornada_" + RUN_ID + "!Sql'",
        ),
    )
    for original, replacement in mutants:
        changed = source.replace(original, replacement, 1)
        if changed == source:
            raise AssertionError(f"Source mutation anchor disappeared: {original}")
        try:
            check(changed)
        except AssertionError:
            continue
        raise AssertionError("Credential checker accepted a deliberately unsafe CI mutation")


if __name__ == "__main__":
    source = WORKFLOW.read_text(encoding="utf-8")
    check(source)
    self_test(source)
    print("CI SQLCMD CREDENTIAL TRANSPORT: OK (19 SQL calls and 2 health checks)")
