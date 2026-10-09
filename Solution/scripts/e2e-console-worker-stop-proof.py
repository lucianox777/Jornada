#!/usr/bin/env python3
"""C3.3c private Docker resident-stop regression: rejects stale identities."""
from __future__ import annotations
import json
import os
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
    stopped = subprocess.run(["python3", script, worker, cid, str(pid)],
                             env=env, capture_output=True, check=False, timeout=40)
    require(stopped.returncode == 0, "private resident stop failed")
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
