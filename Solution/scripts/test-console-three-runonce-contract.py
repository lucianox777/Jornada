#!/usr/bin/env python3
"""C3.3b2 fail-closed backend/CLI contract; never creates Docker or SQL."""
from pathlib import Path
import os
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "scripts/console-private-worker-runonce.py"
HTTP = ROOT / "scripts/e2e-console-supervisor-three-runonce.py"
CTRL = ROOT / "src/Jornada.DevConsole/IsolatedWorkerSupervisorModeController.cs"
API = ROOT / "src/Jornada.DevConsole/Program.cs"
STATUS = ROOT / "src/Jornada.DevConsole/IsolatedWorkerSupervisorStatusReader.cs"
MODE = ROOT / "scripts/console-private-worker-mode.py"
BOOT = ROOT / "scripts/e2e-private-sql-bootstrap-runtime.sh"


def deny(cmd: Path, args: tuple[str, ...], env: dict[str, str]) -> None:
    result = subprocess.run([sys.executable, str(cmd), *args],
                            cwd=ROOT, env={"PATH": os.environ.get("PATH", ""),
                                           "HOME": "/tmp", **env},
                            capture_output=True, text=True, timeout=5, check=False)
    assert result.returncode == 2 and not result.stdout, (
        cmd.name, result.returncode, result.stderr)


def main() -> None:
    for target in (CLI, HTTP):
        compile(target.read_text(encoding="utf-8"), str(target), "exec")
        deny(target, (), {})
        deny(target, ("processor",), {})
        deny(target, ("bronze-maintenance",), {})
        deny(target, ("unknown",), {"GITHUB_ACTIONS": "true", "CI": "true"})
    pseudo = {"GITHUB_ACTIONS": "true", "CI": "true",
              "GITHUB_REPOSITORY": "lucianox777/Jornada",
              "GITHUB_RUN_ID": "1234567", "GITHUB_RUN_ATTEMPT": "1",
              "JORNADA_WORKERS_E2E_ID": "ci12345671",
              "JORNADA_WORKERS_E2E_SQL_PASSWORD": "fake",
              "JORNADA_RUNTIME_MODE": "DEV",
              "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true"}
    deny(CLI, ("processor",), {**pseudo, "DOCKER_HOST": "tcp://external:2375"})
    deny(CLI, ("processor",), {**pseudo, "DOCKER_CONTEXT": "external"})
    deny(CLI, ("processor",), {**pseudo, "JORNADA_RUNTIME_MODE": "PROD"})
    deny(CLI, ("processor",), {**pseudo, "JORNADA_WORKERS_E2E_ID": "wrong"})
    deny(CLI, ("processor",), {**pseudo, "GITHUB_REPOSITORY": "other/project"})
    cli = CLI.read_text(encoding="utf-8")
    api = API.read_text(encoding="utf-8")
    ctrl = CTRL.read_text(encoding="utf-8")
    status = STATUS.read_text(encoding="utf-8")
    mode = MODE.read_text(encoding="utf-8")
    e2e = HTTP.read_text(encoding="utf-8")
    assert all('"' + worker + '"' in cli for worker in
               ("processor", "operations-maintenance", "bronze-maintenance"))
    for guard in ("GITHUB_ACTIONS", "GITHUB_REPOSITORY", "JORNADA_RUNTIME_MODE",
                  "JORNADA_WORKERS_E2E_SQL_PASSWORD", "JORNADA_WORKERS_E2E_ID",
                  "DOCKER_HOST", "DOCKER_CONTEXT", "JORNADA_WORKERS_E2E_IMAGE_TAG"):
        assert guard in cli
    assert "JornadaE2E" in cli and 'jornada-workers-e2e-ci' in cli
    assert 'com.docker.compose.project' in cli and 'com.docker.compose.service' in cli
    assert 'label=com.docker.compose.oneoff=True' in cli
    assert 'label=com.docker.compose.oneoff=False' in cli
    assert 'JORNADA_WORKERS_E2E_RUN_ONCE_ALLOWED=true' in cli
    assert 'JORNADA_WORKERS_E2E_RUN_ONCE=true' in cli
    assert '"run", "--rm", "--no-deps", "-T"' in cli
    assert '"--run-once"' in cli
    assert "result.returncode == 0" in cli
    assert "infra" in cli and "RunOnce" in cli
    for bad in ("JornadaLocal", "DROP DATABASE", "RESTORE DATABASE",
                '"prune"', '"down"', '"kill"', '"volume"'):
        assert bad not in cli
    assert 'label=com.docker.compose.oneoff=False' in status
    assert 'label=com.docker.compose.oneoff=False' in mode
    assert 'label=com.docker.compose.oneoff=True' in mode
    assert "ConcurrentDictionary<string,byte> activeFinite" in ctrl
    assert 'await transition.WaitAsync(ct)' in ctrl
    assert 'if(requested=="ON"&&!activeFinite.IsEmpty)' in ctrl
    assert 'if(effective.Mode!="OFF")' in ctrl
    assert 'activeFinite.TryAdd(worker,0)' in ctrl
    assert 'activeFinite.TryRemove(worker,out _)' in ctrl
    assert 'WithFiniteWorkers(' in ctrl and 'State="RUN_ONCE"' in ctrl
    assert 'ApplyPrivateRunOnceAsync(' in ctrl
    assert 'Script' not in ctrl or 'console-private-worker-runonce.py' in ctrl
    assert 'UseShellExecute=false' in ctrl
    assert 'psi.ArgumentList.Add(worker)' in ctrl
    assert 'Results.Ok(await controller.RunOnceAsync(worker,runtime,ct))' in api
    assert 'app.MapPost("/api/workers/{worker}/run-once"' in api
    assert 'System.Net.IPAddress.IsLoopback(remote)' in api
    assert 'controller.WithFiniteWorkers(await supervisor.ReadAsync(runtime,ct))' in api
    assert '"/api/workers/" + worker + "/run-once", "POST"' in e2e
    assert '"ON" must reject' not in e2e or '"ON"' in e2e
    assert 'ON must reject all three RunOnce workers' in e2e
    assert 'state(base, "OFF")' in e2e and 'state(base, "ON")' in e2e
    assert "python3 scripts/e2e-console-supervisor-three-runonce.py" in BOOT.read_text()
    print("C3.3b2: PASS three finite worker endpoints/guards (no Docker or SQL)")


if __name__ == "__main__":
    main()
