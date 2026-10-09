#!/usr/bin/env python3
"""C3.3a static/fail-closed contract. Do NOT start Console, Docker or SQL."""
from pathlib import Path
import os
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SERVICE = ROOT / "src/Jornada.DevConsole/IsolatedWorkerSupervisorStatusReader.cs"
API = ROOT / "src/Jornada.DevConsole/Program.cs"
E2E = ROOT / "scripts/e2e-console-worker-supervisor-status.py"
BOOTSTRAP = ROOT / "scripts/e2e-private-sql-bootstrap-runtime.sh"


def main() -> None:
    code = SERVICE.read_text(encoding="utf-8")
    api = API.read_text(encoding="utf-8")
    assert 'sealed class IsolatedWorkerSupervisorStatusReader' in code
    assert 'IsolatedWorkerSupervisorStatus' in code
    assert 'ToggleAvailable' in code and 'false, state' in code
    for guard in (
        'mode.Mode == "DEV"', 'GITHUB_ACTIONS', 'CI',
        'GITHUB_REPOSITORY', 'lucianox777/Jornada',
        'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT',
        'JORNADA_WORKERS_E2E_RUNTIME_TEST', 'JORNADA_WORKERS_E2E_ID',
        'JORNADA_WORKERS_E2E_SQL_PASSWORD',
        'JORNADA_WORKERS_E2E_IMAGE_TAG', 'DOCKER_HOST', 'DOCKER_CONTEXT'
    ):
        assert guard in code, guard
    for worker in ("processor", "operations-maintenance", "bronze-maintenance"):
        assert f'("{worker}",' in code, worker
    assert 'jornada-workers-e2e-' in code
    assert 'com.docker.compose.project' in code
    assert 'com.docker.compose.service' in code
    assert 'known["sqlserver"]' in code
    assert 'known["api"]' in code and 'known["resultado-api"]' in code
    assert 'api?.Healthy != true' in code and 'sql?.Healthy != true' in code
    assert '0.0.0.0' not in code
    assert 'http://127.0.0.1:5080/health/ready' in code
    assert 'http://127.0.0.1:5081/health' in code
    assert 'controle.runtime_componente' in code
    assert "status" in code and "heartbeat_em" in code
    assert 'Age: >= 0 and <= 45' in code
    assert 'worker.RestartPolicy == "unless-stopped"' in code
    assert 'worker.HostPid > 1' in code
    assert 'active == Workers.Length ? "ON"' in code
    assert '!existsRunning && !hasRestarting ? "OFF"' in code
    assert 'hasRestarting ? "REINICIANDO"' in code
    assert ':"ERRO"' not in code or 'ERRO' in code
    assert 'return new IsolatedWorkerSupervisorStatus(' in code
    assert 'sqlPassword' in code and 'start.Environment["SQLCMDPASSWORD"]' in code
    assert 'errors = process.StandardError.ReadToEndAsync' in code
    assert '"-d", "JornadaE2E"' in code
    assert '"-l", "7"' in code
    # No shell invocation, no child container creation, no mutations on host.
    assert 'UseShellExecute = false' in code
    for disallowed in ('"stop"', '"kill"', '"down"', '"prune"', '"rm"',
                       '"up"', 'JornadaLocal', 'DROP DATABASE', 'docker-compose.yml'):
        assert disallowed not in code, disallowed
    assert 'app.MapGet("/api/workers/supervisor"' in api
    assert 'builder.Services.AddSingleton<IsolatedWorkerSupervisorStatusReader>()' in api
    assert 'if(!supervisor.Enabled(runtime))' in api
    assert 'Results.Conflict' in api and 'toggleAvailable=false' in api
    assert 'mode="ERRO",toggleAvailable=false' in api
    assert 'Status503ServiceUnavailable' in api
    assert 'response.Headers.CacheControl="no-store"' in api
    assert 'app.MapPost("/api/workers/supervisor"' not in api
    assert not re.search(r'app\.Map(?:Put|Delete|Post)\("/api/workers/', api)
    e2e = E2E.read_text(encoding="utf-8")
    compile(e2e, str(E2E), "exec")
    assert 'proc.terminate()' in e2e and 'proc.kill()' in e2e
    assert '"/api/workers/supervisor"' in e2e
    assert '"OFF", "ON"' in e2e
    assert 'ASSEMBLY.is_file()' in e2e
    assert 'subprocess.Popen(' in e2e and '"dotnet"' in e2e
    assert '127.0.0.1' in e2e and "0.0.0.0" not in e2e
    assert 'subprocess.run(["docker"' not in e2e
    assert 'subprocess.Popen(["docker"' not in e2e
    assert 'python3 scripts/e2e-console-worker-supervisor-status.py OFF' in BOOTSTRAP.read_text()
    assert 'python3 scripts/e2e-console-worker-supervisor-status.py ON' in BOOTSTRAP.read_text()
    base = {"PATH": os.environ.get("PATH", ""), "HOME": "/tmp"}
    for args, env in [(("ON",), base), (("OFF",), base),
                      (("UNKNOWN",), base), ((), base),
                      (("ON",), {**base, "GITHUB_ACTIONS": "true", "CI": "true",
                                 "GITHUB_REPOSITORY": "wrong/repository"})]:
        res = subprocess.run([sys.executable, str(E2E), *args],
                             cwd=ROOT, env=env, capture_output=True,
                             text=True, timeout=5, check=False)
        assert res.returncode == 2 and not res.stdout
    print("C3.3a: PASS read-only guarded status + CI-only HTTP E2E negative contract (no Docker or SQL)")


if __name__ == "__main__":
    main()
