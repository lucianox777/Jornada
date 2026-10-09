#!/usr/bin/env python3
"""C3.2b: independent API process contract. Never starts dotnet, SQL or Docker."""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "install/container-test/api-entrypoint.sh"
COMMON = {
    "JORNADA_WORKER_ISOLATED_PROFILE": "true",
    "JORNADA_RUNTIME_MODE": "DEV",
    "DOTNET_ENVIRONMENT": "Development",
    "ASPNETCORE_ENVIRONMENT": "Development",
    "JORNADA_E2E_SQL_DATABASE": "JornadaE2E",
    "JORNADA_SQL_DATABASE_OVERRIDE": "JornadaE2E",
    "ConnectionStrings__Jornada":
        "Server=sqlserver,1433;Database=JornadaE2E;Integrated Security=true;Encrypt=true",
}
CASES = (
    ("Api", {"JORNADA_NODE_ID": "NODE1", "ASPNETCORE_URLS": "http://0.0.0.0:5080"}),
    ("ResultadoApi", {"JORNADA_NODE_ID": "NODE2",
                      "ASPNETCORE_URLS": "http://0.0.0.0:5081",
                      "JornadaApiBaseUrl": "http://api:5080"}),
)


def check(component: str, env: dict[str, str], *extra: str) -> subprocess.CompletedProcess[str]:
    safe = {"PATH": os.environ.get("PATH", ""), "HOME": os.environ.get("HOME", "/tmp")}
    safe.update(COMMON)
    safe.update(env)
    return subprocess.run(
        ["bash", str(SCRIPT), component, "--check", *extra],
        env=safe, text=True, capture_output=True, timeout=5, check=False,
    )


def require(condition: bool, label: str) -> None:
    if not condition:
        raise AssertionError(label)


def main() -> int:
    require(subprocess.run(["bash", "-n", str(SCRIPT)], check=False).returncode == 0,
            "API entrypoint has invalid Bash syntax")
    for component, extra in CASES:
        good = check(component, extra)
        require(good.returncode == 0 and f"component={component};" in good.stdout,
                f"{component}: fail-closed valid config rejected: {good.stderr}")
        for key, value in (
            ("JORNADA_WORKER_ISOLATED_PROFILE", "false"),
            ("JORNADA_RUNTIME_MODE", "PROD"),
            ("DOTNET_ENVIRONMENT", "Production"),
            ("ASPNETCORE_ENVIRONMENT", "Production"),
            ("JORNADA_E2E_SQL_DATABASE", "JornadaLocal"),
            ("JORNADA_SQL_DATABASE_OVERRIDE", "JornadaLocal"),
            ("ConnectionStrings__Jornada",
             "Server=sqlserver,1433;Database=JornadaLocal;Encrypt=true"),
            ("ConnectionStrings__Jornada",
             "Server=prod-db,1433;Database=JornadaE2E;Encrypt=true"),
            ("ConnectionStrings__Jornada",
             "Server=sqlserver,1433;Database=JornadaE2EExtra;Encrypt=true"),
            ("JORNADA_NODE_ID", "NODE3"),
            ("ASPNETCORE_URLS", "http://0.0.0.0:7000"),
        ):
            require(check(component, {**extra, key: value}).returncode != 0,
                    f"{component} must reject {key}={value!r}")
        if component == "ResultadoApi":
            require(check(component, {**extra, "JornadaApiBaseUrl": "http://prod-api:5080"}).returncode != 0,
                    "ResultadoApi cannot call outside project network")
    for bad in ("", "Processor", "Linkage", "../Api", "Api;echo invalid"):
        require(check(bad, CASES[0][1]).returncode != 0,
                f"Unallowlisted component admitted: {bad!r}")
    require(check("Api", CASES[0][1], "--not-allowed").returncode != 0,
            "Unrecognized argument admitted")
    print("API INDEPENDENT ENTRYPOINT: PASS "
          "(2 positive, 22 negative per-environment cases, "
          "1 internal URL, 5 invalid components; nothing started)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
