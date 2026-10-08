#!/usr/bin/env python3
"""T0.1a: black-box HTTP acceptance for the real DevConsole process.

This test uses only read-only, proven-blocked and local ZIP generation endpoints.
It never sends a ZIP to the API or connects to SQL, Bronze or linkage.
Run after a Release build, and only on an isolated CI/temporary host.
"""
from __future__ import annotations

import hashlib
import io
import json
import os
import re
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
from urllib import error, request
import uuid
import zipfile

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


def zip_download(base: str, execution_id: str) -> tuple[bytes, str]:
    with request.urlopen(base + f"/api/runs/{execution_id}/result", timeout=10) as response:
        assert_true(response.status == 200, "ZIP download did not return 200")
        assert_true("application/zip" in response.headers.get("Content-Type", ""),
                    "ZIP download has incorrect media type")
        return response.read(), response.headers.get("Content-Disposition", "")


def assert_stream_receipt(base: str, execution_id: str, status_expected: str) -> None:
    """Read the real SSE stream after completion, as a history/stream reconnect.

    A completed run remains readable from the live in-process stream.
    The UI depends on a terminal status event and strictly monotonic IDs.
    """
    with request.urlopen(base + f"/api/runs/{execution_id}/stream", timeout=12) as response:
        assert_true(response.status == 200
                    and "text/event-stream" in response.headers.get("Content-Type", ""),
                    "Run SSE endpoint did not return event-stream")
        frames = response.read().decode("utf-8").replace("\r\n", "\n").strip().split("\n\n")
    entries: list[tuple[int, dict]] = []
    for frame in frames:
        fields = dict(line.split(": ", 1) for line in frame.split("\n") if ": " in line)
        assert_true("id" in fields and "data" in fields,
                    "SSE frame is missing event ID or payload")
        seq = int(fields["id"])
        item = json.loads(fields["data"])
        assert_true(item.get("seq") == seq and isinstance(item.get("at"), str)
                    and isinstance(item.get("text"), str),
                    "SSE event serialization does not match the Console contract")
        entries.append((seq, item))
    assert_true(len(entries) >= 3, "SSE has too few events for a real ZIP run")
    seqs = [seq for seq, _ in entries]
    assert_true(seqs == sorted(set(seqs)) and seqs[0] == 1,
                "SSE IDs must start at 1, be strictly increasing and have no duplicates")
    assert_true(entries[-1][1].get("stream") == "status"
                and status_expected in entries[-1][1].get("text", ""),
                "Run SSE never produced the expected terminal status")
    assert_true(sum(item.get("stream") == "status" for _, item in entries) == 1,
                "Run SSE must terminate with exactly one status event")


def await_zip_result(base: str, execution_id: str) -> dict:
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        status, result = http(base, "GET", f"/api/runs/{execution_id}")
        if status == 200 and isinstance(result, dict):
            return result
        assert_true(status == 404, f"Unknown run status: {status}")
        time.sleep(0.15)
    raise TimeoutError("ZIP generation did not complete within 45 seconds")


def test_manual_zip(base: str, fixture_name: str, *, expect_facts: bool) -> None:
    fixture = ROOT / "tests/fixtures/ingestao" / fixture_name
    manifest = (fixture / "manifest.json").read_text(encoding="utf-8")
    people = (fixture / "pessoas.jsonl").read_text(encoding="utf-8")
    facts = (fixture / "registros.jsonl").read_text(encoding="utf-8")
    status, started = http(base, "POST", "/api/zip/manual/start", {
        "gestor": "SEHAB", "manifestJson": manifest,
        "pessoasJsonl": people, "registrosJsonl": facts
    })
    assert_true(status == 202 and isinstance(started, dict)
                and started.get("id") and started.get("executionNumber"),
                f"{fixture_name}: ZIP start did not return an accepted execution")
    run = await_zip_result(base, started["id"])
    assert_true(run.get("status") == "SUCESSO", f"{fixture_name}: ZIP failed: {run.get('summary')}")
    assert_true(run.get("command") == "zip"
                and run.get("step", {}).get("exitCode") == 0,
                f"{fixture_name}: ZIP lacks successful execution receipt")
    assert_stream_receipt(base, started["id"], "SUCESSO")
    data, disposition = zip_download(base, started["id"])
    with request.urlopen(base + f"/api/runs/{started['id']}/artifacts/0", timeout=10) as artifact:
        assert_true(artifact.status == 200 and artifact.read() == data,
                    f"{fixture_name}: history artifact differs from result download")
    archive_sha = hashlib.sha256(data).hexdigest()
    assert_true(bool(re.search(r"_[a-f0-9]{64}\.zip", disposition))
                and archive_sha in disposition,
                f"{fixture_name}: file name does not contain real ZIP checksum")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        assert_true(archive.namelist() == ["manifest.json", "pessoas.jsonl", "registros.jsonl"],
                    f"{fixture_name}: ZIP file layout mismatch")
        assert_true(archive.testzip() is None, f"{fixture_name}: corrupted ZIP member")
        assert_true(json.loads(archive.read("manifest.json")) == json.loads(manifest),
                    f"{fixture_name}: manifest not preserved")
        actual_people = archive.read("pessoas.jsonl").decode("utf-8").strip()
        assert_true(actual_people == people.strip(), f"{fixture_name}: person data changed")
        actual_facts = archive.read("registros.jsonl").decode("utf-8").strip()
        assert_true(actual_facts == facts.strip(), f"{fixture_name}: facts changed")
        assert_true(bool(actual_facts) == expect_facts,
                    f"{fixture_name}: expected {'facts' if expect_facts else 'empty facts file'}")
    status, records = http(base, "GET", "/api/runs")
    assert_true(status == 200 and any(r.get("id") == started["id"] for r in records),
                f"{fixture_name}: ZIP is missing from session history")


