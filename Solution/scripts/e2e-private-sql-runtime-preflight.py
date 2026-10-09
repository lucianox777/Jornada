#!/usr/bin/env python3
"""C3.2d preflight: fail-closed contract before any operational E2E.

No Docker state, SQL database or application process is touched by this check.
"""
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"
BOOTSTRAP = ROOT / "install/console-dev-e2e/sql-bootstrap/bootstrap.sh"

def main():
    # Deliberately reject any local invocation; operational CI is opt-in.
    assert os.environ.get("GITHUB_ACTIONS") == "true", "GitHub CI only"
    assert os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
    assert os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    assert re.fullmatch(r"[0-9]{6,16}", run), "unique run id required"
    assert re.fullmatch(r"[0-9]{1,3}", attempt), "run attempt required"
    assert COMPOSE.is_file() and BOOTSTRAP.is_file()
    text = COMPOSE.read_text(encoding="utf-8")
    bootstrap = BOOTSTRAP.read_text(encoding="utf-8")
    assert "jornada-workers-e2e-" in text
    assert "service_completed_successfully" in text
    assert "JornadaE2E" in text
    assert "JornadaLocal" not in bootstrap
    assert "CREATE DATABASE [JornadaE2E]" in bootstrap
    assert "DROP DATABASE" not in bootstrap
    assert "RESTORE DATABASE" not in bootstrap
    print("C3.2d: CI E2E preflight PASS; no runtime started")
    return 0

if __name__ == "__main__":
    sys.exit(main())
