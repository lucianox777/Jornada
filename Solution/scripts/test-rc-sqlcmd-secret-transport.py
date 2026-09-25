#!/usr/bin/env python3
"""Verify RC sqlcmd secret transport without running the tag-only RC workflow.

The complete RC evidence workflow remains an explicit tag/dispatch gate.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "ci.yml"
RC_PASSWORD = "$JORNADA_RC_SQL_PASSWORD"
PREFIX = 'SQLCMDPASSWORD="' + RC_PASSWORD + '" '


def rc_body(source: str) -> str:
    match = re.search(r"(?m)^  rc-evidence:\n", source)
    if not match:
        raise AssertionError("Missing RC evidence workflow job")
    rest = source[match.end():]
    next_job = re.search(r"(?m)^  [a-z0-9-]+:\n", rest)
    return rest[:next_job.start()] if next_job else rest


def check(source: str) -> None:
    body = rc_body(source)
    if "JORNADA_RC_SQL_PASSWORD: Jornada_" not in body:
        raise AssertionError("Missing run-scoped RC SQL password")
    if "-P " in body:
        raise AssertionError("RC sqlcmd must never receive a password argument")
    invocations = [
        line.strip()
        for line in body.splitlines()
        if RC_PASSWORD in line and "sqlcmd " in line
    ]
    if len(invocations) != 4:
        raise AssertionError(f"Expected 4 RC sqlcmd calls, got {len(invocations)}")
    binaries = ("/opt/mssql-tools18/bin/sqlcmd", "sqlcmd")
    for line in invocations:
        if not any(line.startswith(PREFIX + binary + " -S localhost -U sa ")
                   for binary in binaries):
            raise AssertionError(f"RC sqlcmd must scope SQLCMDPASSWORD in the child env: {line}")
        if "-C" not in line:
            raise AssertionError("RC SQL trust option must be preserved")
    if "database/Jornada_Dev_DdlFingerprint.sql" not in body:
        raise AssertionError("RC structural fingerprint verification disappeared")
    if "exit 1" not in body or "for i in $(seq 1 60)" not in body:
        raise AssertionError("Fail-closed RC readiness retry loop disappeared")


def self_test(source: str) -> None:
    mutations = (
        (PREFIX + "/opt/mssql-tools18/bin/sqlcmd ",
         '/opt/mssql-tools18/bin/sqlcmd -P "' + RC_PASSWORD + '" '),
        (PREFIX + "sqlcmd ", "sqlcmd "),
        ("JORNADA_RC_SQL_PASSWORD: Jornada_", "RC_SQL_WRONG_NAME: Jornada_"),
    )
    base = rc_body(source)
    for original, replacement in mutations:
        mutated = base.replace(original, replacement, 1)
        if mutated == base:
            raise AssertionError(f"RC test mutation anchor not found: {original}")
        mutated_workflow = source.replace(base, mutated, 1)
        try:
            check(mutated_workflow)
        except AssertionError:
            continue
        raise AssertionError("RC credential gate accepted a deliberately unsafe mutation")


if __name__ == "__main__":
    source = WORKFLOW.read_text(encoding="utf-8")
    check(source)
    self_test(source)
    print("RC SQLCMD CREDENTIAL TRANSPORT CONTRACT: OK (4 calls, 3 negative mutations)")
