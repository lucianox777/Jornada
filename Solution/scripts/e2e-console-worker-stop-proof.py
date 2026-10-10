#!/usr/bin/env python3
"""C3.3c private Docker resident-stop regression: rejects stale identities."""
from __future__ import annotations
import json
import os
from pathlib import Path
import socket
import time
from urllib import error, request
import re
import subprocess
import sys

WORKERS = ("processor", "operations-maintenance", "bronze-maintenance")


def require(ok: bool, reason: str) -> None:
    if not ok:
        raise RuntimeError(reason)


def docker(*args: str) -> str:
    p = subprocess.run(["docker", *args], capture_output=True,
                       stdin=subprocess.DEVNULL, check=False, timeout=15)
    require(p.returncode == 0, "private Docker inspection failed")
    return p.stdout.decode().strip()


def inspect(cid: str) -> dict:
    return json.loads(docker("inspect", "--format", "{{json .}}", cid))


def main() -> None:
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == "ci" + run + attempt
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None,
            "CI-only regression")
    project = "jornada-workers-e2e-ci" + run + attempt
    snapshots = {}
    for service in ("sqlserver", "api", "resultado-api", *WORKERS):
        ids = docker("ps", "-aq",
                     "--filter", "label=com.docker.compose.project=" + project,
                     "--filter", "label=com.docker.compose.service=" + service,
                     "--filter", "label=com.docker.compose.oneoff=False").splitlines()
        require(len(ids) == 1, "expected exactly one private container")
        obj = inspect(ids[0])
        require(obj["State"]["Running"] is True
                and int(obj["State"]["Pid"]) > 1,
                "private service must be running")
        snapshots[service] = (obj["Id"], obj["State"]["Pid"])
    worker = "processor"
    cid, pid = snapshots[worker]
    script = os.path.join(os.path.dirname(__file__), "console-private-worker-stop.py")
    env = {**os.environ, "JORNADA_RUNTIME_MODE": "DEV"}
    # A stale PID must never stop a live worker.
    denied = subprocess.run(["python3", script, worker, cid, str(pid + 1)],
                            env=env, capture_output=True, check=False, timeout=30)
    require(denied.returncode != 0, "stale PID accepted")
    require(inspect(cid)["State"]["Running"] is True, "stale PID stopped worker")
    # No worker can be addressed using another service's immutable identity.
    denied = subprocess.run(["python3", script, "bronze-maintenance", cid, str(pid)],
                            env=env, capture_output=True, check=False, timeout=30)
    require(denied.returncode != 0, "foreign service identity accepted")
    require(inspect(cid)["State"]["Running"] is True, "foreign service stopped worker")
    # The original CLI-negative cases above protect the Docker/PID guard.
    # The *successful* stop must go through the real loopback Console HTTP
    # controller: a CLI-only call bypasses the controller and cannot create
    # an ADMITIDO/SUCESSO audit event. This additionally proves API/SQL
    # status was checked before and after the operation.
    root = Path(__file__).resolve().parents[1]
    dll = root / "src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"
    require(dll.is_file(), "private Console Release assembly missing")

    def http(base: str, endpoint: str, method: str = "GET",
             payload: dict | None = None) -> tuple[int, dict]:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        req = request.Request(base + endpoint, data=data, method=method,
                              headers={"Content-Type": "application/json"})
        try:
            response = request.urlopen(req, timeout=85)
        except error.HTTPError as ex:
            response = ex
        with response:
            return response.status, json.loads(response.read())

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as bound:
        bound.bind(("127.0.0.1", 0))
        port = bound.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    console_env = {**env, "DOTNET_ENVIRONMENT": "Development",
                   "ASPNETCORE_ENVIRONMENT": "Development",
                   "ASPNETCORE_URLS": base}
    out = root / ".local/e2e/c3-3c-stop"
    out.mkdir(parents=True, exist_ok=True)
    with (out / "devconsole-stop-audit.log").open("wb") as log:
        console = subprocess.Popen(["dotnet", str(dll)], cwd=root,
                                   env=console_env, stdin=subprocess.DEVNULL,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 35
            while True:
                require(console.poll() is None,
                        "private DevConsole exited before stop acceptance")
                try:
                    code, version = http(base, "/api/version")
                    if code == 200 and version.get("mode") == "DEV":
                        break
                except (OSError, ValueError):
                    pass
                require(time.monotonic() < deadline, "private Console not ready")
                time.sleep(0.2)

            code, before = http(base, "/api/workers/supervisor")
            require(code == 200 and before.get("mode") == "ON"
                    and before.get("composeProject") == project,
                    "worker supervisor not ON in the private project")
            require(before.get("sqlRunning") is True
                    and before.get("apiReady") is True
                    and before.get("resultadoApiLive") is True,
                    "private SQL/API/Resultado unavailable")
            members = {x["worker"]: x for x in before["workers"]}
            require(set(members) == set(WORKERS)
                    and members[worker]["containerId"] == cid
                    and members[worker]["hostPid"] == pid,
                    "private Docker identity differs from Console observation")
            endpoint = f"/api/workers/{worker}/stop"
            code, _ = http(base, endpoint, "POST",
                           {"containerId": cid, "hostPid": pid, "confirmed": False})
            require(code == 409 and inspect(cid)["State"]["Running"] is True,
                    "unconfirmed HTTP stop changed Docker")
            code, _ = http(base, endpoint, "POST",
                           {"containerId": cid, "hostPid": pid + 1, "confirmed": True})
            require(code == 409 and inspect(cid)["State"]["Running"] is True,
                    "stale HTTP PID stopped a worker")
            code, result = http(base, endpoint, "POST",
                                {"containerId": cid, "hostPid": pid, "confirmed": True})
            require(code == 200 and result.get("mode") == "ERRO",
                    "confirmed private Console stop was not accepted")
            after = {x["worker"]: x for x in result["workers"]}
            require(after[worker]["state"] == "PARADO"
                    and all(after[x]["state"] == "ATIVO"
                            for x in WORKERS if x != worker),
                    "private stop changed unrelated workers")
            require(result.get("sqlRunning") is True
                    and result.get("apiReady") is True
                    and result.get("resultadoApiLive") is True,
                    "private APIs/SQL changed after stop")
        finally:
            if console.poll() is None:
                console.terminate()  # terminate only this temporary Console child
                try:
                    console.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    console.kill()
                    console.wait(timeout=5)
    require(inspect(cid)["State"]["Running"] is False, "worker remains running")
    for service, (before_id, before_pid) in snapshots.items():
        if service == worker:
            continue
        now = inspect(before_id)
        require(now["Id"] == before_id
                and now["State"]["Running"] is True
                and now["State"]["Pid"] == before_pid,
                "unrelated private service changed: " + service)
    print("C3.3c: PASS stale PID/foreign service rejected; one resident stopped; five peers intact")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError, KeyError, subprocess.TimeoutExpired):
        print("C3.3c: FAIL private stop regression", file=sys.stderr)
        sys.exit(2)
