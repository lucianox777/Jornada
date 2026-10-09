#!/usr/bin/env python3
"""C3.3c: stop exactly one verified resident in the disposable GitHub E2E project.

Never accepts a project name, Docker command, arbitrary PID or container lookup
from a caller. An expected container ID and host PID are mandatory compare-and-
stop fences. This tool cannot target a oneoff, SQL, API or a host service.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys

WORKERS = {"processor", "operations-maintenance", "bronze-maintenance"}


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise RuntimeError(reason)


def docker(*args: str) -> str:
    result = subprocess.run(["docker", *args], stdin=subprocess.DEVNULL,
                            capture_output=True, check=False, timeout=20)
    require(result.returncode == 0, "private Docker command failed")
    return result.stdout.decode("utf-8").strip()


def inspect(cid: str, project: str, worker: str) -> dict:
    obj = json.loads(docker("inspect", "--format", "{{json .}}", cid))
    labels = obj["Config"]["Labels"]
    require(labels.get("com.docker.compose.project") == project
            and labels.get("com.docker.compose.service") == worker
            and labels.get("com.docker.compose.oneoff") == "False",
            "Docker identity mismatch")
    return obj


def main() -> None:
    require(len(sys.argv) == 4, "expected worker, container ID and host PID")
    worker, cid, pid_text = sys.argv[1:]
    require(worker in WORKERS
            and re.fullmatch(r"[0-9a-f]{64}", cid) is not None
            and re.fullmatch(r"[0-9]{1,10}", pid_text) is not None
            and int(pid_text) > 1, "invalid worker identity")
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == "ci" + run + attempt
            and os.environ.get("JORNADA_WORKERS_E2E_IMAGE_TAG", "test") == "test"
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "not the disposable GitHub CI DEV environment")
    project = "jornada-workers-e2e-ci" + run + attempt
    ids = docker("ps", "-aq",
                 "--filter", "label=com.docker.compose.project=" + project,
                 "--filter", "label=com.docker.compose.service=" + worker,
                 "--filter", "label=com.docker.compose.oneoff=False").splitlines()
    require(len(ids) == 1 and ids[0] == cid[:12],
            "worker container changed or is ambiguous")
    before = inspect(cid, project, worker)
    require(before["State"]["Running"] is True
            and before["State"]["Pid"] == int(pid_text)
            and before["HostConfig"]["RestartPolicy"]["Name"] == "unless-stopped",
            "worker PID/state/restart policy changed")
    # Docker stop is container-ID scoped, not PID scoped; never use host kill.
    docker("stop", "--time", "15", cid)
    after = inspect(cid, project, worker)
    require(after["State"]["Running"] is False
            and after["State"]["Pid"] == 0,
            "worker stop not confirmed by Docker")
    print("C3.3c: PASS one verified private resident stopped")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError, KeyError, subprocess.TimeoutExpired):
        print("C3.3c: REJECTED private resident stop", file=sys.stderr)
        sys.exit(2)
