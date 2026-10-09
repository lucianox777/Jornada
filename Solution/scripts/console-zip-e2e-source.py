#!/usr/bin/env python3
"""Generate the exact ZIP used by the isolated SQL E2E via real Console HTTP.

Not a substitute for the backend E2E: this script only supplies its ZIP input,
which the existing local-e2e.sh then ingests, processes and verifies in SQL.
This helper never invokes DB reset/clean or a worker.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import tempfile
import time
from urllib import error, request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ASSEMBLY = ROOT / "src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"


def require(check: bool, message: str) -> None:
    if not check:
        raise RuntimeError(message)


def local_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def call_json(base: str, method: str, path: str, payload: dict | None = None) -> tuple[int, dict]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    req = request.Request(base + path, data=body, method=method,
                          headers={"Content-Type": "application/json"} if body is not None else {})
    try:
        resp = request.urlopen(req, timeout=10)
    except error.HTTPError as exc:
        resp = exc
    with resp:
        data = resp.read()
        value = json.loads(data) if data else {}
        return resp.status, value


def generate(fixture: Path, output: Path, summary_path: Path) -> None:
    require(os.environ.get("JORNADA_E2E_CONSOLE_ZIP") == "true",
            "Refusing to run without isolated JORNADA_E2E_CONSOLE_ZIP=true")
    require(ASSEMBLY.is_file(), "Release build of DevConsole is required")
    require(fixture.is_dir(), "ZIP fixture must exist")
    output.mkdir(parents=True, exist_ok=True)
    manifest = (fixture / "manifest.json").read_text(encoding="utf-8")
    people = (fixture / "pessoas.jsonl").read_text(encoding="utf-8")
    facts = (fixture / "registros.jsonl").read_text(encoding="utf-8")
    expected = {"manifest.json": manifest, "pessoas.jsonl": people, "registros.jsonl": facts}

    with tempfile.TemporaryDirectory(prefix="jornada-console-e2e-") as td:
        tmp = Path(td)
        (tmp / "home").mkdir()
        base = f"http://127.0.0.1:{local_port()}"
        env = dict(os.environ)
        env.update({
            "ASPNETCORE_URLS": base,
            "ASPNETCORE_ENVIRONMENT": "Development",
            "DOTNET_ENVIRONMENT": "Development",
            "JORNADA_RUNTIME_MODE": "DEV",
            "XDG_DATA_HOME": str(tmp / "appdata"),
            "HOME": str(tmp / "home"),
            "JORNADA_E2E_SQL_DATABASE": "JornadaE2E",
            "JORNADA_E2E_API_URL": "http://127.0.0.1:5088",
            "JORNADA_E2E_IDEMPOTENCY_KEY": "local-e2e-001"
        })
        logpath = tmp / "console.log"
        with logpath.open("wb") as log:
            proc = subprocess.Popen(
                ["dotnet", str(ASSEMBLY)], cwd=ROOT, env=env,
                stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT
            )
            try:
                deadline = time.monotonic() + 35
                while True:
                    require(proc.poll() is None, f"DevConsole exited early: {proc.returncode}")
                    try:
                        status, v = call_json(base, "GET", "/api/version")
                        if status == 200 and v.get("mode") == "DEV":
                            break
                    except (error.URLError, TimeoutError, ConnectionError, ValueError):
                        pass
                    require(time.monotonic() < deadline, "DevConsole failed to start")
                    time.sleep(0.2)

                status, started = call_json(base, "POST", "/api/zip/manual/start", {
                    "gestor": "SEHAB", "manifestJson": manifest,
                    "pessoasJsonl": people, "registrosJsonl": facts
                })
                require(status == 202 and started.get("id"), "Console did not accept ZIP generation")
                execution_id = started["id"]
                deadline = time.monotonic() + 60
                while True:
                    status, run = call_json(base, "GET", f"/api/runs/{execution_id}")
                    if status == 200:
                        break
                    require(status == 404, f"Unexpected run status {status}")
                    require(time.monotonic() < deadline, "Timed out waiting for Console ZIP")
                    time.sleep(0.2)
                require(run.get("status") == "SUCESSO"
                        and run.get("step", {}).get("exitCode") == 0,
                        f"Console ZIP run was not successful: {run.get('status')}")

                with request.urlopen(base + f"/api/runs/{execution_id}/result", timeout=20) as resp:
                    require(resp.status == 200
                            and "application/zip" in resp.headers.get("Content-Type", ""),
                            "Console ZIP download failed")
                    disposition = resp.headers.get("Content-Disposition", "")
                    content = resp.read()
                match = re.search(r'filename="?([^";]+)', disposition, re.I)
                require(match is not None, "Console did not return ZIP download filename")
                filename = match.group(1)
                require(bool(re.fullmatch(r"ENTREGA_SEHAB_[A-Za-z0-9_-]+_v[0-9]+_[0-9a-f]{64}\.zip", filename)),
                        "Unexpected Console download filename")
                sha = hashlib.sha256(content).hexdigest()
                require(filename.endswith(f"_{sha}.zip"), "ZIP filename SHA-256 mismatch")
                with zipfile.ZipFile(io.BytesIO(content)) as archive:
                    require(archive.namelist() == list(expected), "Console ZIP layout invalid")
                    require(archive.testzip() is None, "Console ZIP CRC check failed")
                    if not facts.strip():
                        require(archive.getinfo("registros.jsonl").file_size == 0,
                                "Pessoa-only ZIP must carry an empty registros.jsonl entry")
                    for name, value in expected.items():
                        require(archive.read(name).decode("utf-8").strip() == value.strip(),
                                f"Console changed {name}")
                target = output / filename
                require(not target.exists(), "Refusing to overwrite existing E2E ZIP")
                target.write_bytes(content)
                report = {
                    "status": "PASS",
                    "source": "DevConsole /api/zip/manual/start + /api/runs/{id}/result",
                    "zipSha256": sha,
                    "zipName": filename,
                    "zipBytes": len(content),
                    "zipMembers": list(expected),
                    "sourceFixture": fixture.name,
                    "integrity": "PASS",
                    "note": "Backend ingestion/Processor/Silver/Gold asserted by local-e2e.sh"
                }
                summary_path.parent.mkdir(parents=True, exist_ok=True)
                summary_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
                # Exercise the real Chromium button against the disposable API.
                # The Console process inherits only the explicitly scoped E2E
                # transport variables; the legacy NODE1 command is never used.
                if os.environ.get("JORNADA_E2E_BROWSER_INGESTION") == "true":
                    pointer = ROOT / ".local/e2e/browser-ingestion-zip-path.txt"
                    pointer.write_text(str(target) + "\n", encoding="utf-8")
                    from playwright.sync_api import sync_playwright
                    with sync_playwright() as playwright:
                        browser = playwright.chromium.launch(headless=True, args=["--no-sandbox"])
                        try:
                            page = browser.new_page()
                            page.goto(base, wait_until="domcontentloaded", timeout=30000)
                            button = page.get_by_role("button", name="Enviar arquivo", exact=True)
                            # The command cards load asynchronously after DOMContentLoaded.
                            # Wait for the actual command UI before asserting uniqueness.
                            button.wait_for(state="visible", timeout=15000)
                            require(button.count() == 1, "Missing or duplicate ingestion button in Chromium")
                            with page.expect_response(lambda r: "/api/commands/ingestion/start" in r.url and r.request.method == "POST", timeout=15000) as started_response:
                                button.click()
                            response = started_response.value
                            require(response.status == 202, f"Browser ingestion command returned HTTP {response.status}")
                            command_id = response.json().get("id")
                            require(command_id, "Browser ingestion command did not return run id")
                            deadline = time.monotonic() + 45
                            while True:
                                status, command_run = call_json(base, "GET", f"/api/runs/{command_id}")
                                if status == 200:
                                    break
                                require(status == 404, f"Unexpected browser command run status: {status}")
                                require(time.monotonic() < deadline, "Browser ingestion command timed out")
                                time.sleep(0.25)
                            require(command_run.get("status") == "SUCESSO", f"Browser ingestion failed: {command_run}")
                            receipt_path = ROOT / ".local/dev-console/last-ingestion.json"
                            require(receipt_path.is_file(), "Browser ingestion receipt missing")
                            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
                            require(receipt.get("database") == "JornadaE2E" and receipt.get("receipt", {}).get("entregaId"),
                                    "Browser ingestion did not persist disposable API receipt")
                            report["browserIngestion"] = {"status": "PASS", "runId": command_id,
                                                           "entregaId": receipt["receipt"]["entregaId"]}
                            summary_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
                        finally:
                            browser.close()
                # Stdout contains only the path consumed by local-e2e.sh.
                print(str(target))
            except BaseException as exc:
                log.flush()
                snippet = logpath.read_text(encoding="utf-8", errors="replace")[-2500:]
                raise RuntimeError(f"Console E2E ZIP source failed: {exc}\n{snippet}") from exc
            finally:
                if proc.poll() is None:
                    proc.terminate()
                    try:
                        proc.wait(timeout=6)
                    except subprocess.TimeoutExpired:
                        proc.kill()
                        proc.wait(timeout=6)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--summary", type=Path, required=True)
    args = parser.parse_args()
    generate(args.fixture.resolve(), args.output_dir.resolve(), args.summary.resolve())
    return 0


if __name__ == "__main__":
    sys.exit(main())
