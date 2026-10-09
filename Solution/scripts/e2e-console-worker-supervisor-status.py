#!/usr/bin/env python3
"""C3.3a operational GET evidence against the disposable SQL/worker E2E project.

Starts ONLY a local DevConsole HTTP observer process, no containers or
SQL mutations. Kills/terminates only that child process on exit.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
from urllib import request, error

ROOT = Path(__file__).resolve().parents[1]
ASSEMBLY = ROOT / "src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"
OUTPUT = ROOT / ".local/e2e/c3-3a-supervisor"


def require(ok: bool, message: str) -> None:
    if not ok:
        raise RuntimeError(message)


def local_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.bind(("127.0.0.1", 0))
        return int(server.getsockname()[1])


def get_json(base: str, path: str) -> tuple[int, dict]:
    req = request.Request(base + path, headers={"Accept": "application/json"}, method="GET")
    try:
        res = request.urlopen(req, timeout=40)
    except error.HTTPError as exc:
        res = exc
    with res:
        return res.status, json.loads(res.read())


def main() -> None:
    require(len(sys.argv) == 2 and sys.argv[1] in ("OFF", "ON"), "expected OFF or ON")
    expected = sys.argv[1]
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == f"ci{run}{attempt}"
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "private disposable GitHub Actions sandbox only")
    require(ASSEMBLY.is_file(), "existing Release DevConsole DLL required; NO build")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    base = f"http://127.0.0.1:{local_port()}"
    env = dict(os.environ)
    env.update({
        "JORNADA_RUNTIME_MODE": "DEV",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": base,
    })
    # No use of a canonical NODE/host DB, Docker Compose or child worker.
    with (OUTPUT / f"devconsole-{expected.lower()}.log").open("wb") as output:
        proc = subprocess.Popen(
            ["dotnet", str(ASSEMBLY)], env=env, cwd=ROOT,
            stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 35
            while True:
                require(proc.poll() is None, "private observer DevConsole exited early")
                try:
                    code, version = get_json(base, "/api/version")
                    if code == 200 and version.get("mode") == "DEV":
                        break
                except (OSError, ValueError, TimeoutError):
                    pass
                require(time.monotonic() < deadline, "DevConsole observer startup timed out")
                time.sleep(0.2)
            code, state = get_json(base, "/api/workers/supervisor")
            require(code == 200, f"Console effective mode refused HTTP {code}")
            require(state.get("mode") == expected,
                    f"Console returned {state.get('mode')}, expected {expected}")
            require(state.get("toggleAvailable") is False,
                    "C3.3a must never expose an untested ON/OFF mutation")
            require(state.get("sqlRunning") is True and state.get("apiReady") is True
                    and state.get("resultadoApiLive") is True,
                    "SQL/API/Resultado unavailable")
            require(state.get("composeProject") ==
                    f"jornada-workers-e2e-ci{run}{attempt}", "unrecognized project")
            workers = state.get("workers")
            require(isinstance(workers, list) and len(workers) == 3,
                    "missing distinct workers")
            names = {entry.get("worker") for entry in workers}
            require(names == {"processor", "operations-maintenance", "bronze-maintenance"},
                    "unexpected service identity in backend")
            if expected == "OFF":
                for worker in workers:
                    require(worker.get("state") == "PARADO"
                            and worker.get("hostPid") is None
                            and worker.get("instanceId") is None,
                            "initial OFF must have zero residents or stale PID")
            else:
                for worker in workers:
                    require(worker.get("state") == "ATIVO"
                            and isinstance(worker.get("hostPid"), int)
                            and worker["hostPid"] > 1
                            and isinstance(worker.get("restartCount"), int)
                            and worker["restartCount"] >= 1
                            and isinstance(worker.get("heartbeatAgeSeconds"), int)
                            and 0 <= worker["heartbeatAgeSeconds"] <= 45
                            and re.fullmatch(
                                r"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}",
                                str(worker.get("instanceId", ""))),
                            "ON requires real PID, restart counter and fresh SQL heartbeat")
            (OUTPUT / f"snapshot-{expected.lower()}.json").write_text(
                json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print(f"C3.3a: {expected} observed via real Console HTTP + Docker PID/SQL heartbeat")
        finally:
            if proc.poll() is None:
                proc.terminate()  # only the DevConsole process CREATED ABOVE
                try:
                    proc.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait(timeout=5)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, OSError, KeyError) as exc:
        print("C3.3a: REJECTED " + str(exc), file=sys.stderr)
        raise SystemExit(2)
