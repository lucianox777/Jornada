#!/usr/bin/env python3
"""C3.3b2: real private HTTP RunOnce of three .NET workers, no other project."""
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
OUT = ROOT / ".local/e2e/c3-3b2-three-runonce"
WORKERS = ("processor", "operations-maintenance", "bronze-maintenance")


def require(ok: bool, message: str) -> None:
    if not ok:
        raise RuntimeError(message)


def http(base: str, path: str, method: str = "GET", payload: dict | None = None) -> tuple[int, dict]:
    body = None if payload is None else json.dumps(payload).encode()
    req = request.Request(base + path, data=body, method=method,
                          headers={"Content-Type": "application/json"})
    try:
        response = request.urlopen(req, timeout=120)
    except error.HTTPError as exc:
        response = exc
    with response:
        return response.status, json.loads(response.read())


def good(base: str, path: str, method: str = "GET", payload: dict | None = None) -> dict:
    code, obj = http(base, path, method, payload)
    require(code == 200, "private Console HTTP failed: " + path)
    return obj


def state(base: str, expected: str) -> dict:
    result = good(base, "/api/workers/supervisor")
    require(result.get("mode") == expected,
            "global effective mode mismatch: " + repr(result.get("mode")))
    require(result.get("sqlRunning") is True
            and result.get("apiReady") is True
            and result.get("resultadoApiLive") is True,
            "API/Resultado/SQL did not remain independently ready")
    actual = result.get("workers")
    require(isinstance(actual, list) and len(actual) == 3
            and {x.get("worker") for x in actual} == set(WORKERS),
            "three independent worker identities are required")
    if expected == "OFF":
        require(all(x.get("state") == "PARADO"
                    and x.get("hostPid") is None for x in actual),
                "resident worker unexpectedly alive while OFF")
    else:
        require(all(x.get("state") == "ATIVO"
                    and isinstance(x.get("hostPid"), int)
                    and x["hostPid"] > 1
                    and isinstance(x.get("restartCount"), int)
                    and x.get("heartbeatAgeSeconds") is not None
                    and 0 <= x["heartbeatAgeSeconds"] <= 45 for x in actual),
                "ON worker missing PID, restart policy or live heartbeat")
    return result


def main() -> None:
    require(len(sys.argv) == 1, "no arguments accepted")
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == "ci" + run + attempt
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "only disposable GitHub DEV E2E can run finite workers")
    require(DLL.is_file(), "Console Release DLL must be already built by existing E2E")
    OUT.mkdir(parents=True, exist_ok=True)
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    env = {**os.environ, "JORNADA_RUNTIME_MODE": "DEV",
           "ASPNETCORE_ENVIRONMENT": "Development",
           "DOTNET_ENVIRONMENT": "Development",
           "ASPNETCORE_URLS": base}
    with (OUT / "devconsole-runonce.log").open("wb") as log:
        console = subprocess.Popen(["dotnet", str(DLL)], cwd=ROOT, env=env,
                                   stdin=subprocess.DEVNULL,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 35
            while True:
                require(console.poll() is None, "Console exited")
                try:
                    code, body = http(base, "/api/version")
                    if code == 200 and body.get("mode") == "DEV":
                        break
                except (OSError, ValueError):
                    pass
                require(time.monotonic() < deadline, "Console RunOnce HTTP unavailable")
                time.sleep(0.25)

            initial = state(base, "ON")
            for worker in WORKERS:
                code, _ = http(base, "/api/workers/" + worker + "/run-once", "POST")
                require(code == 409, "ON must reject all three RunOnce workers")
            bad_code, _ = http(base, "/api/workers/unknown/run-once", "POST")
            require(bad_code == 409, "unknown worker must not invoke a container")

            off = good(base, "/api/workers/supervisor", "POST", {"mode": "OFF"})
            require(off.get("mode") == "OFF", "global OFF transition failed")
            state(base, "OFF")

            summaries = {}
            for worker in WORKERS:
                started = time.monotonic()
                result = good(base, "/api/workers/" + worker + "/run-once", "POST")
                require(result == {"worker": worker, "state": "CONCLUIDO", "exitCode": 0},
                        "finite worker failed to return successful .NET exit")
                summaries[worker] = {"response": result,
                                     "elapsed_seconds": round(time.monotonic() - started, 3)}
                state(base, "OFF")  # no resident or ghost after a finite run
                print("C3.3b2: " + worker + " private RunOnce .NET exit=0; supervisor OFF")
            after = good(base, "/api/workers/supervisor", "POST", {"mode": "ON"})
            require(after.get("mode") == "ON", "supervisor could not re-arm after finite runs")
            final = state(base, "ON")
            original = {x["worker"]: x["hostPid"] for x in initial["workers"]}
            for x in final["workers"]:
                require(x["hostPid"] != original[x["worker"]],
                        "ON did not start fresh resident worker after RunOnce")
            (OUT / "summary.json").write_text(json.dumps({
                "status": "PASS", "project": f"jornada-workers-e2e-ci{run}{attempt}",
                "three_workers_finite_exit_zero": summaries,
                "mode_sequence": ["ON", "OFF", "PROCESSOR_ONCE", "OPERATIONS_ONCE",
                                  "BRONZE_ONCE", "ON"],
                "runonce_rejected_while_on": True,
                "no_resident_during_runonce": True,
                "sql_api_resultado_remained_ready": True,
                "supervisor_restored_on": True
            }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print("C3.3b2: PASS three real isolated worker RunOnce cycles, "
                  "ON rejected, OFF preserved, ON restored")
        finally:
            if console.poll() is None:
                console.terminate()
                try:
                    console.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    console.kill()
                    console.wait(timeout=5)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError, KeyError) as ex:
        print("C3.3b2: REJECTED " + type(ex).__name__, file=sys.stderr)
        sys.exit(2)