def test_invalid_manual_zip(base: str) -> None:
    fixture = ROOT / "tests/fixtures/ingestao/AA01_SEM_FATOS_v2"
    valid_manifest = (fixture / "manifest.json").read_text(encoding="utf-8")
    status, started = http(base, "POST", "/api/zip/manual/start", {
        "gestor": "SEHAB", "manifestJson": valid_manifest,
        "pessoasJsonl": '{"idPessoaEntrega":', "registrosJsonl": ""
    })
    assert_true(status == 202 and isinstance(started, dict),
                "Malformed JSONL should produce an observable async run")
    run = await_zip_result(base, started["id"])
    assert_true(run.get("status") == "FALHA"
                and run.get("step", {}).get("resultPath") is None,
                "Malformed JSONL was not rejected before ZIP creation")
    assert_stream_receipt(base, started["id"], "FALHA")
    status, _ = http(base, "GET", f"/api/runs/{started['id']}/result")
    assert_true(status == 404, "Failed ZIP created a downloadable artifact")
    status, _ = http(base, "GET", f"/api/runs/{started['id']}/artifacts/0")
    assert_true(status == 404, "Failed ZIP leaked a historical artifact")
    status, history = http(base, "GET", "/api/runs")
    assert_true(status == 200 and any(
        item.get("id") == started["id"] and item.get("status") == "FALHA"
        for item in history), "Failed ZIP execution missing from session history")


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

                missing_id = str(uuid.uuid4())
                status, _ = http(base, "GET", "/api/runs/" + missing_id + "/result")
                assert_true(status == 404, f"{mode}: unknown run returned {status}")
                status, _ = http(base, "GET", "/api/runs/" + missing_id + "/stream")
                assert_true(status == 404, f"{mode}: unknown SSE stream returned {status}")
                status, _ = http(base, "POST", "/api/commands/absent-command/start")
                assert_true(status == 404, f"{mode}: missing command returned {status}")
                checks.append("missing run/command fail closed")

                # Contract file API: read-only catalog and fail-closed writes.
                # Never mutate the repository's active contract fixtures.
                status, contract_paths = http(base, "GET", "/api/contracts")
                assert_true(status == 200 and isinstance(contract_paths, list),
                            f"{mode}: contract file catalog is unavailable")
                if contract_paths:
                    selected = contract_paths[0]
                    from urllib.parse import quote
                    status, contract_file = http(
                        base, "GET", "/api/contracts/file?path=" + quote(selected, safe="")
                    )
                    assert_true(status == 200 and isinstance(contract_file, dict)
                                and contract_file.get("path") == selected,
                                f"{mode}: contract file cannot be read")
                    original = contract_file.get("content")
                    assert_true(isinstance(original, str),
                                f"{mode}: contract file response has no content")
                    status, _ = http(base, "PUT", "/api/contracts/file", {
                        "path": selected, "content": '{"invalid":'
                    })
                    assert_true(status >= 400,
                                f"{mode}: invalid JSON contract write was accepted")
                    status, reread = http(
                        base, "GET", "/api/contracts/file?path=" + quote(selected, safe="")
                    )
                    assert_true(status == 200 and reread.get("content") == original,
                                f"{mode}: rejected JSON modified the contract")
                status, _ = http(base, "PUT", "/api/contracts/file", {
                    "path": "../outside.json", "content": "{}"
                })
                assert_true(status >= 400,
                            f"{mode}: contract path traversal was accepted")
                checks.append("contract catalog, read-only inspection, invalid JSON and traversal rejection")
                # The editor must never create a missing contract or accept
                # a JSON file outside the allowlisted contract directory.
                missing_contract = "config/contracts/__console_acceptance_missing__.json"
                status, _ = http(base, "PUT", "/api/contracts/file", {
                    "path": missing_contract, "content": "{}"
                })
                assert_true(status >= 400,
                            f"{mode}: editor created an unapproved contract file")
                status, _ = http(base, "GET",
                                 "/api/contracts/file?path=config%2Fcontracts%2F__console_acceptance_missing__.json")
                assert_true(status >= 400,
                            f"{mode}: missing contract was unexpectedly readable")
                status, _ = http(base, "PUT", "/api/contracts/file", {
                    "path": "config/contracts/../../config/active.json",
                    "content": "{}"
                })
                assert_true(status >= 400,
                            f"{mode}: nested traversal escaped the contract root")
                status, _ = http(base, "PUT", "/api/contracts/file", {
                    "path": "config/contracts/invalid.txt", "content": "{}"
                })
                assert_true(status >= 400,
                            f"{mode}: non-JSON extension was accepted")
                checks.append("contract editor rejects missing files, nested traversal and non-JSON extensions")


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
                    # Exercise the real Console -> local ZIP -> HTTP download chain,
                    # without invoking the ingest API or touching shared SQL/IBGE.
                    test_manual_zip(base, "AA01_SEM_FATOS_v2", expect_facts=False)
                    checks.append("manual ZIP: Pessoa-only + empty registros.jsonl")
                    test_manual_zip(base, "AA01_v2", expect_facts=True)
                    checks.append("manual ZIP: Pessoa + fato + SHA-256")
                    test_invalid_manual_zip(base)
                    checks.append("malformed JSONL: fail closed and no artifact")
                    checks.append("SSE replay + monotonic event IDs + terminal status + history artifacts")
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
