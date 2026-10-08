#!/usr/bin/env python3
"""Browser-level Console DEV acceptance with real Chromium and real ZIP backend.

Only catalog and Gold example HTTP reads are mocked: those normally require
database access and are deliberately outside this isolated no-SQL test.
All navigation, forms, ZIP commands, SSE, history and download metadata
run against the real DevConsole process on loopback.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import tempfile
import time

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"
OUT = ROOT / ".local/test-evidence/unit/devconsole-browser"

CONTRACT_KEY = "SEHAB:SEHAB:P1:BENEFICIO:AA01:V1"
CONTRACT = {
    "key": CONTRACT_KEY, "label": "SEHAB AA01 v1 — fixture sintética",
    "gestor": "SEHAB", "codigoSistemaOrigem": "SEHAB",
    "pessoaSchemaVersao": 1, "natureza": "BENEFICIO",
    "codigoTipo": "AA01", "tipoVersao": 1,
}
MANIFEST = {
    "formatoVersao": 2, "pessoaSchemaVersao": 1, "codigoSistemaOrigem": "SEHAB",
    "natureza": "BENEFICIO", "codigoTipo": "AA01", "tipoVersao": 1,
    "dataReferencia": "2026-08-27T00:00:00-03:00",
}
PERSON = {
    "idPessoaEntrega": "UI-SYNTH-1", "cpf": "11144477735",
    "cpfAusenteMotivo": None, "nomeCompleto": "Pessoa Browser Sintetica",
    "dataNascimento": "1990-01-02", "nomeMae": "Mae Browser Sintetica",
    "sourceTransactionId": "UI-SYNTH-TX-1", "atributosTransversais": [],
}
FACT = {
    "idPessoaEntrega": "UI-SYNTH-1", "codigoRegistroOrigem": "UI-AA01-1",
    "operacao": "INCLUSAO", "dataInicioConcessao": "2026-08-27",
    "dataEventoConcessao": "2026-08-27", "valorConcedido": 100,
    "situacaoVigencia": "VIGENTE",
}
TEMPLATE = {
    "source": "SYNTHETIC_BROWSER_FIXTURE", "pessoaUuid": "00000000-0000-0000-0000-000000000001",
    "contractKey": CONTRACT_KEY, "contractLabel": CONTRACT["label"],
    "gestor": "SEHAB", "codigoSistemaOrigem": "SEHAB",
    "natureza": "BENEFICIO", "codigoTipo": "AA01", "tipoVersao": 1,
    "pessoaSchemaVersao": 1, "nomeCompleto": PERSON["nomeCompleto"],
    "dataNascimento": PERSON["dataNascimento"], "nomeMae": PERSON["nomeMae"],
    "idPessoaEntrega": PERSON["idPessoaEntrega"], "codigoPessoaOrigem": "UI-SYNTH-SOURCE-1",
    "cpf": PERSON["cpf"], "codigoRegistroOrigem": FACT["codigoRegistroOrigem"],
    "manifestJson": json.dumps(MANIFEST), "pessoasJsonl": json.dumps(PERSON),
    "registrosJsonl": json.dumps(FACT),
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def available_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def install_synthetic_catalog(page) -> None:
    """Intercept ONLY the two DB-bound READ endpoints, not manual ZIP endpoints."""
    def respond(route, payload):
        route.fulfill(status=200, content_type="application/json",
                      body=json.dumps(payload, ensure_ascii=False))
    page.route("**/api/zip/contracts", lambda route: respond(route, [CONTRACT]))
    page.route(re.compile(r"/api/zip/template(?:\\?.*)?$"),
               lambda route: respond(route, TEMPLATE))


def start_zip_dialog(page) -> None:
    page.locator('#commands button[onclick="openZipDialog()"]').click()
    page.locator("#zipDialog").wait_for(state="visible", timeout=15000)
    page.locator("#zipTemplateSource").get_by_text(
        "SYNTHETIC_BROWSER_FIXTURE", exact=False
    ).wait_for(timeout=15000)


def wait_for_result(page, expected: str) -> None:
    page.locator("#consoleView").wait_for(state="visible", timeout=15000)
    page.wait_for_function(
        "(value) => document.querySelector('#consoleStatus')?.textContent?.trim() === value",
        arg=expected, timeout=30000
    )
    require(page.locator("#runSummary").inner_text().find(expected) >= 0,
            "Result summary does not match terminal status")


def main() -> int:
    require(os.environ.get("JORNADA_CONSOLE_BROWSER_ISOLATED") == "true",
            "Refusing browser acceptance without isolated CI opt-in")
    require(APP.is_file(), "Missing Release Console binary")
    OUT.mkdir(parents=True, exist_ok=True)
    checked: list[str] = []

    with tempfile.TemporaryDirectory(prefix="jornada-browser-") as folder:
        temp = Path(folder)
        (temp / "home").mkdir()
        base = f"http://127.0.0.1:{available_port()}"
        env = dict(os.environ)
        env.update({
            "ASPNETCORE_URLS": base, "JORNADA_RUNTIME_MODE": "DEV",
            "ASPNETCORE_ENVIRONMENT": "Development",
            "DOTNET_ENVIRONMENT": "Development",
            "XDG_DATA_HOME": str(temp / "xdg"), "HOME": str(temp / "home"),
        })
        logpath = temp / "console.log"
        with logpath.open("wb") as log:
            proc = subprocess.Popen(
                ["dotnet", str(APP)], env=env, cwd=ROOT,
                stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL
            )
            try:
                with sync_playwright() as pw:
                    browser = pw.chromium.launch(headless=True, args=["--no-sandbox"])
                    try:
                        context = browser.new_context(viewport={"width": 1365, "height": 900},
                                                      accept_downloads=True)
                        page = context.new_page()
                        page.set_default_timeout(15000)
                        install_synthetic_catalog(page)
                        # The HTML is the actual production UI; API is real except
                        # the two catalog/example GETs which would access Gold/SQL.
                        page.goto(base, wait_until="domcontentloaded", timeout=30000)
                        page.locator("#commands .stage").first.wait_for()
                        require("Console DEV" in page.title(), "Wrong Console HTML title")
                        require(page.locator("#commands .exec-badge").filter(
                            has_text="RunOnce").count() > 0,
                            "Browser did not render RunOnce badge")
                        require(page.get_by_role("button", name="Executar one shot").is_disabled(),
                                "Linkage browser button enabled before ingest/Silver")
                        checked.append("desktop navigation and precondition rendering")

                        start_zip_dialog(page)
                        require(page.locator("#zipContract").input_value() == CONTRACT_KEY,
                                "Contract selector lost the provided synthetic catalog")
                        page.locator("#zipPessoaId").fill("UI-SYNTH-PERSON-ONLY")
                        page.locator('#zipDialog button[onclick="setZipMode(\'json\')"]').click()
                        require(json.loads(page.locator("#zipPessoas").input_value())[
                            "idPessoaEntrega"] == "UI-SYNTH-PERSON-ONLY",
                            "HTML form did not serialize to JSONL")
                        page.locator("#zipRegistros").fill("")
                        page.locator('#zipDialog button[onclick="startZip()"]').click()
                        wait_for_result(page, "SUCESSO")
                        require(page.locator("#resultActions button").filter(
                            has_text="Abrir resultado").count() == 1,
                            "Completed ZIP is missing the result download button")
                        require("ZIP gerado" in page.locator("#runSummary").inner_text(),
                                "Completed ZIP summary missing")
                        page.screenshot(path=str(OUT / "desktop-zip-person-only.png"),
                                        full_page=True)
                        checked.append("clicked HTML form → JSONL without facts → real ZIP + SSE")

                        page.locator('header button[onclick="showHistory()"]').click()
                        page.locator("#history .history-item").first.wait_for()
                        require("SUCESSO" in page.locator("#history").inner_text(),
                                "Completed ZIP absent in browser history")
                        page.locator("#history .history-item a").first.click()
                        wait_for_result(page, "SUCESSO")
                        require(page.locator("#resultActions button").filter(
                            has_text="Abrir resultado").count() == 1,
                            "Reopened history lost ZIP download")
                        checked.append("browser history → reopen successful ZIP")

                        page.locator('header button[onclick="openActivityLog()"]').click()
                        page.locator("#activityDialog").wait_for(state="visible")
                        require("Gerar ZIP de ingestão" in page.locator("#activityRuns").inner_text(),
                                "Activity log omitted the completed ZIP")
                        page.locator('#activityDialog button[onclick="activityDialog.close()"]').first.click()
                        checked.append("browser activity modal exposes persisted execution")

                        page.locator('header button[onclick="showHome()"]').click()
                        start_zip_dialog(page)
                        page.locator('#zipDialog button[onclick="setZipMode(\'json\')"]').click()
                        page.locator("#zipPessoas").fill('{"idPessoaEntrega":')
                        page.locator('#zipDialog button[onclick="startZip()"]').click()
                        wait_for_result(page, "FALHA")
                        require(page.locator("#resultActions button").filter(
                            has_text="Abrir resultado").count() == 0,
                            "Invalid ZIP has a browser download button")
                        page.screenshot(path=str(OUT / "desktop-invalid-jsonl.png"),
                                        full_page=True)
                        page.locator('header button[onclick="showHistory()"]').click()
                        page.locator("#history .history-item").first.wait_for()
                        require("FALHA" in page.locator("#history").inner_text(),
                                "Failed ZIP absent from browser history")
                        checked.append("invalid JSONL → visible failure, no download, history")

                        mobile = browser.new_context(viewport={"width": 390, "height": 844},
                                                     device_scale_factor=1)
                        phone = mobile.new_page()
                        install_synthetic_catalog(phone)
                        phone.goto(base, wait_until="domcontentloaded", timeout=30000)
                        phone.locator("#commands .stage").first.wait_for()
                        phone.locator('header button[onclick="showTools()"]').click()
                        phone.locator("#toolsView").wait_for(state="visible")
                        phone.locator('header button[onclick="showHistory()"]').click()
                        phone.locator("#historyView").wait_for(state="visible")
                        phone.locator('header button[onclick="showHome()"]').click()
                        phone.locator("#homeView").wait_for(state="visible")
                        phone.screenshot(path=str(OUT / "mobile-flow.png"),
                                         full_page=True)
                        checked.append("mobile 390px viewport: tools/history/home navigation")
                        mobile.close()
                        context.close()
                    finally:
                        browser.close()
                report = {
                    "status": "PASS", "testData": "SYNTHETIC",
                    "scope": "real Chromium UI+SSE+ZIP, mocked catalog/Gold template reads",
                    "checks": checked, "screenshots": [
                        "desktop-zip-person-only.png", "desktop-invalid-jsonl.png",
                        "mobile-flow.png"
                    ],
                    "notCovered": [
                        "Real SQL-backed contract/Gold catalog lookup",
                        "Browser button Enviar arquivo / API+Silver (covered separately by SQL E2E via HTTP)",
                        "Probabilistic linkage without CPF"
                    ],
                }
                (OUT / "summary.json").write_text(
                    json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
                )
                print(f"CONSOLE CHROMIUM BROWSER: PASS ({len(checked)} scenarios)")
            except BaseException as exc:
                log.flush()
                tail = logpath.read_text(encoding="utf-8", errors="replace")[-2500:]
                raise RuntimeError(f"Console Chromium acceptance failed: {exc}\n{tail}") from exc
            finally:
                if proc.poll() is None:
                    proc.terminate()
                    try:
                        proc.wait(timeout=6)
                    except subprocess.TimeoutExpired:
                        proc.kill()
                        proc.wait(timeout=6)
    return 0


if __name__ == "__main__":
    sys.exit(main())
