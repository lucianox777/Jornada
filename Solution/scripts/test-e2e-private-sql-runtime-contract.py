#!/usr/bin/env python3
"""C3.2d unit gate: negative CI guards, with NO Docker, DB or network calls."""
from __future__ import annotations
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/e2e-private-sql-bootstrap-runtime.sh"

def denied(env: dict[str, str], *args: str) -> None:
    minimal = {"PATH": os.environ.get("PATH", ""), "HOME": "/tmp", **env}
    result = subprocess.run(["bash", str(SCRIPT), *args],
                            cwd=ROOT, env=minimal, capture_output=True,
                            text=True, check=False, timeout=5)
    assert result.returncode == 2, (result.returncode, result.stdout, result.stderr)
    assert not result.stdout, result.stdout

def main() -> None:
    subprocess.run(["bash", "-n", str(SCRIPT)], check=True)
    denied({})
    base = {"GITHUB_ACTIONS": "true", "CI": "true",
            "GITHUB_REPOSITORY": "lucianox777/Jornada"}
    denied(base)
    denied({**base, "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true"})
    denied({**base, "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
            "GITHUB_RUN_ID": "123456"})
    denied({**base, "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
            "GITHUB_RUN_ID": "123456", "GITHUB_RUN_ATTEMPT": "0" * 4})
    denied({**base, "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
            "GITHUB_RUN_ID": "123456", "GITHUB_RUN_ATTEMPT": "1"}, "--unsafe")
    denied({**base, "GITHUB_REPOSITORY": "someone/else",
            "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
            "GITHUB_RUN_ID": "123456", "GITHUB_RUN_ATTEMPT": "1"})
    content = SCRIPT.read_text(encoding="utf-8")
    assert "jornada-workers-e2e-" in content
    assert "e2e-private-sql-runtime-preflight.py" in content
    assert "--env-file /dev/null -p" in content
    assert "sql-bootstrap" in content and "JornadaE2E" in content
    # API/Resultado smoke runs ONLY after SQL bootstrap succeeded, still
    # inside the exclusive project with workers absent in OFF mode.
    assert "exited:0)" in content and '[[ "$state" == exited:0 ]]' in content  # check one-shot exit=0
    assert "compose build api" in content and "compose up -d --no-build --no-deps api resultado-api" in content
    assert "http://127.0.0.1:5080/health/ready" in content
    assert "http://127.0.0.1:5081/health" in content
    assert "http://api:5080/health/ready" in content
    assert "resultado_to_api_private_dns" in content
    assert "worker_residents':0" in content
    assert "jornada-local" not in content
    for unsafe in ("local-db.ps1", "local-db.sh", "docker compose down",
                   "compose down", "DROP DATABASE", "RESTORE DATABASE"):
        assert unsafe not in content, unsafe
    print("C3.2d CI-only runtime contract PASS (negative paths, no Docker/SQL)")

if __name__ == "__main__":
    main()
