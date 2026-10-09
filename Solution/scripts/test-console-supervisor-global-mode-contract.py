#!/usr/bin/env python3
"""C3.3b1 unit contract: static, negative-only, NO Docker/SQL/worker execution."""
from pathlib import Path
import os
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "scripts/console-private-worker-mode.py"
HTTP = ROOT / "scripts/e2e-console-supervisor-global-toggle.py"
CONTROL = ROOT / "src/Jornada.DevConsole/IsolatedWorkerSupervisorModeController.cs"
API = ROOT / "src/Jornada.DevConsole/Program.cs"
RUNTIME = ROOT / "src/Jornada.DevConsole/DevConsoleRuntime.cs"


def deny(target: Path, args: tuple[str, ...], environment: dict[str, str]) -> None:
    result = subprocess.run([sys.executable, str(target), *args],
                            cwd=ROOT, capture_output=True, text=True,
                            timeout=5, check=False, env={
                                "PATH": os.environ.get("PATH", ""),
                                "HOME": "/tmp", **environment})
    assert result.returncode == 2 and not result.stdout, (
        target.name, result.returncode, result.stderr, result.stdout)


def main() -> None:
    for path in (CLI, HTTP):
        compile(path.read_text(encoding="utf-8"), str(path), "exec")
        deny(path, (), {})
        deny(path, ("ON",), {})
        deny(path, ("OFF",), {})
        deny(path, ("BAD",), {})
        deny(path, ("ON",), {"GITHUB_ACTIONS": "true", "CI": "true",
                            "GITHUB_REPOSITORY": "wrong/repository"})
    valid_no_secret = {
        "GITHUB_ACTIONS": "true", "CI": "true",
        "GITHUB_REPOSITORY": "lucianox777/Jornada",
        "GITHUB_RUN_ID": "1234567", "GITHUB_RUN_ATTEMPT": "1",
        "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
        "JORNADA_WORKERS_E2E_ID": "ci12345671",
        "JORNADA_RUNTIME_MODE": "DEV",
    }
    deny(CLI, ("ON",), valid_no_secret)
    deny(CLI, ("ON",), {**valid_no_secret,
                        "JORNADA_WORKERS_E2E_SQL_PASSWORD": "fake",
                        "DOCKER_HOST": "tcp://external:2375"})
    deny(CLI, ("OFF",), {**valid_no_secret,
                         "JORNADA_WORKERS_E2E_SQL_PASSWORD": "fake",
                         "DOCKER_CONTEXT": "external"})
    deny(CLI, ("ON",), {**valid_no_secret,
                        "JORNADA_WORKERS_E2E_SQL_PASSWORD": "fake",
                        "JORNADA_WORKERS_E2E_ID": "ciOTHER"})
    deny(CLI, ("OFF",), {**valid_no_secret,
                         "JORNADA_WORKERS_E2E_SQL_PASSWORD": "fake",
                         "JORNADA_RUNTIME_MODE": "PROD"})
    cli = CLI.read_text(encoding="utf-8")
    api = API.read_text(encoding="utf-8")
    ctl = CONTROL.read_text(encoding="utf-8")
    runtime = RUNTIME.read_text(encoding="utf-8")
    e2e = HTTP.read_text(encoding="utf-8")

    assert "WORKERS = (\"processor\", \"operations-maintenance\", \"bronze-maintenance\")" in cli
    assert "INFRA = (\"sqlserver\", \"api\", \"resultado-api\")" in cli
    assert 'com.docker.compose.project' in cli
    assert 'com.docker.compose.service' in cli
    assert 'image' not in re.sub(r"(?m)^\s*#.*$", "", cli).lower() or 'JORNADA_WORKERS_E2E_IMAGE_TAG' in cli
    disarm = cli.index('"docker", "update", "--restart=no"')
    stop = cli.index('"docker", "stop", "--time", "8"')
    assert disarm < stop, "disarm must happen before stop"
    assert "state[\"HostConfig\"][\"RestartPolicy\"][\"Name\"] == \"unless-stopped\"" in cli
    assert '"--force-recreate", *WORKERS' in cli
    assert '"--no-build", "--no-deps"' in cli
    assert 'infra_unchanged(project, before)' in cli
    for forbidden in ("JornadaLocal", "DROP DATABASE", "RESTORE DATABASE",
                      '"prune"', '"down"', '"kill"', '"rm"', '"volume"'):
        assert forbidden not in cli, forbidden
    assert "subprocess.run(args, cwd=ROOT" in cli
    assert "stdin=subprocess.DEVNULL" in cli
    assert "process.returncode == 0" in cli

    assert 'private' in ctl.lower()
    assert 'SemaphoreSlim transition' in ctl
    assert 'await transition.WaitAsync(ct)' in ctl
    assert 'HasActiveWorkerRunOnce()' in ctl
    assert 'before.Mode is not ("OFF" or "ON")' in ctl
    assert 'if (before.Mode == requested)' in ctl
    assert 'process.Start()' in ctl
    assert 'psi.ArgumentList.Add(requested)' in ctl
    assert 'effective.Mode == requested' in ctl
    assert 'activity.Add("SUPERVISAO"' in ctl
    assert 'RedirectStandardOutput = true' in ctl
    assert 'RedirectStandardError = true' in ctl
    assert 'UseShellExecute = false' in ctl
    assert 'builder.Services.AddSingleton<IsolatedWorkerSupervisorModeController>()' in api
    assert 'app.MapPost("/api/workers/supervisor"' in api
    assert 'IsolatedWorkerSupervisorModeController controller' in api
    assert 'System.Net.IPAddress.IsLoopback(remote)' in api
    assert 'reader.Enabled(runtime)' in api
    assert 'controller.SetAsync(request.Mode,runtime,ct)' in api
    assert 'definition.Id=="silver"&&supervisor.Enabled(runtime)' in api
    assert 'HasActiveWorkerRunOnce()' in runtime
    assert 'commandIds.TryAdd(id,definition.Id)' in runtime

    assert '"/api/workers/supervisor", "POST", {"mode": "MAYBE"}' in e2e
    assert '"/api/commands/silver/start", "POST"' in e2e
    assert '"/api/workers/supervisor", "POST", {"mode": "OFF"}' in e2e
    assert '"/api/workers/supervisor", "POST", {"mode": "ON"}' in e2e
    assert 'console.terminate()' in e2e
    assert 'localhost' not in e2e or '127.0.0.1' in e2e
    print("C3.3b1: PASS negative lifecycle/RunOnce/identity guard (no Docker/SQL)")


if __name__ == "__main__":
    main()
