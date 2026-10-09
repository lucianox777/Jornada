#!/usr/bin/env python3
"""C3.3b1: toggle ONLY the three workers of a unique GitHub-hosted E2E Compose.

No external daemon, host port, file bind mount, user cluster or user SQL.
Called from the DevConsole backend after verifying the effective mode and lock.
NOT a general Docker maintenance utility.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
COMPOSE_FILE = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"
WORKERS = ("processor", "operations-maintenance", "bronze-maintenance")
INFRA = ("sqlserver", "api", "resultado-api")


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise RuntimeError(reason)


def cmd(*args: str, timeout: float = 45) -> str:
    process = subprocess.run(args, cwd=ROOT, stdin=subprocess.DEVNULL,
                             capture_output=True, timeout=timeout, check=False)
    # Never print stderr: Compose may echo the environment, including secret.
    require(process.returncode == 0, "disposable Docker operation failed: " + args[0])
    return process.stdout.decode("utf-8").strip()


def inspect(cid: str) -> dict:
    return json.loads(cmd("docker", "inspect", "--format", "{{json .}}", cid))


def service_id(project: str, service: str) -> str | None:
    ids = cmd("docker", "ps", "-aq",
              "--filter", "label=com.docker.compose.project=" + project,
              "--filter", "label=com.docker.compose.service=" + service,
              "--filter", "label=com.docker.compose.oneoff=False").splitlines()
    require(len(ids) <= 1 and all(re.fullmatch(r"[a-f0-9]{12,64}", i) for i in ids),
            "ambiguous Docker service identity")
    return ids[0] if ids else None


def service(project: str, name: str) -> tuple[str, dict] | None:
    cid = service_id(project, name)
    if cid is None:
        return None
    state = inspect(cid)
    labels = state["Config"]["Labels"]
    require(labels.get("com.docker.compose.project") == project
            and labels.get("com.docker.compose.service") == name
            and state["Id"].startswith(cid), "foreign container identity rejected")
    return cid, state


def infra_before(project: str) -> dict:
    states = {}
    for name in INFRA:
        entry = service(project, name)
        require(entry is not None, "private infrastructure missing: " + name)
        cid, state = entry
        require(state["State"]["Running"] is True
                and int(state["State"]["Pid"]) > 1
                and state["State"].get("Health", {}).get("Status") == "healthy",
                "private SQL/API not healthy: " + name)
        states[name] = (cid, state["State"]["Pid"], state["RestartCount"])
    return states


def infra_unchanged(project: str, previous: dict) -> None:
    for name, (cid, pid, restarts) in previous.items():
        current = service(project, name)
        require(current is not None and current[0] == cid,
                "infrastructure container changed: " + name)
        state = current[1]
        require(state["State"]["Running"] is True
                and state["State"]["Pid"] == pid
                and state["RestartCount"] == restarts,
                "SQL/API process restarted during worker transition: " + name)


def verified_compose(project: str) -> tuple[str, ...]:
    return ("docker", "compose", "--env-file", "/dev/null",
            "--profile", "continuous", "-p", project,
            "-f", str(COMPOSE_FILE))


def on(project: str) -> None:
    before = infra_before(project)
    oneoffs = cmd("docker", "ps", "-aq",
                  "--filter", "label=com.docker.compose.project=" + project,
                  "--filter", "label=com.docker.compose.oneoff=True")
    require(not oneoffs, "refusing ON while a finite oneoff might be active")
    for name in WORKERS:
        entry = service(project, name)
        require(entry is None or entry[1]["State"]["Running"] is False,
                "ON requires all resident workers OFF (no partial mode)")
    # Compose starts ONLY allowlisted worker service names and does not rebuild.
    cmd(*verified_compose(project), "up", "-d", "--no-build", "--no-deps",
        "--force-recreate", *WORKERS, timeout=65)
    for name in WORKERS:
        entry = service(project, name)
        require(entry is not None, "worker not created: " + name)
        _, state = entry
        require(state["State"]["Running"] is True
                and int(state["State"]["Pid"]) > 1
                and state["HostConfig"]["RestartPolicy"]["Name"] == "unless-stopped",
                "worker did not receive independent restart supervision: " + name)
    infra_unchanged(project, before)


def off(project: str) -> None:
    before = infra_before(project)
    targets = []
    for name in WORKERS:
        entry = service(project, name)
        if entry is not None:
            cid, state = entry
            targets.append((name, cid))
            require(state["State"]["Running"] is True
                    and int(state["State"]["Pid"]) > 1,
                    "OFF refuses partial/restarting worker: " + name)
    require(len(targets) == len(WORKERS),
            "OFF requires three unambiguous supervised workers")
    # FIRST disarm automatic restarts, BEFORE administratively stopping PIDs.
    for name, cid in targets:
        require(service(project, name)[0] == cid,
                "worker changed before restart disarm")
        cmd("docker", "update", "--restart=no", cid)
        require(service(project, name)[1]["HostConfig"]["RestartPolicy"]["Name"] == "no",
                "restart policy not disarmed: " + name)
    for name, cid in targets:
        require(service(project, name)[0] == cid,
                "worker identity changed before stop")
        cmd("docker", "stop", "--time", "8", cid, timeout=25)
    # Demonstrate stop is stable; never remove Docker containers or volumes.
    time.sleep(2)
    for name, cid in targets:
        current = service(project, name)
        require(current is not None and current[0] == cid
                and current[1]["State"]["Running"] is False
                and current[1]["HostConfig"]["RestartPolicy"]["Name"] == "no",
                "worker unexpectedly restarted after supervisor OFF: " + name)
    infra_unchanged(project, before)


def main() -> None:
    require(len(sys.argv) == 2 and sys.argv[1] in ("ON", "OFF"),
            "exactly one ON/OFF command required")
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == "ci" + run + attempt
            and os.environ.get("JORNADA_WORKERS_E2E_IMAGE_TAG", "test") == "test"
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "worker lifecycle permitted only in isolated GitHub-hosted DEV CI")
    project = f"jornada-workers-e2e-ci{run}{attempt}"
    require(COMPOSE_FILE.is_file(), "private Compose file is absent")
    # Refuse any stray non-allowlisted service in the E2E project so that
    # this script cannot affect a reused or misconfigured Docker topology.
    known = set(WORKERS) | set(INFRA) | {"sql-bootstrap"}
    containers = cmd("docker", "ps", "-aq",
                     "--filter", "label=com.docker.compose.project=" + project).splitlines()
    for cid in containers:
        st = inspect(cid)
        labels = st["Config"]["Labels"]
        require(labels.get("com.docker.compose.project") == project
                and labels.get("com.docker.compose.service") in known,
                "unexpected service in disposable project")
    if sys.argv[1] == "ON":
        on(project)
    else:
        off(project)
    print("C3.3b1: " + sys.argv[1] + " safely applied only to three private worker services")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, KeyError, ValueError, subprocess.TimeoutExpired) as exc:
        print("C3.3b1: REJECTED " + type(exc).__name__, file=sys.stderr)
        sys.exit(2)
