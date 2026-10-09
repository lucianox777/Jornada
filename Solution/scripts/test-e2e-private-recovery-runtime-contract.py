#!/usr/bin/env python3
"""Offline C3.2f2c guards. Never run Docker, SQL, or mutate an E2E fixture."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / "scripts/e2e-private-recovery-runtime.py"
FIXTURE = ROOT / "scripts/e2e-private-recovery-fixture.py"
SQL = ROOT / "scripts/e2e-private-recovery-transaction-hold.sql"
COMPOSE = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"


def deny(path: Path, env: dict[str, str], *args: str) -> None:
    result = subprocess.run(
        [sys.executable, str(path), *args],
        cwd=ROOT, env={"PATH": os.environ.get("PATH", ""), "HOME": "/tmp", **env},
        capture_output=True, text=True, timeout=5, check=False)
    assert result.returncode != 0 and not result.stdout, (
        result.returncode, result.stdout, result.stderr)


def main() -> None:
    for path in (RUNTIME, FIXTURE):
        compile(path.read_text(encoding="utf-8"), str(path), "exec")
        deny(path, {})
        deny(path, {"GITHUB_ACTIONS": "true", "CI": "true",
                    "GITHUB_REPOSITORY": "wrong/repository"})
        deny(path, {"GITHUB_ACTIONS": "true", "CI": "true",
                    "GITHUB_REPOSITORY": "lucianox777/Jornada",
                    "GITHUB_RUN_ID": "1234567", "GITHUB_RUN_ATTEMPT": "1",
                    "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
                    "JORNADA_WORKERS_E2E_ID": "wrong"})

    source = RUNTIME.read_text(encoding="utf-8")
    fixture_source = FIXTURE.read_text(encoding="utf-8")
    barrier = SQL.read_text(encoding="utf-8")
    compose = COMPOSE.read_text(encoding="utf-8")
    assert "jornada-workers-e2e-ci" in source
    assert "JORNADA_WORKERS_E2E_ID" in source
    assert "jornada-workers-e2e-ci" not in fixture_source or "JORNADA_WORKERS_E2E_ID" in fixture_source
    assert "JornadaE2E" in source and "Jornada.EnvironmentProfile" in source
    assert "PROCESSANDO" in source and "recuperacao_count" in source
    assert "READ UNCOMMITTED" in source
    assert "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao" in source
    assert "ci_c3_2f2c_requeue_events" in source
    assert "old_heartbeat_rows" in source and "expired lease" in source
    assert "real-recovery-evidence.json" in source
    assert "scripts/e2e-lot-recovery-evidence-gate.py" in source
    assert '"sql_provenance": {' in source
    assert "scripts/build-ingestion-fixture.py" in source
    assert "stdout=subprocess.PIPE" in source
    assert "duplicate_business_keys" in source
    assert "gold.beneficio_concedido" in source
    assert "serving.registro_integrado" in source
    assert "docker" in source and "com.docker.compose.project" in source
    assert "RestartCount" in source and "old_worker_exit_code" in source
    assert "SIGKILL" in source and "sudo" in source
    assert "Processor__LeaseDurationSeconds: \"35\"" in compose
    assert "Processor__HeartbeatSeconds: \"5\"" in compose
    assert "Processor__RecoveryScanSeconds: \"5\"" in compose
    assert "ON identidade.vinculo_fonte\nAFTER INSERT" in barrier
    assert "ON silver.pessoa_observacao\nAFTER INSERT" not in barrier
    assert "ON ingestao.lote\nAFTER UPDATE" in barrier
    assert "INSERT dbo.ci_c3_2f2c_requeue_events" in barrier

    for text in (source, fixture_source):
        for word in ("JornadaLocal", "DROP DATABASE", "RESTORE DATABASE",
                     "local-db.sh", "local-cluster.sh", "docker system prune"):
            assert word not in text
    # Synthetic CPF validates the official digit arithmetic but is explicitly
    # generated from CI run identity; no original record or IBGE file read.
    spec = importlib.util.spec_from_file_location("recovery_fixture", FIXTURE)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    cpf1 = module.synthetic_cpf("CI-REC-1234567-1")
    cpf2 = module.synthetic_cpf("CI-REC-1234567-2")
    assert re.fullmatch(r"[0-9]{11}", cpf1)
    assert cpf1 != cpf2 and len(set(cpf1)) > 1
    for cpf in (cpf1, cpf2):
        digits = list(map(int, cpf))
        for count in (9, 10):
            rem = (sum(d * (count + 1 - i) for i, d in enumerate(digits[:count]))
                   * 10) % 11
            assert digits[count] == (0 if rem == 10 else rem)
    print("C3.2f2c static contract PASS (no Docker, SQL, user data or real runtime)")


if __name__ == "__main__":
    main()
