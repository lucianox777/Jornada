#!/usr/bin/env python3
"""Offline C3.3b3b0: ensure denied inputs cannot reach Docker, without SQL/host."""
from pathlib import Path
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
HELPER = ROOT / "scripts/console-private-worker-cancel-oneoff.py"


def reject(args: tuple[str, ...], env: dict[str, str]) -> None:
    # Path excludes docker on purpose; a Docker invocation means fail.
    p = subprocess.run([sys.executable, str(HELPER), *args],
                       cwd=ROOT,
                       env={"PATH": "", "HOME": "/tmp", **env},
                       capture_output=True, text=True, check=False, timeout=5)
    assert p.returncode == 2, f"unexpected helper admission: {p.returncode}"
    assert "REJECTED" in p.stderr and "Traceback" not in p.stderr
    assert not p.stdout, "negative run leaked output"


def main() -> None:
    text = HELPER.read_text(encoding="utf-8")
    compile(text, str(HELPER), "exec")
    for token in (
        'JORNADA_WORKERS_E2E_CANCEL_ALLOWED") == "true"',
        'JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"',
        'GITHUB_REPOSITORY") == "lucianox777/Jornada"',
        'JORNADA_RUNTIME_MODE") == "DEV"',
        'not os.environ.get("DOCKER_HOST")',
        'not os.environ.get("DOCKER_CONTEXT")',
        'label=com.docker.compose.project=',
        'label=com.docker.compose.oneoff=',
        'host["RestartPolicy"]["Name"] == "no"',
        'int(state["Pid"]) == int(pid_text)',
        '"--run-once" in (obj["Config"].get("Cmd") or [])',
        'set(networks) == {project + "_default"}',
        'not host.get("PortBindings")',
        'm.get("Type") != "bind"',
        'run("docker", "stop", "--time", "6", cid, timeout=18)',
        'time.monotonic() + 30',
        'require(infrastructure(project) == before,',
        'docker compose --rm',
    ):
        assert token in text, "required exact identity/exit/cleanup guard missing: " + token
    for forbidden in ("docker prune", "docker compose down", "JornadaLocal",
                      "kill -9", "os.system(", "shell=True", "DOCKER_HOST="):
        assert forbidden not in text, "unsafe broad Docker or local-data operation"
    fake_id = "a" * 64
    fake_run = "123e4567-e89b-42d3-a456-426614174000"
    reject((), {})
    reject(("processor", fake_id, "123", fake_run), {})
    reject(("sqlserver", fake_id, "123", fake_run), {})
    fake_ci = {
        "GITHUB_ACTIONS": "true", "CI": "true",
        "GITHUB_REPOSITORY": "lucianox777/Jornada",
        "GITHUB_RUN_ID": "38013214019", "GITHUB_RUN_ATTEMPT": "1",
        "JORNADA_RUNTIME_MODE": "DEV",
        "JORNADA_WORKERS_E2E_RUNTIME_TEST": "true",
        "JORNADA_WORKERS_E2E_ID": "ci380132140191",
        "JORNADA_WORKERS_E2E_SQL_PASSWORD": "synthetic_only",
    }
    reject(("processor", fake_id, "123", fake_run), fake_ci)
    reject(("processor", fake_id, "123", fake_run),
           {**fake_ci, "JORNADA_WORKERS_E2E_CANCEL_ALLOWED": "true",
            "DOCKER_HOST": "tcp://host.invalid:2376"})
    reject(("processor", fake_id, "123", fake_run),
           {**fake_ci, "JORNADA_WORKERS_E2E_CANCEL_ALLOWED": "true",
            "GITHUB_REPOSITORY": "not/Jornada"})
    # Every CI admission condition is independently fail-closed, even with
    # the explicit cancellation opt-in. These tests never invoke Docker.
    allowed = {**fake_ci, "JORNADA_WORKERS_E2E_CANCEL_ALLOWED": "true"}
    for key, invalid in (
        ("GITHUB_ACTIONS", "false"),
        ("CI", "false"),
        ("GITHUB_RUN_ID", "not-a-run"),
        ("GITHUB_RUN_ATTEMPT", "0"),
        ("JORNADA_RUNTIME_MODE", "PROD"),
        ("JORNADA_WORKERS_E2E_RUNTIME_TEST", "false"),
        ("JORNADA_WORKERS_E2E_ID", "ci-foreign"),
        ("JORNADA_WORKERS_E2E_SQL_PASSWORD", ""),
        ("JORNADA_WORKERS_E2E_IMAGE_TAG", "production"),
        ("DOCKER_CONTEXT", "remote"),
    ):
        reject(("processor", fake_id, "123", fake_run), {**allowed, key: invalid})
    # Absence is distinct from an invalid value: every required attestation
    # must be present, and a default image tag must not grant host access.
    for key in (
        "GITHUB_ACTIONS", "CI", "GITHUB_REPOSITORY", "GITHUB_RUN_ID",
        "GITHUB_RUN_ATTEMPT", "JORNADA_RUNTIME_MODE",
        "JORNADA_WORKERS_E2E_RUNTIME_TEST",
        "JORNADA_WORKERS_E2E_CANCEL_ALLOWED", "JORNADA_WORKERS_E2E_ID",
        "JORNADA_WORKERS_E2E_SQL_PASSWORD",
    ):
        missing = allowed.copy()
        del missing[key]
        reject(("processor", fake_id, "123", fake_run), missing)
    reject(("processor", fake_id, "123", fake_run),
           {**allowed, "GITHUB_RUN_ATTEMPT": "2"})
    reject(("processor", fake_id, "123", fake_run),
           {**allowed, "GITHUB_RUN_ID": "38013214020"})
    for bad_args in (
        ("processor", "not-a-container", "123", fake_run),
        ("processor", fake_id, "0", fake_run),
        ("processor", fake_id, "123", "not-a-uuid"),
        ("operations-maintenance", fake_id, "123", fake_run, "extra"),
    ):
        reject(bad_args, allowed)
    print("C3.3b3b0: PASS cancelled oneoff helper refuses unconfirmed/foreign host (offline)")


if __name__ == "__main__":
    main()
