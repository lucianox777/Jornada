#!/usr/bin/env python3
"""T0.1a: black-box HTTP acceptance for the real DevConsole process.

The check intentionally calls only read-only or proven-blocked endpoints.
It neither starts an operational command nor connects to SQL or Bronze.
Run after a Release build, and only on an isolated CI/temporary host.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
from urllib import error, request
import uuid

ROOT = Path(__file__).resolve().parents[1]
ASSEMBLY = ROOT / "src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"
EVIDENCE = ROOT / ".local/test-evidence/unit/devconsole-http-acceptance.json"


def assert_true(condition: bool, description: str) -> None:
    if not condition:
        raise AssertionError(description)


def spare_local_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def http(base: str, method: str, path: str, body: object = None) -> tuple[int, object]:
    headers = {}
    payload = None
    if body is not None:
        payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    call = request.Request(base + path, data=payload, headers=headers, method=method)
    try:
        response = request.urlopen(call, timeout=8)
    except error.HTTPError as exc:
        response = exc
    with response:
        content = response.read().decode("utf-8")
        mime = response.headers.get("Content-Type", "")
        result = json.loads(content) if "application/json" in mime and content else content
        return response.status, result


def run_mode(mode: str) -> dict:
    checks: list[str] = []
    with tempfile.TemporaryDirectory(prefix="jornada-console-http-") as folder:
        data = Path(folder)
        port = spare_local_port()
        base = f"http://127.0.0.1:{port}"
        env = dict(os.environ)
        env.update({
            "ASPNETCORE_URLS": base,
            "ASPNETCORE_ENVIRONMENT": "Development",
            "DOTNET_ENVIRONMENT": "Development",
            "JORNADA_RUNTIME_MODE": mode,
            "XDG_DATA_HOME": str(data / "appdata"),
            "HOME": str(data / "home"),
        })
        (data / "home").mkdir()
        log_path = data / "console.log"
        with log_path.open("wb") as log:
            process = subprocess.Popen(
                ["dotnet", str(ASSEMBLY)], cwd=ROOT, env=env,
                stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
            )
            try:
                deadline = time.monotonic() + 35
                while True:
                    if process.poll() is not None:
                        raise RuntimeError(f"Console exited during startup (rc={process.returncode})")
                    try:
                        status, version = http(base, "GET", "/api/version")
                        if status == 200:
                            break
                    except (error.URLError, TimeoutError, ConnectionError):
                        pass
                    if time.monotonic() >= deadline:
                        raise TimeoutError("Console did not serve /api/version on localhost")
                    time.sleep(0.2)

                assert_true(isinstance(version, dict) and version.get("mode") == mode,
                            f"{mode}: incorrect runtime mode")
                assert_true(isinstance(version.get("processId"), int),
                            f"{mode}: missing process id")
                checks.append("real HTTP /api/version")

                status, commands = http(base, "GET", "/api/commands")
                assert_true(status == 200 and isinstance(commands, list) and len(commands) >= 10,
                            f"{mode}: invalid command catalog")
                ids = [item["id"] for item in commands]
                assert_true(len(ids) == len(set(ids)), f"{mode}: duplicate command ids")
                assert_true([item["order"] for item in commands] == list(range(len(commands))),
                            f"{mode}: unstable catalog ordering")
                assert_true([ids.index(k) for k in ("infrastructure", "zip", "bronze", "silver", "linkage", "gold")]
                            == sorted(ids.index(k) for k in ("infrastructure", "zip", "bronze", "silver", "linkage", "gold")),
                            f"{mode}: pipeline order changed")
                by_id = {item["id"]: item for item in commands}
                assert_true(by_id["silver"]["executionMode"] == "RUN_ONCE",
                            f"{mode}: Silver is not RunOnce")
                assert_true(by_id["linkage"]["executionMode"] == "ONE_SHOT",
                            f"{mode}: Linkage is not one-shot")
                assert_true(by_id["infrastructure"]["executionMode"] == "SEQUENCE",
                            f"{mode}: infrastructure mode changed")
                checks.append("command catalog + RunOnce + ordering")

                status, page = http(base, "GET", "/")
                assert_true(status == 200 and isinstance(page, str)
                            and ("<html" in page.lower()), f"{mode}: main page not HTML")
                checks.append("real HTML entrypoint")

                status, runs = http(base, "GET", "/api/runs")
                assert_true(status == 200 and isinstance(runs, list) and not runs,
                            f"{mode}: expected isolated empty session")
                status, activity = http(base, "GET", "/api/activity")
                assert_true(status == 200 and isinstance(activity, list),
                            f"{mode}: activity endpoint failed")
                checks.append("isolated activity and runs")

                status, _ = http(base, "GET", "/api/runs/" + str(uuid.uuid4()) + "/result")
                assert_true(status == 404, f"{mode}: unknown run returned {status}")
                status, _ = http(base, "POST", "/api/commands/absent-command/start")
                assert_true(status == 404, f"{mode}: missing command returned {status}")
                checks.append("missing run/command fail closed")

                if mode == "DEV":
                    assert_true(by_id["linkage"]["disabled"] is True,
                                "Linkage without Silver is not blocked in catalog")
                    status, blocked = http(base, "POST", "/api/commands/linkage/start")
                    assert_true(status == 409 and isinstance(blocked, dict)
                                and "Envie um arquivo de ingestão" in blocked.get("error", ""),
                                "Linkage accepted before ingest/Silver")
                    status, _ = http(base, "POST", "/api/session-counts/reset", ["silver"])
                    assert_true(status == 200, "session counter reset failed")
                    checks.append("precondition enforcement + in-memory counter reset")
                else:
                    for command in ("finish", "reset-environment"):
                        assert_true(by_id[command]["disabled"] is True,
                                    f"PROD: destructive {command} enabled in catalog")
                        status, blocked = http(base, "POST", f"/api/commands/{command}/start")
                        assert_true(status == 409 and isinstance(blocked, dict)
                                    and blocked.get("mode") == "PROD",
                                    f"PROD: destructive {command} was not rejected")
                    checks.append("PROD destructive operations fail closed")
                return {"mode": mode, "checks": checks, "status": "PASS"}
            except BaseException as exc:
                log.flush()
                transcript = log_path.read_text(encoding="utf-8", errors="replace")[-3500:]
                raise RuntimeError(f"HTTP acceptance {mode}: {exc}\nConsole log:\n{transcript}") from exc
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=6)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=6)


def main() -> int:
    if os.environ.get("JORNADA_CONSOLE_ACCEPTANCE_ISOLATED") != "true":
        raise SystemExit("REFUSED: set JORNADA_CONSOLE_ACCEPTANCE_ISOLATED=true for an isolated test host")
    if not ASSEMBLY.is_file():
        raise SystemExit(f"Missing Release build: {ASSEMBLY}")
    results = [run_mode("DEV"), run_mode("PROD")]
    summary = {"status": "PASS", "scope": "T0.1a DevConsole HTTP isolated",
               "levels": ["Console HTTP"], "modes": results,
               "note": "Not a full ZIP/SQL/Silver/Linkage/Gold end-to-end test."}
    EVIDENCE.parent.mkdir(parents=True, exist_ok=True)
    EVIDENCE.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"DEVCONSOLE HTTP ACCEPTANCE: PASS ({sum(len(row['checks']) for row in results)} assertions groups)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
