#!/usr/bin/env python3
"""Finite 3-worker CLI exclusively for CI-created JornadaE2E Compose project.

This program NEVER targets the normal NODE, JornadaLocal, HML, or PROD.
No user/HTTP-supplied project, service or command is accepted.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
FILE = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"
WORKERS = {
    "processor": ("Processor", "Processor__RunOnce"),
    "operations-maintenance": ("OperationsMaintenance", "MaintenanceExecution__RunOnce"),
    "bronze-maintenance": ("BronzeMaintenance", "BronzeMaintenance__RunOnce"),
}
INFRA = ("sqlserver", "api", "resultado-api")


def require(ok: bool, text: str) -> None:
    if not ok:
        raise RuntimeError(text)


def command(*args: str, timeout: int = 15) -> str:
    process = subprocess.run(args, cwd=ROOT, stdin=subprocess.DEVNULL,
                             capture_output=True, timeout=timeout, check=False)
    # Never log stderr: it could contain credentials or connection strings.
    require(process.returncode == 0, "private Docker inspection/operation rejected")
    return process.stdout.decode("utf-8").strip()


def project_container(project: str, service: str) -> tuple[str, dict] | None:
    ids = command("docker", "ps", "-aq",
                  "--filter", "label=com.docker.compose.project=" + project,
                  "--filter", "label=com.docker.compose.service=" + service,
                  "--filter", "label=com.docker.compose.oneoff=False").splitlines()
    require(len(ids) <= 1 and all(re.fullmatch(r"[a-f0-9]{12,64}", i) for i in ids),
            "ambiguous resident service identity")
    if not ids:
        return None
    obj = json.loads(command("docker", "inspect", "--format", "{{json .}}", ids[0]))
    labels = obj["Config"]["Labels"]
    require(labels.get("com.docker.compose.project") == project
            and labels.get("com.docker.compose.service") == service
            and labels.get("com.docker.compose.oneoff") == "False",
            "foreign Docker project or oneoff identity")
    return ids[0], obj


def main() -> None:
    require(len(sys.argv) == 2 and sys.argv[1] in WORKERS,
            "one allowlisted worker service argument required")
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
            "finite worker only in isolated GitHub CI DEV")
    require(FILE.is_file(), "private Compose file missing")
    project = f"jornada-workers-e2e-ci{run}{attempt}"
    infra = {}
    for name in INFRA:
        service = project_container(project, name)
        require(service is not None and service[1]["State"]["Running"] is True
                and service[1]["State"].get("Health", {}).get("Status") == "healthy",
                "private SQL/API infrastructure not healthy: " + name)
        infra[name] = (service[0], service[1]["State"]["Pid"], service[1]["RestartCount"])
    for worker in WORKERS:
        resident = project_container(project, worker)
        require(resident is None or resident[1]["State"]["Running"] is False,
                "resident worker is running while RunOnce requested: " + worker)
    oneoffs = command("docker", "ps", "-aq",
                      "--filter", "label=com.docker.compose.project=" + project,
                      "--filter", "label=com.docker.compose.oneoff=True").splitlines()
    require(len(oneoffs) == 0, "concurrent/leftover finite worker found in E2E project")

    worker, flag = WORKERS[sys.argv[1]]
    compose = ("docker", "compose", "--env-file", "/dev/null", "--profile",
               "continuous", "-p", project, "-f", str(FILE))
    result = subprocess.run([
        *compose, "run", "--rm", "--no-deps", "-T",
        "-e", "JORNADA_WORKERS_E2E_RUN_ONCE=true",
        "-e", "JORNADA_WORKERS_E2E_RUN_ONCE_ALLOWED=true",
        "-e", "JORNADA_WORKERS_E2E_ID=ci" + run + attempt,
        "-e", flag + "=true",
        "-e", flag.replace("RunOnce", "RunOnceMaxSeconds") + "=65",
        sys.argv[1], worker, "--run-once"
    ], cwd=ROOT, stdin=subprocess.DEVNULL,
       capture_output=True, check=False, timeout=95)
    # Keep bounded logs without publishing secrets. The subprocess exit code
    # (zero only) is the sole proof that the .NET finite path returned success.
    require(result.returncode == 0, "finite worker execution did not complete successfully")

    for name, (cid, pid, restarts) in infra.items():
        current = project_container(project, name)
        require(current is not None and current[0] == cid
                and current[1]["State"]["Running"] is True
                and current[1]["State"]["Pid"] == pid
                and current[1]["RestartCount"] == restarts,
                "SQL/API/Resultado restarted during RunOnce: " + name)
    extra = command("docker", "ps", "-aq",
                    "--filter", "label=com.docker.compose.project=" + project,
                    "--filter", "label=com.docker.compose.oneoff=True")
    require(not extra, "RunOnce did not clean up its temporary worker container")
    print("C3.3b2: PASS private finite " + sys.argv[1] +
          " worker exit=0 without resident/SQL/API interference")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, KeyError, ValueError, OSError, subprocess.TimeoutExpired) as ex:
        print("C3.3b2: REJECTED " + type(ex).__name__, file=sys.stderr)
        sys.exit(2)
