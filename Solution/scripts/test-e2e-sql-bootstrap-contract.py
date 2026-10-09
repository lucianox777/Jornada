#!/usr/bin/env python3
"""Fail-closed, non-destructive contract tests: no database/container started."""
from __future__ import annotations

import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "install/console-dev-e2e/sql-bootstrap/bootstrap.sh"
DOCKERFILE = ROOT / "install/console-dev-e2e/sql-bootstrap/Dockerfile"
BASELINE = ROOT / "database/Jornada_Fase1_v3.70.sql"
VALID = {
    "JORNADA_WORKERS_E2E_SQL_BOOTSTRAP": "true",
    "JORNADA_RUNTIME_MODE": "DEV",
    "DOTNET_ENVIRONMENT": "Development",
    "JORNADA_E2E_SQL_DATABASE": "JornadaE2E",
    "JORNADA_SQL_DATABASE_OVERRIDE": "JornadaE2E",
    "JORNADA_WORKERS_E2E_ID": "contract123",
    "JORNADA_WORKERS_E2E_SQL_HOST": "sqlserver",
    "SQLCMDPASSWORD": "mock_" + "not-a-real-secret",
}


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise AssertionError(reason)


def check(env: dict[str, str], args: list[str] | None = None) -> subprocess.CompletedProcess[str]:
    isolated_env = {
        "PATH": os.environ.get("PATH", ""),
        "HOME": os.environ.get("HOME", "/tmp"),
        **env,
    }
    return subprocess.run(
        ["bash", str(SCRIPT), *(args or ["--check"])],
        cwd=ROOT, env=isolated_env, text=True, capture_output=True,
        timeout=5, check=False,
    )


def main() -> int:
    subprocess.run(["bash", "-n", str(SCRIPT)], check=True)
    ok = check(VALID)
    require(ok.returncode == 0 and
            "bootstrap=JornadaE2E;host=sqlserver;mode=create-only" in ok.stdout,
            "private create-only bootstrap validation rejected")

    cases = [
        ({}, "no opt-in"),
        ({**VALID, "JORNADA_WORKERS_E2E_SQL_BOOTSTRAP": "false"}, "disabled"),
        ({**VALID, "JORNADA_RUNTIME_MODE": "PROD"}, "production"),
        ({**VALID, "DOTNET_ENVIRONMENT": "Production"}, "dotnet production"),
        ({**VALID, "JORNADA_E2E_SQL_DATABASE": "JornadaLocal"}, "local database"),
        ({**VALID, "JORNADA_SQL_DATABASE_OVERRIDE": "JornadaLocal"}, "local override"),
        ({**VALID, "JORNADA_WORKERS_E2E_ID": "x"}, "short sandbox id"),
        ({**VALID, "JORNADA_WORKERS_E2E_SQL_HOST": "localhost"}, "non-private SQL host"),
        ({**VALID, "SQLCMDPASSWORD": ""}, "missing SQL secret"),
    ]
    for env, label in cases:
        result = check(env)
        require(result.returncode != 0 and not result.stdout,
                "Fail-closed guard bypass: " + label)
    require(check(VALID, ["--unsafe"]).returncode != 0,
            "Unexpected bootstrap command allowed")

    docker = DOCKERFILE.read_text(encoding="utf-8")
    script = SCRIPT.read_text(encoding="utf-8")
    baseline = BASELINE.read_text(encoding="utf-8")
    require("COPY database/ " not in docker and
            "COPY database/migrations/" in docker and
            "COPY database/Jornada_Seed_Dev.sql" in docker,
            "Bootstrap image must copy only required SQL sources")
    includes = re.findall(r"^:r\s+(\S+)\s*$", baseline, re.MULTILINE)
    require(bool(includes) and
            all((ROOT / p).is_file() for p in includes),
            "Canonical schema manifest references missing source files")
    require('existing" == "0"' in script and "CREATE DATABASE [JornadaE2E]" in script,
            "Create-only bootstrap must refuse pre-existing database")
    forbidden = ("DROP DATABASE", "RESTORE DATABASE", "ALTER DATABASE [JornadaLocal]",
                 "docker compose down", "docker compose stop", "docker compose up")
    require(not any(term in script for term in forbidden),
            "Bootstrap script attempts an unsafe state transition")
    print("E2E SQL BOOTSTRAP: PASS (create-only, private host, allowlist, "
          "baseline paths; no SQL/Docker process started)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
