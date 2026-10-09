#!/usr/bin/env python3
"""C3.3b1 live HTTP toggle acceptance; only the existing disposable Compose.

No extra .NET build or Docker project. Only creates/terminates its own
temporary DevConsole process; backend performs guarded worker-only changes.
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
from urllib import error, request

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"
OUT = ROOT / ".local/e2e/c3-3b1-global-toggle"


def require(ok: bool, message: str) -> None:
    if not ok:
        raise RuntimeError(message)


def call(url: str, method: str = "GET", payload: object | None = None) -> tuple[int, dict]:
    body = None if payload is None else json.dumps(payload).encode()
    req = request.Request(url, data=body, method=method,
                          headers={"Content-Type": "application/json",
                                   "Accept": "application/json"})
    try:
        response = request.urlopen(req, timeout=115)
    except error.HTTPError as exc:
        response = exc
    with response:
        return response.status, json.loads(response.read())


def api(url: str, path: str, method: str = "GET", data: object | None = None) -> dict:
    code, body = call(url + path, method, data)
    require(code == 200, f"Console endpoint {path} returned HTTP {code}")
    return body


def check(workers: dict, mode: str, initial: dict, project: str) -> None:
    require(workers.get("mode") == mode and workers.get("composeProject") == project,
            f"effective mode {mode} was not confirmed by real HTTP")
    require(workers.get("sqlRunning") is True
            and workers.get("apiReady") is True
            and workers.get("resultadoApiLive") is True,
            "independent SQL/API/Resultado stopped")
    instances = workers.get("workers")
    require(isinstance(instances, list) and len(instances) == 3,
            "three worker states required")
    require({i.get("worker") for i in instances} ==
            {"processor", "operations-maintenance", "bronze-maintenance"},
            "different worker services returned")
    for entry in instances:
        name = entry["worker"]
        if mode == "OFF":
            require(entry.get("state") == "PARADO" and entry.get("hostPid") is None,
                    "OFF still has a resident worker: " + name)
        else:
            require(entry.get("state") == "ATIVO"
                    and isinstance(entry.get("hostPid"), int)
                    and entry["hostPid"] > 1
                    and isinstance(entry.get("restartCount"), int)
                    and entry.get("heartbeatAgeSeconds") is not None
                    and 0 <= entry["heartbeatAgeSeconds"] <= 45,
                    "ON must prove live heartbeat/PID for " + name)
    # The mode switch may restart only workers. SQL/API readiness is checked
    # here; container PID stability is checked by the backend worker CLI.
    if mode == "ON" and initial:
        previous = {x["worker"]: x for x in initial["workers"]}
        for entry in instances:
            require(entry["hostPid"] != previous[entry["worker"]]["hostPid"],
                    "ON did not create a new isolated worker instance")


def main() -> None:
    require(len(sys.argv) == 1, "no arguments")
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == "ci" + run + attempt
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "private CI credentials/identity required")
    require(DLL.is_file(), "prebuilt Console Release DLL absent")
    OUT.mkdir(parents=True, exist_ok=True)
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as bound:
        bound.bind(("127.0.0.1", 0))
        port = bound.getsockname()[1]
    root = f"http://127.0.0.1:{port}"
    project = f"jornada-workers-e2e-ci{run}{attempt}"
    env = {**os.environ, "JORNADA_RUNTIME_MODE": "DEV",
           "DOTNET_ENVIRONMENT": "Development",
           "ASPNETCORE_ENVIRONMENT": "Development",
           "ASPNETCORE_URLS": root}
    with (OUT / "devconsole-lifecycle.log").open("wb") as log:
        console = subprocess.Popen(["dotnet", str(DLL)], cwd=ROOT, env=env,
                                   stdin=subprocess.DEVNULL, stdout=log,
                                   stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 35
            while True:
                require(console.poll() is None, "Console lifecycle process exited")
                try:
                    code, state = call(root + "/api/version")
                    if code == 200 and state.get("mode") == "DEV":
                        break
                except (OSError, ValueError, TimeoutError):
                    pass
                require(time.monotonic() < deadline, "Console startup unavailable")
                time.sleep(0.25)

            original = api(root, "/api/workers/supervisor")
            check(original, "ON", {}, project)

            # Reject invalid input before doing any Docker operation.
            code, _ = call(root + "/api/workers/supervisor", "POST", {"mode": "MAYBE"})
            require(code == 400, "invalid supervisor mode did not fail closed")
            code, _ = call(root + "/api/commands/silver/start", "POST")
            require(code == 409, "unsafe legacy Silver command was not blocked")

            off = api(root, "/api/workers/supervisor", "POST", {"mode": "OFF"})
            check(off, "OFF", original, project)
            verify_off = api(root, "/api/workers/supervisor")
            check(verify_off, "OFF", original, project)

            # Re-sending OFF is idempotent and must not bring workers back.
            idem_off = api(root, "/api/workers/supervisor", "POST", {"mode": "OFF"})
            check(idem_off, "OFF", original, project)

            on = api(root, "/api/workers/supervisor", "POST", {"mode": "ON"})
            check(on, "ON", original, project)
            verify_on = api(root, "/api/workers/supervisor")
            check(verify_on, "ON", original, project)

            # No unnecessary Compose restarts when ON is already effective.
            idem_on = api(root, "/api/workers/supervisor", "POST", {"mode": "ON"})
            check(idem_on, "ON", original, project)
            for value in ("original", "off", "after-off", "on", "after-on", "idempotent-on"):
                item = {"original": original, "off": off, "after-off": verify_off,
                        "on": on, "after-on": verify_on,
                        "idempotent-on": idem_on}[value]
                (OUT / (value + ".json")).write_text(
                    json.dumps(item, indent=2, ensure_ascii=False) + "\n",
                    encoding="utf-8")
            print("C3.3b1: PASS real Console HTTP ON→OFF→ON, restart disarmed before stop, "
                  "RunOnce guarded, SQL/APIs independent")
        finally:
            if console.poll() is None:
                console.terminate()  # only the child DevConsole created by this test
                try:
                    console.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    console.kill()
                    console.wait(timeout=5)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, KeyError, ValueError) as exc:
        print("C3.3b1: REJECTED " + str(exc), file=sys.stderr)
        sys.exit(2)
