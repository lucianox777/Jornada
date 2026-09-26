#!/usr/bin/env python3
"""Prevent reintroduction of SQL secrets in process arguments (#405).

This is a narrow static guard, complementary to Gitleaks and source-sanity.
It scans checked-in executable shell/PowerShell entrypoints and Actions, not
documentation or intentional malicious test fixtures. It never prints secrets.
"""
from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SQLCMD = re.compile(r"(?i)(?:^|[/\\\s])sqlcmd(?:\.exe)?(?:\b|$)")
P_FLAG = re.compile(r"(?:^|[\s,])['\"]?-P(?:['\"])?(?=[\s,]|$)")
# Covers shell, YAML and PowerShell array-argument forms.
DOCKER_ASSIGNMENT = re.compile(
    r"(?i)\bexec\b.{0,250}?"
    r"(?:\s-e\s+|['\"]-e['\"]\s*,\s*)"
    r"['\"]?SQLCMDPASSWORD\s*="
)


def eligible(path: str) -> bool:
    if path.startswith(".github/workflows/") and path.endswith((".yml", ".yaml")):
        return True
    if not path.startswith("Solution/scripts/") or not path.endswith((".sh", ".ps1")):
        return False
    name = Path(path).name.lower()
    return not (name.startswith("test") or name.startswith("local-test-"))


def problems(path: str, source: str) -> list[str]:
    errors: list[str] = []
    lines = source.splitlines()
    for index, line in enumerate(lines):
        stripped = line.lstrip()
        if stripped.startswith(("#", "//")):
            continue
        # Look only at invocation windows, including backslash/backtick continuations.
        window = "\n".join(lines[index:index + 4])
        if SQLCMD.search(line):
            # Avoid false positives on prose and source-code assertions in workflows.
            if (
                "run:" not in stripped
                and not stripped.startswith(("echo ", "Write-Host ", "Write-CommandLine ", "if ", "grep "))
                and P_FLAG.search(window)
            ):
                # Only a command continuation can carry flags on subsequent lines.
                first = line.rstrip()
                if P_FLAG.search(line) or first.endswith(("\\", "`")):
                    errors.append(f"{path}:{index + 1}: sqlcmd accepts -P in process argv")
        if DOCKER_ASSIGNMENT.search(line):
            errors.append(f"{path}:{index + 1}: Docker receives SQLCMDPASSWORD=<value> in argv")
    return sorted(set(errors))


def self_test() -> None:
    safe = (
        'SQLCMDPASSWORD="$secret" docker compose exec -T -e SQLCMDPASSWORD '
        'sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C'
    )
    assert not problems("Solution/scripts/probe.sh", safe)
    bad_shell = (
        'docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \\\n'
        '  -S localhost -U sa -P "$secret" -C'
    )
    assert problems("Solution/scripts/probe.sh", bad_shell)
    bad_pwsh = (
        "& docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd `\n"
        "  -S localhost -U sa -P $password -C"
    )
    assert problems("Solution/scripts/probe.ps1", bad_pwsh)
    bad_docker = (
        'docker compose exec -T -e "SQLCMDPASSWORD=$secret" '
        'sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost'
    )
    assert problems("Solution/scripts/probe.sh", bad_docker)
    bad_array = (
        "@('compose','exec','-T','-e',\"SQLCMDPASSWORD=$password\","
        "'sqlserver','/opt/mssql-tools18/bin/sqlcmd')"
    )
    assert problems("Solution/scripts/probe.ps1", bad_array)
    assert eligible(".github/workflows/ci.yml")
    assert not eligible("Solution/scripts/test-secret-transport.sh")


def scan() -> list[str]:
    files = subprocess.check_output(
        ["git", "-C", str(ROOT), "ls-files"], text=True
    ).splitlines()
    violations: list[str] = []
    for path in files:
        if not eligible(path):
            continue
        actual = ROOT / path
        if actual.is_file():
            violations.extend(problems(path, actual.read_text(encoding="utf-8-sig")))
    return violations


if __name__ == "__main__":
    self_test()
    found = scan()
    if found:
        raise SystemExit("SQL ARGV SAFETY GATE: FAIL\n" + "\n".join(found))
    print("SQL ARGV SAFETY GATE: OK (entrypoints and Actions; malicious self-tests rejected)")
