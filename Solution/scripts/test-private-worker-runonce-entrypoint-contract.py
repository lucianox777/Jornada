#!/usr/bin/env python3
"""C3.3b2 prep: no Docker, DB, or worker binaries are invoked."""
from pathlib import Path
import os
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ENTRY = ROOT / "install/container-test/worker-entrypoint.sh"


def attempt(worker: str, mode: str, overrides: dict[str, str]) -> subprocess.CompletedProcess:
    env = {
        "PATH": os.environ.get("PATH", ""),
        "JORNADA_WORKER_ISOLATED_PROFILE": "true",
        "JORNADA_RUNTIME_MODE": "DEV",
        "DOTNET_ENVIRONMENT": "Development",
        "JORNADA_E2E_SQL_DATABASE": "JornadaE2E",
        "JORNADA_SQL_DATABASE_OVERRIDE": "JornadaE2E",
        "JORNADA_NODE_ID": "NODE2",
        "ConnectionStrings__Jornada":
            "Server=sqlserver,1433;Database=JornadaE2E;User Id=sa;Password=test;Encrypt=true",
        **overrides,
    }
    return subprocess.run(["bash", str(ENTRY), worker, mode],
                          capture_output=True, text=True, cwd=ROOT,
                          env=env, timeout=5, check=False)


def main() -> None:
    subprocess.run(["bash", "-n", str(ENTRY)], check=True)
    source = ENTRY.read_text(encoding="utf-8")
    for name, runonce in (("Processor", "Processor__RunOnce"),
                          ("OperationsMaintenance", "MaintenanceExecution__RunOnce"),
                          ("BronzeMaintenance", "BronzeMaintenance__RunOnce")):
        check = attempt(name, "--check", {})
        assert check.returncode == 0 and "mode=CONTINUOUS" in check.stdout
        for guards in (
            {},
            {"JORNADA_WORKERS_E2E_RUN_ONCE": "true"},
            {"JORNADA_WORKERS_E2E_RUN_ONCE": "true",
             "JORNADA_WORKERS_E2E_ID": "ci12345671"},
            {"JORNADA_WORKERS_E2E_RUN_ONCE": "true",
             "JORNADA_WORKERS_E2E_ID": "ci12345671",
             "JORNADA_WORKERS_E2E_RUN_ONCE_ALLOWED": "false"},
            {"JORNADA_WORKERS_E2E_RUN_ONCE": "true",
             "JORNADA_WORKERS_E2E_ID": "jornada-local",
             "JORNADA_WORKERS_E2E_RUN_ONCE_ALLOWED": "true"},
        ):
            rejected = attempt(name, "--run-once", guards)
            assert rejected.returncode == 2 and not rejected.stdout
            assert "RunOnce só é autorizado" in rejected.stderr
        assert f'export {runonce}=$(' in source
    bad = attempt("OtherWorker", "--check", {})
    assert bad.returncode == 2
    invalid = attempt("Processor", "--restart", {})
    assert invalid.returncode == 2
    assert "--run-once" in source and "RUN_ONCE;restart=none" in source
    assert "JORNADA_WORKERS_E2E_RUN_ONCE_ALLOWED" in source
    assert re.search(r"\^ci\[0-9\]\{7,19\}\$", source)
    assert "JornadaLocal" not in source
    assert "exec dotnet" in source and "wait -n" not in source
    print("C3.3b2 prep: PASS finite worker entrypoint negative contract (no Docker or SQL)")


if __name__ == "__main__":
    main()
