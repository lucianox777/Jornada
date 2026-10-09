#!/usr/bin/env python3
"""C3.2f2b sandbox preflight: no live SQL, Docker calls or user data reads."""
from __future__ import annotations
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/e2e-private-api-ingestion-baseline-runtime.sh"

def deny(env: dict[str, str], *arguments: str) -> None:
    p = subprocess.run(
        ["bash", str(SCRIPT), *arguments], cwd=ROOT,
        env={"PATH": os.environ.get("PATH", ""), "HOME": "/tmp", **env},
        capture_output=True, text=True, timeout=5, check=False)
    assert p.returncode == 2 and not p.stdout, (p.returncode, p.stderr, p.stdout)

def main() -> None:
    subprocess.run(["bash", "-n", str(SCRIPT)], check=True)
    deny({})
    base = {"GITHUB_ACTIONS": "true", "CI": "true",
            "GITHUB_REPOSITORY": "lucianox777/Jornada",
            "GITHUB_RUN_ID": "1234567", "GITHUB_RUN_ATTEMPT": "1",
            "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
            "JORNADA_WORKERS_E2E_ID": "ci12345671"}
    deny(base)  # no secret; refuse before Docker
    deny({**base, "GITHUB_ACTIONS": "false"})
    deny({**base, "GITHUB_REPOSITORY": "other/unknown"})
    deny({**base, "GITHUB_RUN_ID": "not-a-run"})
    deny({**base, "JORNADA_WORKERS_E2E_ID": "unauthorized"})
    deny({**base, "JORNADA_WORKERS_E2E_RUNTIME_TEST": "false"})
    deny({**base, "JORNADA_WORKERS_E2E_IMAGE_TAG": "prod",
          "JORNADA_WORKERS_E2E_SQL_PASSWORD": "synthetic"})
    deny(base, "--unsafe")
    script = SCRIPT.read_text(encoding="utf-8")
    assert "jornada-workers-e2e-$ID" in script
    assert "docker compose --env-file /dev/null --profile continuous" in script
    assert 'com.docker.compose.project' in script and 'com.docker.compose.service' in script
    assert "Database=JornadaE2E" not in script  # SQLCMD -d, not user DB string
    assert "-d JornadaE2E" in script
    assert '127.0.0.1:5080/api/v1/ingestao/entregas' in script
    assert '--data-binary @-' in script and '--fixture tests/fixtures/ingestao/AA01_v2' in script
    assert 'same_delivery_on_http_idempotency_replay' in script
    assert 'identical_counts_after_http_replay' in script
    assert "'processing_recovery_verified':False" in script
    for forbidden in ("JornadaLocal", "DROP DATABASE", "RESTORE DATABASE",
                      "local-db.sh", "local-db.ps1", "local-cluster"):
        assert forbidden not in script
    for command in (r"(?m)^\s*docker\s+(?:stop|kill|rm|prune)\b",
                    r"(?m)^\s*docker\s+compose\s+down\b"):
        assert re.search(command, script) is None
    print("C3.2f2b: PASS private API/SQL ZIP contract (offline, no Docker)")

if __name__ == "__main__":
    main()
