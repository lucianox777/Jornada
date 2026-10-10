#!/usr/bin/env python3
"""C3.3b3b0: exact-container SIGTERM for one private finite Docker oneoff.

NOT a public API. The C3.3b3b controller must separately authenticate
execution ID + one-time operator confirmation before invoking this helper.
No arbitrary project, container, command, Docker host or database argument.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

WORKERS = ("processor", "operations-maintenance", "bronze-maintenance")
INFRA = ("sqlserver", "api", "resultado-api")
ROOT = Path(__file__).resolve().parents[1]


def require(ok: bool, reason: str) -> None:
    if not ok:
        raise RuntimeError(reason)


def run(*args: str, timeout: int = 12) -> str:
    p = subprocess.run(args, cwd=ROOT, capture_output=True,
                       stdin=subprocess.DEVNULL, check=False, timeout=timeout)
    # Never print subprocess output: Docker stderr may contain credentials.
    require(p.returncode == 0, "private Docker verification/stop failed")
    return p.stdout.decode("utf-8").strip()


def inspect(cid: str) -> dict:
    return json.loads(run("docker", "inspect", "--format", "{{json .}}", cid))


def ids(project: str, *, oneoff: bool, service: str | None = None) -> list[str]:
    query = ["docker", "ps", "-aq", "--filter",
             "label=com.docker.compose.project=" + project, "--filter",
             "label=com.docker.compose.oneoff=" + ("True" if oneoff else "False")]
    if service is not None:
        query += ["--filter", "label=com.docker.compose.service=" + service]
    result = run(*query).splitlines()
    require(all(re.fullmatch(r"[a-f0-9]{12,64}", item) for item in result),
            "unexpected Docker container identity")
    return result


def verify_ci() -> str:
    run_id = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and os.environ.get("JORNADA_WORKERS_E2E_CANCEL_ALLOWED") == "true"
            and os.environ.get("JORNADA_WORKERS_E2E_IMAGE_TAG", "test") == "test"
            and re.fullmatch(r"[0-9]{6,16}", run_id) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == "ci" + run_id + attempt
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "only disposable GitHub-hosted JornadaE2E sandbox may stop oneoff")
    project = "jornada-workers-e2e-ci" + run_id + attempt
    require(re.fullmatch(r"jornada-workers-e2e-ci[0-9]{7,19}", project) is not None,
            "unexpected Compose project identity")
    return project


def infrastructure(project: str) -> dict[str, tuple[str, int, int]]:
    snapshot = {}
    for service in INFRA:
        found = ids(project, oneoff=False, service=service)
        require(len(found) == 1, "private SQL/API resident missing/ambiguous")
        obj = inspect(found[0])
        labels = obj["Config"]["Labels"]
        require(labels.get("com.docker.compose.project") == project
                and labels.get("com.docker.compose.service") == service
                and labels.get("com.docker.compose.oneoff") == "False"
                and obj["State"]["Running"] is True
                and int(obj["State"]["Pid"]) > 1,
                "private SQL/API resident identity/health absent")
        snapshot[service] = (
            obj["Id"], int(obj["State"]["Pid"]), int(obj["RestartCount"]))
    return snapshot


def main() -> None:
    require(len(sys.argv) == 5, "worker, container ID, PID, execution UUID required")
    project = verify_ci()
    worker, cid, pid_text, execution_id = sys.argv[1:]
    require(worker in WORKERS, "foreign worker not allowed")
    require(re.fullmatch(r"[0-9a-f]{64}", cid) is not None,
            "full immutable container ID required")
    require(re.fullmatch(r"[1-9][0-9]{0,8}", pid_text) is not None
            and int(pid_text) > 1, "valid host PID required")
    require(re.fullmatch(
        r"[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}",
        execution_id) is not None,
        "opaque run ID required")

    before = infrastructure(project)
    for service in WORKERS:
        resident = ids(project, oneoff=False, service=service)
        require(not resident or all(not inspect(x)["State"]["Running"] for x in resident),
                "resident worker present during finite-only OFF mode")

    found = ids(project, oneoff=True)
    require(len(found) == 1 and (cid == found[0] or cid.startswith(found[0])),
            "missing, ambiguous or foreign private oneoff")
    obj = inspect(cid)
    labels = obj["Config"]["Labels"]
    state = obj["State"]
    host = obj["HostConfig"]
    require(obj["Id"] == cid
            and labels.get("com.docker.compose.project") == project
            and labels.get("com.docker.compose.service") == worker
            and labels.get("com.docker.compose.oneoff") == "True"
            and state["Running"] is True
            and int(state["Pid"]) == int(pid_text)
            and host["RestartPolicy"]["Name"] == "no"
            and not host.get("PortBindings")
            and all(m.get("Type") != "bind" for m in obj.get("Mounts", []))
            and "--run-once" in (obj["Config"].get("Cmd") or []),
            "oneoff identity/PID/restart/port/volume/run-once guard rejected")
    networks = obj["NetworkSettings"]["Networks"]
    require(set(networks) == {project + "_default"},
            "private oneoff has non-isolated network")

    # docker stop first sends SIGTERM and after 6 seconds SIGKILL, both to
    # this exact 64-hex ID only. The owning 'docker compose run --rm' must
    # independently observe the process exit and remove this oneoff.
    # No docker rm/down/prune, no resident stop and no host resource access.
    run("docker", "stop", "--time", "6", cid, timeout=18)
    deadline = time.monotonic() + 30
    while True:
        remaining = ids(project, oneoff=True)
        require(not any(x != cid and not cid.startswith(x) for x in remaining),
                "another finite container appeared during cancellation")
        if not remaining:
            break
        require(time.monotonic() < deadline,
                "oneoff not removed by owning docker compose --rm")
        time.sleep(0.2)

    require(infrastructure(project) == before,
            "private SQL/API/Resultado identity changed during oneoff stop")
    for service in WORKERS:
        require(not any(inspect(x)["State"]["Running"]
                        for x in ids(project, oneoff=False, service=service)),
                "finite stop altered/started a resident worker")
    print("C3.3b3b0: PASS exact private oneoff exited and --rm removed; infrastructure unchanged")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, KeyError, OSError,
            subprocess.TimeoutExpired, TypeError, json.JSONDecodeError):
        print("C3.3b3b0: REJECTED private oneoff safety condition", file=sys.stderr)
        sys.exit(2)
