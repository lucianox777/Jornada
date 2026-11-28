#!/usr/bin/env python3
"""Real loopback HTTP delivery and private SQL lineage, then API observations.
No identity-level material or credentials are printed or uploaded.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.request
import uuid
import zipfile

API = "http://127.0.0.1:5088"
MANAGERS = {"SEHAB", "SMADS", "SMDET", "SMS"}
DIGEST = re.compile(r"[0-9A-F]{64}")
ROUTES = "/api/v1"


def require(condition, label):
    if not condition:
        raise ValueError(label)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())


def get_json(path):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 32 * 1024 * 1024,
            "PRIVATE_JSON_INVALID")
    return json.loads(path.read_bytes())


def credentials():
    entries = get_json(Path("config/security/test-access-keys.json"))["credentials"]
    result = {}
    for manager in MANAGERS:
        matches = [x for x in entries if x["type"] == "GESTOR" and x.get("gestorCodigo") == manager]
        require(len(matches) == 1, "MANAGER_CREDENTIAL_REQUIRED")
        result[manager] = matches[0]["accessKey"]
    return result


def http(route, manager=None, key=None, data=None, extra=None):
    headers = dict(extra or {})
    if manager is not None:
        require(manager in MANAGERS and bool(key), "MANAGER_INVALID")
        headers.update({"X-Jornada-Gestor": manager, "X-Jornada-Access-Key": key})
    try:
        request = urllib.request.Request(API + route, data=data, headers=headers)
        with OPENER.open(request, timeout=60) as response:
            raw = response.read(4 * 1024 * 1024 + 1)
            require(len(raw) <= 4 * 1024 * 1024, "HTTP_RESPONSE_BOUND")
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as exc:
        return exc.code, None


def sql_scalar(query, attempt):
    db = "JornadaV9Dev_" + attempt.hex
    container = "v9-probe-sql-" + os.environ["GITHUB_RUN_ID"] + "-" + os.environ["GITHUB_RUN_ATTEMPT"]
    require(re.fullmatch(r"v9-probe-sql-[0-9]+-[0-9]+", container), "CI_CONTAINER_INVALID")
    # The password stays in the container's environment, not command arguments.
    completed = subprocess.run(["docker", "exec", container, "sh", "-c",
        'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b -W -h -1 -d "$1" -Q "$2"',
        "sh", db, "SET NOCOUNT ON; " + query], capture_output=True, text=True, timeout=120)
    require(completed.returncode == 0, "SQL_LINEAGE_QUERY_FAILED")
    lines = [x.strip() for x in completed.stdout.splitlines() if x.strip()]
    require(len(lines) == 1, "SQL_LINEAGE_SCALAR_INVALID")
    return lines[0]


def ingest(root, attempt, population=1000, *, after_wave=None, receipt_dir=None):
    scale_profile = os.environ.get('JORNADA_V9_PHASE4_SCALE')
    allowed_scale_profiles = ({f'diagnostic{population//1000}k', f'optimized{population//1000}k'}
                              if population in (2000, 4000) else
                              {'optimized10k'} if population == 10000 else set())
    require(population == 1000 or (population in (2000,4000,10000) and os.environ.get('GITHUB_ACTIONS') == 'true'
            and os.environ.get('GITHUB_REPOSITORY') == 'lucianox777/Jornada'
            and os.environ.get('GITHUB_EVENT_NAME') == 'pull_request'
            and os.environ.get('JORNADA_V9_FULL_DIAGNOSTIC') == 'true'
            and os.environ.get('JORNADA_RUNTIME_MODE') == 'DEV'
            and scale_profile in allowed_scale_profiles
            and os.environ.get('JORNADA_E2E_HTTP_VOLUME_COUNT') == str(population)), 'VOLUME_ADMISSION')
    preflight_path = root / "delivery-preflight-receipt.json"
    pre = get_json(preflight_path)
    require(pre["schema"] == "V9_E2E_1K_WAVE_PREFLIGHT_V1" and pre["status"] == "PASS"
            and pre["baseIdentities"] == population and pre["waveCount"] == 2,
            "WAVE_PREFLIGHT_REQUIRED")
    require(sql_scalar("SELECT COUNT_BIG(*) FROM silver.pessoa_observacao;", attempt) == "0",
            "DIRTY_SILVER_ATTEMPT")
    require(sql_scalar("SELECT COUNT_BIG(*) FROM identidade.linkage_run;", attempt) == "0",
            "DIRTY_RUN_ATTEMPT")
    keys = credentials()
    observed = 0
    deliveries = 0
    delivery_rows = []
    for group in pre["packagesByWave"]:
        wave = group["wave"]
        for item in group["packages"]:
            manager, name = item["manager"], item["fileName"]
            expected = item["versions"]
            sha = item["zipSha256"]
            source_sha = item["sourceSetSha256Utf16le"]
            require(manager in MANAGERS and name == Path(name).name and name.endswith(".zip")
                    and DIGEST.fullmatch(sha) and DIGEST.fullmatch(source_sha)
                    and type(expected) is int and 0 < expected <= population,
                    "PACKAGE_RECEIPT_INVALID")
            path = root / "ingestion" / f"wave-{wave:02}" / "delivery" / name
            require(path.is_file() and not path.is_symlink() and path.stat().st_size == item["zipBytes"],
                    "PACKAGE_SIZE_DRIFT")
            raw = path.read_bytes()
            require(hashlib.sha256(raw).hexdigest().upper() == sha, "PACKAGE_BYTES_DRIFT")
            upload_headers = {"Idempotency-Key": "v9-1k-" + str(wave) + "-" + manager + "-" + sha[:18],
                              "Content-Type": "application/zip",
                              "Content-Disposition": "attachment; filename=" + name}
            status, response = http(ROUTES + "/ingestao/entregas", manager, keys[manager], raw, upload_headers)
            require(status == 202 and response, "HTTP_INGESTION_NOT_ACCEPTED")
            delivery = str(uuid.UUID(response["entregaId"]))
            for _ in range(120 if population==1000 else 1800):
                status, receipt = http(ROUTES + "/ingestao/entregas/" + delivery, manager, keys[manager])
                if status == 429:
                    time.sleep(4)
                    continue
                require(status == 200 and receipt, "HTTP_STATUS_UNAVAILABLE")
                if receipt["status"] == "PROCESSADA":
                    break
                require(receipt["status"] not in ("REJEITADA", "QUARENTENA"), "PROCESSOR_REJECTED")
                time.sleep(4)
            else:
                raise ValueError("PROCESSOR_TIMEOUT")
            sql = f"""SELECT CONCAT(COUNT_BIG(*),'|',UPPER(CONVERT(varchar(64),
                HASHBYTES('SHA2_256',STRING_AGG(CONVERT(nvarchar(max),po.codigo_pessoa_origem+NCHAR(10)),N'')
                WITHIN GROUP (ORDER BY po.codigo_pessoa_origem COLLATE Latin1_General_100_BIN2)),2)))
                FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id
                WHERE l.entrega_id='{delivery}';"""
            require(sql_scalar(sql, attempt) == f"{expected}|{source_sha}", "SILVER_SOURCE_SET_DIVERGENCE")
            sql = f"""SELECT COUNT_BIG(*) FROM ingestao.entrega e
                JOIN ref.gestor g ON g.gestor_id=e.gestor_id
                JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
                WHERE e.entrega_id='{delivery}' AND g.codigo=N'{manager}'
                AND e.status=N'PROCESSADA' AND e.payload_sha256=b.payload_sha256
                AND UPPER(e.payload_sha256)='{sha}' AND e.bytes_recebidos={len(raw)}
                AND b.tamanho_bytes=e.bytes_recebidos
                AND EXISTS(SELECT 1 FROM ingestao.lote l WHERE l.entrega_id=e.entrega_id
                GROUP BY l.entrega_id HAVING COUNT_BIG(*)=MAX(l.lote_total)
                AND MIN(l.lote_seq)=1 AND MAX(l.lote_seq)=MAX(l.lote_total)
                AND SUM(CASE WHEN l.status=N'PROCESSADO' THEN 1 ELSE 0 END)=COUNT_BIG(*));"""
            require(sql_scalar(sql, attempt) == "1", "BRONZE_LINEAGE_DIVERGENCE")
            # Same key and exact bytes must not create a second delivery or version.
            for _ in range(30):
                status, replay = http(ROUTES + "/ingestao/entregas", manager, keys[manager], raw, upload_headers)
                if status != 429:
                    break
                time.sleep(2)
            require(status == 202 and replay and str(uuid.UUID(replay["entregaId"])) == delivery,
                    "HTTP_IDEMPOTENCY_DIVERGENCE")
            with zipfile.ZipFile(path) as archive:
                facts = len(archive.read("registros.jsonl").splitlines())
            delivery_rows.append({**item, "deliveryId": delivery, "facts": facts})
            observed += expected
            deliveries += 1
            # Rate-limit bucket is scoped per credential. No raw response is logged.
        if after_wave is not None:
            after_wave(wave, observed)
    require(observed == pre["sourceVersions"]
            and int(sql_scalar("SELECT COUNT_BIG(*) FROM silver.pessoa_observacao;", attempt)) == observed,
            "SILVER_COMPLETE_CENSUS_DIVERGENCE")
    require(len({x["deliveryId"] for x in delivery_rows}) == deliveries
            and int(sql_scalar("SELECT COUNT_BIG(*) FROM ingestao.entrega;", attempt)) == deliveries,
            "HTTP_DELIVERY_IDEMPOTENCY_CENSUS")
    destination = root if receipt_dir is None else receipt_dir
    with (destination / "deliveries-private.json").open("x", encoding="utf-8") as output:
        json.dump(delivery_rows, output)
    with (destination / "http-lineage.json").open("x", encoding="utf-8") as output:
        json.dump({"schema": "V9_E2E_1K_HTTP_LINEAGE_V1", "status": "PASS",
                   "attempt": str(attempt), "sourceVersions": observed, "deliveries": deliveries,
                   "sameKeyReplayDeliveries": deliveries,
                   "preflightSha256": hashlib.sha256(preflight_path.read_bytes()).hexdigest()}, output)
    print(f"V9_E2E_HTTP_PROCESSOR_LINEAGE_PASS deliveries={deliveries} versions={observed}")


def api(root):
    rows = get_json(root / "sql-census.json")
    oracle = get_json(root / "identity-receipt.json")
    require(oracle["status"] == "PASS", "IDENTITY_ORACLE_REQUIRED")
    keys = credentials()
    # At least one actually published association without CPF and the
    # matching deterministic CPF reference. Use only SQL-census output.
    cpf_by_uuid = {}
    for row in rows:
        if row["hasCpf"] and row["currentStatus"] == "RESOLVIDO":
            cpf_by_uuid.setdefault(row["currentUuid"], row)
    matched = next((row for row in rows if not row["hasCpf"]
                    and row["runResult"] == "ASSOCIACAO_EXISTENTE"
                    and row["currentUuid"] in cpf_by_uuid), None)
    require(matched is not None, "API_ASSOCIATED_PERSON_MISSING")
    reference = cpf_by_uuid[matched["currentUuid"]]
    cpf = None
    for package in root.glob("ingestion/wave-*/delivery/" + reference["package"]):
        with zipfile.ZipFile(package) as archive:
            for line in archive.read("pessoas.jsonl").splitlines():
                row = json.loads(line)
                if row["codigoPessoaOrigem"] == reference["opaque"]:
                    cpf = row["cpf"]
                    break
    require(isinstance(cpf, str) and len(cpf) == 11, "API_SYNTHETIC_CPF_MISSING")
    manager = reference["manager"]
    status, resolution = http(ROUTES + "/identidade/resolver", manager, keys[manager],
        json.dumps({"cpf": cpf}).encode(), {"Content-Type": "application/json"})
    require(status == 200 and resolution and resolution.get("pessoaUuid") == matched["currentUuid"],
            "API_CPF_RESOLUTION_DIVERGENCE")
    manager = matched["manager"]
    destination = str(uuid.UUID(matched["currentUuid"]))
    for suffix in ("", "/registros", "/beneficios-concedidos"):
        status, result = http(ROUTES + "/pessoas/" + destination + suffix, manager, keys[manager])
        require(status == 200 and result is not None, "API_PERSON_QUERY_FAILED")
    with (root / "api-receipt.json").open("x", encoding="utf-8") as output:
        json.dump({"schema": "V9_E2E_1K_API_SAMPLE_V1", "status": "PASS",
                   "cpfResolveMatchesSql": True, "associatedPersonRoutes": 3,
                   "scope": "IDENTITY_AND_PERSON_API_SAMPLE_NO_FACTUAL_PAYLOAD_CERTIFICATION"}, output)
    print("V9_E2E_API_SAMPLE_PASS_NOT_INTEGRAL")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("mode", choices=("ingest", "api"))
    p.add_argument("--root", type=Path, required=True)
    a = p.parse_args()
    try:
        require(os.environ.get("GITHUB_ACTIONS") == "true"
                and os.environ.get("JORNADA_E2E_V9_DEV_FULL") == "true"
                and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
                and a.root.is_dir() and not a.root.is_symlink(), "DISPOSABLE_CI_REQUIRED")
        attempt = uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"])
        if a.mode == "ingest":
            ingest(a.root, attempt)
        else:
            api(a.root)
    except (OSError, ValueError, KeyError, TypeError, StopIteration, subprocess.SubprocessError):
        raise SystemExit("V9_E2E_HTTP_OR_API_FAILED") from None


if __name__ == "__main__":
    main()
