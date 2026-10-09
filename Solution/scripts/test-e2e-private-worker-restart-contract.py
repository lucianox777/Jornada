#!/usr/bin/env python3
"""C3.2f1 negative contract tests; NEVER launch Docker or access SQL."""
from __future__ import annotations

import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/e2e-private-worker-restart-runtime.sh"

def deny(env: dict[str, str], *args: str) -> None:
    minimal_env = {"PATH": os.environ.get("PATH", ""), "HOME": "/tmp", **env}
    p = subprocess.run(["bash", str(SCRIPT), *args],
                       env=minimal_env, cwd=ROOT, text=True,
                       capture_output=True, timeout=5, check=False)
    assert p.returncode == 2 and not p.stdout, (p.returncode, p.stdout, p.stderr)

def main() -> None:
    subprocess.run(["bash", "-n", str(SCRIPT)], check=True)
    deny({})
    ci = {"GITHUB_ACTIONS": "true", "CI": "true",
          "GITHUB_REPOSITORY": "lucianox777/Jornada",
          "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
          "GITHUB_RUN_ID": "1234567", "GITHUB_RUN_ATTEMPT": "1",
          "JORNADA_WORKERS_E2E_ID": "ci12345671"}
    deny({**ci, "GITHUB_ACTIONS": "false"})
    deny({**ci, "GITHUB_REPOSITORY": "wrong/repository"})
    deny({**ci, "JORNADA_WORKERS_E2E_RUNTIME_TEST": "false"})
    deny({**ci, "JORNADA_WORKERS_E2E_ID": "contract123"})
    deny({**ci, "GITHUB_RUN_ID": "not-a-run"})
    deny(ci)  # no disposable SQL secret
    deny({**ci, "JORNADA_WORKERS_E2E_SQL_PASSWORD": "dummy", "JORNADA_WORKERS_E2E_IMAGE_TAG": "prod"})
    deny(ci, "--unsafe")

    code = SCRIPT.read_text(encoding="utf-8")
    assert "docker compose --env-file /dev/null --profile continuous" in code
    assert '[[ "${JORNADA_WORKERS_E2E_ID:-}" == "$EXPECTED_ID" ]]' in code
    for name in ("processor", "operations-maintenance", "bronze-maintenance"):
        assert name in code
    assert 'sudo kill -KILL -- "$before_pid"' in code
    assert '$(get_pid "$target_cid")" == "$before_pid"' in code
    assert "com.docker.compose.project" in code
    assert "com.docker.compose.service" in code
    assert "unless-stopped" in code
    assert "controle.runtime_componente" in code and "instance_id" in code
    assert "heartbeat_em" in code and "get_restart_count" in code
    assert "processing_recovery_verified\": False" in code
    forbidden = (r"(?m)^\s*(?:docker\s+)?(?:compose\s+)?(?:down|stop|kill|rm|prune)\b",
                 r"(?m)^\s*docker\s+(?:stop|kill|rm|volume\s+rm|system\s+prune)\b")
    assert not any(re.search(pattern, code) for pattern in forbidden)
    for term in ("JornadaLocal", "DROP DATABASE", "RESTORE DATABASE", "local-db.ps1", "local-cluster.ps1"):
        assert term not in code
    print("C3.2f1 contract: PASS (negative guards, 3 independent workers, no Docker run)")

if __name__ == "__main__":
    main()
