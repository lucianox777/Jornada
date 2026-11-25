#!/usr/bin/env python3
"""Verify Resultado.Api, monitor and disabled semiblind boundary in the same DEV run."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

SPEC = importlib.util.spec_from_file_location("http_driver", Path(__file__).with_name("v9-e2e-1k-http.py"))
HTTP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HTTP)
require = HTTP.require


def query(base, route, headers, body=None):
    require(base in (HTTP.API, "http://127.0.0.1:5089"), "SYSTEM_API_LOOPBACK_REQUIRED")
    for _ in range(30):
        req = urllib.request.Request(base + route, data=body, headers=headers)
        try:
            with HTTP.OPENER.open(req, timeout=60) as response:
                raw = response.read(4*1024*1024+1)
                require(len(raw) <= 4*1024*1024, "SYSTEM_API_RESPONSE_BOUND")
                return response.status, raw
        except urllib.error.HTTPError as exc:
            if exc.code != 429:
                raw = exc.read(1024*1024+1)
                require(len(raw) <= 1024*1024, "SYSTEM_API_NEGATIVE_BOUND")
                return exc.code, raw
            time.sleep(2)
    raise ValueError("SYSTEM_API_RATE_LIMIT")


def headers(manager, keys):
    return {"X-Jornada-Gestor": manager, "X-Jornada-Access-Key": keys[manager]}


def check_detail(detail, delivery):
    def count(x):
        return type(x) is int and x >= 0
    require(detail["nomeArquivoConsultado"] == delivery["fileName"]
            and detail["entregasEncontradasParaOMesmoNome"] == 1
            and detail["finalizado"] is True, "RESULTADO_DELIVERY_CENSUS")
    e = detail["entrega"]
    require(e["entregaId"] == delivery["deliveryId"] and e["gestor"] == delivery["manager"]
            and e["nomeArquivo"] == delivery["fileName"] and e["status"] == "PROCESSADA"
            and e["payloadSha256"].upper() == delivery["zipSha256"]
            and type(e["bytesRecebidos"]) is int and e["bytesRecebidos"] == delivery["zipBytes"],
            "RESULTADO_PAYLOAD_DIVERGENCE")
    p = detail["processamento"]
    lots = p["lotes"]
    require(isinstance(lots, list) and len(lots) > 0, "RESULTADO_LOTS_MISSING")
    ids, seqs = set(), set()
    people = records = 0
    for lot in lots:
        lid = str(uuid.UUID(lot["loteId"]))
        seq = lot["loteSeq"]
        require(lid not in ids and type(seq) is int and 1 <= seq <= len(lots)
                and seq not in seqs and lot["loteTotal"] == len(lots)
                and lot["status"] == "PROCESSADO"
                and count(lot["qtdPessoas"]) and count(lot["qtdRegistros"]),
                "RESULTADO_LOT_CENSUS")
        ids.add(lid)
        seqs.add(seq)
        people += lot["qtdPessoas"]
        records += lot["qtdRegistros"]
    require((people, records) == (delivery["versions"], delivery["facts"]), "RESULTADO_LOT_TOTAL")
    by_class = {"PESSOA": 0, "REGISTRO": 0}
    seen = set()
    detailed = consolidated = 0
    for row in p["resumoItens"]:
        key = row["classeItem"], row["resultado"]
        a, b, total = row["quantidadeDetalhada"], row["quantidadeConsolidada"], row["totalObservado"]
        require(key not in seen and key[0] in by_class and all(count(v) for v in (a,b,total))
                and a+b == total, "RESULTADO_ITEM_CENSUS")
        seen.add(key)
        detailed += a
        consolidated += b
        by_class[key[0]] += total
    require(by_class == {"PESSOA": people, "REGISTRO": records}
            and p["itensDetalhadosDisponiveis"] == detailed
            and p["itensConsolidados"] == consolidated
            and p["detalheItemAItemIntegral"] is (consolidated == 0),
            "RESULTADO_ITEM_TOTAL")
    return len(lots)


def verify(root):
    deliveries = HTTP.get_json(root / "deliveries-private.json")
    require(len(deliveries) == 8 and len({x["deliveryId"] for x in deliveries}) == 8,
            "SYSTEM_DELIVERY_CENSUS")
    keys = HTTP.credentials()
    endpoint = "/api/v1/ingestao/resultados/"
    base = "http://127.0.0.1:5089"
    count_lots = 0
    for delivery in deliveries:
        route = endpoint + urllib.parse.quote(delivery["fileName"], safe="")
        status, raw = query(base, route, headers(delivery["manager"], keys))
        require(status == 200, "RESULTADO_HTTP_FAILED")
        detail = json.loads(raw)
        count_lots += check_detail(detail, delivery)
        status, again = query(base, route, headers(delivery["manager"], keys))
        require(status == 200 and json.loads(again) == detail, "RESULTADO_RELOAD_DIVERGENCE")
    first = deliveries[0]
    route = endpoint + urllib.parse.quote(first["fileName"], safe="")
    manager = first["manager"]
    other = next(m for m in sorted(keys) if m != manager)
    negative = [(route, {}, 401), (route, headers(other, keys), 404),
                (endpoint + "invalido.txt", headers(manager, keys), 400)]
    credentials = HTTP.get_json(Path("config/security/test-access-keys.json"))["credentials"]
    typed = next(x for x in credentials if x["type"] == "BENEFICIO" and x["publicCode"] == "AA01")
    typed_headers = {"X-Jornada-Beneficio": "AA01", "X-Jornada-Access-Key": typed["accessKey"]}
    negative.extend([(route, typed_headers, 403),
                     (route, {**typed_headers, "X-Jornada-Gestor": "SEHAB"}, 400)])
    for path, auth, expected in negative:
        status, raw = query(base, path, auth)
        require(status == expected and first["fileName"].encode() not in raw
                and first["deliveryId"].encode() not in raw, "RESULTADO_NEGATIVE_DIVERGENCE")
    monitor_path = "/api/v1/monitor/status"
    status, raw = query(HTTP.API, monitor_path, {})
    require(status == 401, "MONITOR_AUTH_NEGATIVE")
    status, raw = query(HTTP.API, monitor_path, headers("SEHAB", keys))
    require(status == 200, "MONITOR_HTTP_FAILED")
    result = json.loads(raw)
    monitor = result["monitor"]
    runner = HTTP.get_json(root / "runner-receipt.json")
    require(result["readiness"]["ready"] is True
            and monitor["linkageModelGovernance"]["activeModelCount"] == 0
            and len(monitor["linkageRuns"]) == 1, "MONITOR_RUN_CENSUS")
    run = monitor["linkageRuns"][0]
    require(run["runId"] == runner["runId"] and run["runType"] == "FULL"
            and run["status"] == "PUBLICADO" and run["modelVersion"] == 9
            and run["eligible"] == runner["eligible"] and run["evaluated"] == runner["evaluated"],
            "MONITOR_RUN_DIVERGENCE")
    recent = {(x["deliveryId"], x["fileName"], x["manager"], x["status"]) for x in monitor["recentDeliveries"]}
    require(recent == {(x["deliveryId"], x["fileName"], x["manager"], "PROCESSADA") for x in deliveries}
            and not monitor["processing"], "MONITOR_DELIVERY_DIVERGENCE")
    online = {x["component"] for x in monitor["components"] if x["online"] is True}
    require({"Api", "Processor", "ResultadoApi"} <= online, "MONITOR_HEARTBEAT_MISSING")
    # Disabled search is a real negative, not evidence of enabled semiblind
    # visibility, positive Top5 or authorization for new institutional policy.
    semiblind = "/api/v1/identidade/candidatos"
    body = b'{"nomeCompleto":"Pessoa sintetica"}'
    for auth, expected in (({},401), (typed_headers,403), (headers("SEHAB",keys),503)):
        status, raw = query(HTTP.API, semiblind, {**auth, "Content-Type":"application/json"}, body)
        require(status == expected and not raw, "SEMIBLIND_NEGATIVE_DIVERGENCE")
    # Existing bidirectional contract gates; no edits to either OpenAPI.
    for gate in ("openapi-contract-gate.py", "resultado-openapi-contract-gate.py"):
        run = subprocess.run(["python3", str(Path(__file__).with_name(gate))],
                             capture_output=True, timeout=60)
        require(run.returncode == 0, "SYSTEM_OPENAPI_CONTRACT_FAILED")
    with (root / "system-api-receipt.json").open("x", encoding="utf-8") as out:
        json.dump({"schema":"V9_E2E_SYSTEM_API_V1", "status":"PASS",
                   "resultadoDeliveries":len(deliveries), "resultadoLots":count_lots,
                   "resultadoNegativeCases":5, "monitorDeliveries":len(recent),
                   "monitorFullVerified":True, "monitorResidentComponents":3,
                   "semiblindDisabledNegatives":[401,403,503], "openapiContractGates":2,
                   "semiblindEnabledVerified":False, "e2eSystemCertified":False}, out)
    print("V9_E2E_RESULTADO_MONITOR_CONTRACT_PASS_NOT_INTEGRAL")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True, type=Path)
    a = p.parse_args()
    try:
        require(os.environ.get("GITHUB_ACTIONS") == "true"
                and os.environ.get("JORNADA_E2E_V9_DEV_FULL") == "true"
                and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
                and a.root.is_dir() and not a.root.is_symlink(), "SYSTEM_API_DISPOSABLE_REQUIRED")
        verify(a.root)
    except (OSError, ValueError, KeyError, TypeError, AttributeError, StopIteration, subprocess.SubprocessError):
        raise SystemExit("V9_E2E_SYSTEM_API_FAILED") from None


if __name__ == "__main__":
    main()
