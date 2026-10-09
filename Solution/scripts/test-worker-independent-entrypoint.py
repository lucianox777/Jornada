#!/usr/bin/env python3
"""C3.1: non-destructive contract tests for an independent worker entrypoint.

Only --check mode. No docker, no dotnet process, no SQL and no filesystem reset.
"""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
ENTRYPOINT = ROOT / "install/container-test/worker-entrypoint.sh"
VALID = {
    "JORNADA_WORKER_ISOLATED_PROFILE": "true",
    "JORNADA_RUNTIME_MODE": "DEV",
    "DOTNET_ENVIRONMENT": "Development",
    "JORNADA_E2E_SQL_DATABASE": "JornadaE2E",
    "JORNADA_SQL_DATABASE_OVERRIDE": "JornadaE2E",
    "JORNADA_NODE_ID": "NODE2",
    "ConnectionStrings__Jornada": (
        "Server=localhost,1433;Database=JornadaE2E;"
        "Integrated Security=true;Encrypt=true"
    ),
}


def command(env: dict[str, str], worker: str = "Processor",
            switch: str = "--check") -> subprocess.CompletedProcess[str]:
    isolated_env = {"PATH": os.environ["PATH"], "HOME": os.environ.get("HOME", "/tmp")}
    isolated_env.update(env)
    return subprocess.run(
        ["bash", str(ENTRYPOINT), worker, switch],
        cwd=ROOT, env=isolated_env, capture_output=True,
        text=True, timeout=5, check=False,
    )


def require(ok: bool, message: str) -> None:
    if not ok:
        raise AssertionError(message)


def main() -> int:
    syntax = subprocess.run(["bash", "-n", str(ENTRYPOINT)], check=False)
    require(syntax.returncode == 0, "worker entrypoint Bash syntax invalid")
    valid_names = ("Processor", "OperationsMaintenance", "BronzeMaintenance")
    for name in valid_names:
        check = command(VALID, name)
        require(check.returncode == 0
                and "worker=" + name in check.stdout
                and "mode=CONTINUOUS" in check.stdout
                and "restart=external" in check.stdout,
                f"{name} cannot validate its independent process boundary: {check.stderr}")
    deny_cases = [
        ({}, "missing all authorization"),
        ({**VALID, "JORNADA_WORKER_ISOLATED_PROFILE": "false"}, "missing opt-in"),
        ({**VALID, "JORNADA_RUNTIME_MODE": "HML"}, "HML denied"),
        ({**VALID, "JORNADA_RUNTIME_MODE": "PROD"}, "PROD denied"),
        ({**VALID, "DOTNET_ENVIRONMENT": "Production"}, "production env denied"),
        ({**VALID, "JORNADA_SQL_DATABASE_OVERRIDE": "JornadaLocal"}, "local override denied"),
        ({**VALID, "JORNADA_E2E_SQL_DATABASE": "JornadaLocal"}, "local E2E label denied"),
        ({**VALID, "JORNADA_NODE_ID": "NODE3"}, "unrecognized NODE denied"),
        ({**VALID, "ConnectionStrings__Jornada": (
            "Server=localhost;Database=JornadaLocal;Integrated Security=true;Encrypt=true"
        )}, "actual local DB denied"),
        ({**VALID, "ConnectionStrings__Jornada": (
            "Server=localhost;Database=JornadaE2EExtra;Integrated Security=true;Encrypt=true"
        )}, "prefix-matching fake DB denied"),
    ]
    for environment, label in deny_cases:
        result = command(environment)
        require(result.returncode != 0 and not result.stdout,
                f"Fail-closed bypass: {label}")
    for name in ("Api", "Linkage", "../Processor", "Processor;echo fake", ""):
        require(command(VALID, name).returncode != 0,
                f"Non-allowlisted executable admitted: {name!r}")
    require(command(VALID, switch="--unsafe").returncode != 0,
            "Unsafe launch option admitted")
    print("WORKER INDEPENDENT ENTRYPOINT: PASS "
          "(3 allowlisted checks, 10 fail-closed guards, 5 invalid workers, "
          "no processes or SQL started)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
