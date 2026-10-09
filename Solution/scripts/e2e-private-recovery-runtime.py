#!/usr/bin/env python3
"""C3.2f2c real crash/recovery acceptance in a single disposable GitHub E2E project.

Never targets an ordinary SQL/Compose deployment; never starts a second build.
This is a private operational gate, not a reusable cluster maintenance script.
"""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".local/e2e/c3-2f2c-recovery"
COMPOSE_FILE = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"
SQLCMD = "/opt/mssql-tools18/bin/sqlcmd"
SERVICES = ("sqlserver", "api", "resultado-api", "processor",
            "operations-maintenance", "bronze-maintenance")


def require(ok: bool, message: str) -> None:
    if not ok:
        raise RuntimeError(message)


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")


def command(args: list[str], data: bytes | None = None, timeout: int = 25) -> str:
    try:
        result = subprocess.run(args, input=data, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, cwd=ROOT, timeout=timeout,
                                check=False)
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError("private E2E command timed out (no external cleanup)") from exc
    require(result.returncode == 0, "private E2E command failed: " + args[0] +
            " (exit=" + str(result.returncode) + "; inspect CI evidence)")
    return result.stdout.decode("utf-8").strip()


def save(name: str, value: object) -> None:
    p = OUT / name
    require(p.parent == OUT and not p.is_symlink(), "invalid evidence destination")
    p.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sql(cid: str, query: str, name: str | None = None) -> str:
    output = command(["docker", "exec", "-e", "SQLCMDPASSWORD", cid, SQLCMD,
                      "-S", "localhost", "-U", "sa", "-C", "-b", "-I",
                      "-l", "8", "-d", "JornadaE2E", "-W", "-h", "-1",
                      "-Q", "SET NOCOUNT ON; SET LOCK_TIMEOUT 1500; " + query],
                     timeout=14)
    output = "".join(output.split())
    if name:
        save(name, {"observed_at_utc": now(), "sql_query": query, "sql_result": output})
    return output


def count(cid: str, query: str, name: str | None = None) -> int:
    value = sql(cid, query, name)
    require(re.fullmatch(r"[0-9]+", value) is not None, "SQL count is not scalar integer")
    return int(value)


def inspect(cid: str) -> dict:
    return json.loads(command(["docker", "inspect", cid, "--format", "{{json .}}"]))


def attest(cid: str, service: str, project: str) -> dict:
    state = inspect(cid)
    labels = state["Config"]["Labels"]
    require(labels.get("com.docker.compose.project") == project
            and labels.get("com.docker.compose.service") == service,
            "private container labels do not match exclusive project/service: " + service)
    require(state["State"]["Running"] and int(state["State"]["Pid"]) > 1,
            "service stopped: " + service)
    return state


def worker_instance(sql_cid: str) -> str:
    text = sql(sql_cid, """
        SELECT TOP(1) CONVERT(VARCHAR(36),instance_id)
        FROM controle.runtime_componente
        WHERE node_id=N'NODE2' AND componente=N'Processor'
          AND status=N'RUNNING'
          AND heartbeat_em >= DATEADD(SECOND,-45,SYSUTCDATETIME())
        ORDER BY heartbeat_em DESC;""")
    return str(uuid.UUID(text))


def post(api_cid: str, access_key: str, key: str, filename: str, payload: bytes) -> str:
    # No host ports. File bytes cross only STDIN -> verified private API.
    response = command([
        "docker", "exec", "-i", api_cid, "curl", "--max-time", "35",
        "-sS", "--fail-with-body", "-X", "POST",
        "http://127.0.0.1:5080/api/v1/ingestao/entregas",
        "-H", "X-Jornada-Gestor: SEHAB",
        "-H", "X-Jornada-Access-Key: " + access_key,
        "-H", "Idempotency-Key: " + key,
        "-H", "Content-Type: application/zip",
        "-H", "Content-Disposition: attachment; filename=" + filename,
        "--data-binary", "@-",
    ], data=payload, timeout=45)
    return str(uuid.UUID(json.loads(response)["entregaId"]))


def lote_state(sql_cid: str, lote: str) -> dict:
    # READ UNCOMMITTED is used only to observe the target uncommitted Silver
    # write. The committed lease transition is separately evidenced by audit.
    raw = sql(sql_cid, f"""
      SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
      SELECT CONCAT(l.status,'|',
        COALESCE(CONVERT(VARCHAR(36),l.lease_id),'-'),'|',
        COALESCE(CONVERT(VARCHAR(36),h.lease_id),'-'),'|',
        COALESCE(l.lease_owner,'-'),'|',
        l.tentativa_count,'|',l.recuperacao_count,'|',
        COALESCE(DATEDIFF_BIG(SECOND,
          CONVERT(DATETIMEOFFSET,'1970-01-01T00:00:00+00:00'),
          h.lease_expira_em),0),'|',
        (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao p WHERE p.lote_id=l.lote_id))
      FROM ingestao.lote l
      LEFT JOIN ingestao.lote_heartbeat h ON h.lote_id=l.lote_id
      WHERE l.lote_id='{lote}';""")
    parts = raw.split("|")
    require(len(parts) == 8, "private SQL lot snapshot shape mismatch")
    return dict(zip(("status", "lease", "heartbeat_lease", "owner",
                     "attempts", "recoveries", "lease_expiration_epoch",
                     "dirty_silver_people"),
                    (parts[0], parts[1], parts[2], parts[3],
                     int(parts[4]), int(parts[5]), int(parts[6]),
                     int(parts[7]))))


def wait_for(predicate, seconds: int, reason: str):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        value = predicate()
        if value is not None:
            return value
        time.sleep(0.7)
    raise RuntimeError("private E2E timed out: " + reason)


def totals(sql_cid: str, entrega: str, lote: str, key: str) -> dict:
    # Counts and uniqueness are scoped by actual delivery/lot, NEVER DB-wide.
    queries = {
        "entregas": f"SELECT COUNT_BIG(*) FROM ingestao.entrega WHERE idempotency_key=N'{key}';",
        "lotes": f"SELECT COUNT_BIG(*) FROM ingestao.lote WHERE entrega_id='{entrega}';",
        "people": f"SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE lote_id='{lote}';",
        "records": f"SELECT COUNT_BIG(*) FROM silver.registro_observacao WHERE lote_id='{lote}';",
        "items": f"SELECT COUNT_BIG(*) FROM ingestao.item_processado WHERE lote_id='{lote}';",
        "gold": f"SELECT COUNT_BIG(*) FROM gold.beneficio_concedido WHERE entrega_id='{entrega}';",
        "serving": f"SELECT COUNT_BIG(*) FROM serving.registro_integrado WHERE entrega_id='{entrega}';",
        "gold_serving_same_record": f"""SELECT COUNT_BIG(*)
            FROM gold.beneficio_concedido g
            JOIN serving.registro_integrado s
              ON s.registro_observacao_id=g.registro_observacao_id
            WHERE g.entrega_id='{entrega}' AND s.entrega_id='{entrega}';""",
        "duplicate_keys": f"""SELECT COUNT_BIG(*) FROM (
            SELECT codigo_registro_origem FROM silver.registro_observacao
             WHERE lote_id='{lote}' GROUP BY codigo_registro_origem HAVING COUNT_BIG(*)>1
            UNION ALL
            SELECT codigo_registro_origem FROM gold.beneficio_concedido
             WHERE entrega_id='{entrega}' GROUP BY codigo_registro_origem HAVING COUNT_BIG(*)>1
            UNION ALL
            SELECT codigo_registro_origem FROM serving.registro_integrado
             WHERE entrega_id='{entrega}' GROUP BY codigo_registro_origem HAVING COUNT_BIG(*)>1
          ) duplicates;""",
    }
    return {name: count(sql_cid, q) for name, q in queries.items()}


def main() -> None:
    require(len(sys.argv) == 1, "no command arguments permitted")
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and re.fullmatch(r"[0-9]{6,16}", run) is not None
            and re.fullmatch(r"[0-9]{1,3}", attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == f"ci{run}{attempt}"
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and os.environ.get("JORNADA_WORKERS_E2E_IMAGE_TAG", "test") == "test",
            "C3.2f2c forbidden outside unique disposable GitHub E2E")
    project = f"jornada-workers-e2e-ci{run}{attempt}"
    OUT.mkdir(parents=True, exist_ok=True)
    key = f"ci-e2e-recovery-{run}-{attempt}"
    compose = ["docker", "compose", "--env-file", "/dev/null", "--profile",
               "continuous", "-p", project, "-f", str(COMPOSE_FILE)]
    ids = {name: command(compose + ["ps", "-q", name]) for name in SERVICES}
    states = {name: attest(cid, name, project) for name, cid in ids.items()}
    require(count(ids["sqlserver"], "SELECT IIF(DB_NAME()=N'JornadaE2E' AND "
                  "EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 "
                  "AND name=N'Jornada.EnvironmentProfile' AND "
                  "CONVERT(NVARCHAR(100),value)=N'Development'),1,0);") == 1,
            "SQL E2E private DB/DEV marker mismatch")
    original_instance = worker_instance(ids["sqlserver"])
    require(count(ids["sqlserver"],
                  "SELECT COUNT_BIG(*) FROM sys.triggers WHERE "
                  "name=N'tr_ci_c3_2f2c_hold_first_write';") == 0,
            "refusing existing private SQL recovery trigger")

    # Install the trigger ONLY after validating project labels and DB marker.
    script = (ROOT / "scripts/e2e-private-recovery-transaction-hold.sql").read_bytes()
    command(["docker", "exec", "-i", "-e", "SQLCMDPASSWORD", ids["sqlserver"],
             SQLCMD, "-S", "localhost", "-U", "sa", "-C", "-b", "-I", "-l", "10",
             "-d", "JornadaE2E", "-v", "RecoveryKey=" + key,
             "-i", "/dev/stdin"], data=script, timeout=35)
    require(count(ids["sqlserver"], "SELECT COUNT_BIG(*) FROM sys.triggers WHERE "
                  "name=N'tr_ci_c3_2f2c_hold_first_write' AND is_disabled=0;") == 1,
            "private SQL transaction barrier not installed")

    generated = command(["python3", "scripts/e2e-private-recovery-fixture.py",
                         str(OUT / "fixture")])
    require(generated == str(OUT / "fixture"), "synthetic fixture generator misrouted")
    package = command(["python3", "scripts/build-ingestion-fixture.py",
                       "--fixture", str(OUT / "fixture"), "--gestor", "SEHAB",
                       "--output-dir", str(OUT / "packages")])
    path = Path(package)
    require(path.parent == OUT / "packages" and path.is_file() and not path.is_symlink(),
            "synthetic ZIP path not isolated")
    payload = path.read_bytes()
    sha = hashlib.sha256(payload).hexdigest()
    require(path.name == f"ENTREGA_SEHAB_SEHAB_v2_{sha}.zip",
            "ZIP hash and filename inconsistent")
    creds = json.loads((ROOT / "config/security/test-access-keys.json").read_text())["credentials"]
    candidates = [c["accessKey"] for c in creds if c.get("type") == "GESTOR"
                  and c.get("gestorCodigo") == "SEHAB"
                  and "jornada.ingestao.write" in c.get("scopes", [])]
    require(len(candidates) == 1, "unique synthetic Development access key required")
    entrega = post(ids["api"], candidates[0], key, path.name, payload)
    lote = wait_for(lambda: (lambda x: str(uuid.UUID(x)) if x else None)(
        sql(ids["sqlserver"], f"SELECT TOP(1) CONVERT(VARCHAR(36),lote_id) "
            f"FROM ingestao.lote WHERE entrega_id='{entrega}';")),
        20, "real API-created lote")
    save("api-receipt.json", {"entrega_id": entrega, "lote_id": lote,
                             "zip_sha256": sha, "idempotency_key": key})

    def first_write():
        s = lote_state(ids["sqlserver"], lote)
        if (s["status"] == "PROCESSANDO" and s["attempts"] == 1
                and s["dirty_silver_people"] == 1
                and s["lease"] != "-" and s["lease"] == s["heartbeat_lease"]
                and s["owner"] != "-" and s["lease_expiration_epoch"] > 0):
            return s
        return None

    before = wait_for(first_write, 65, "uncommitted Silver row and live lease")
    require(count(ids["sqlserver"],
                  f"SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WITH "
                  f"(READUNCOMMITTED) WHERE lote_id='{lote}';") == 1,
            "transaction open before SIGKILL not confirmed by independent SQL")
    before_time = now()
    save("before-sigkill-sql.json", {"observed_at_utc": before_time,
                                    "lote_id": lote, **before})
    old_pid = int(states["processor"]["State"]["Pid"])
    old_restart = int(states["processor"]["RestartCount"])
    start_event = int(time.time()) - 2
    # Same allowlisted host-PID fault injection proven in merged C3.2f1.
    current = attest(ids["processor"], "processor", project)
    require(current["Id"] == states["processor"]["Id"]
            and int(current["State"]["Pid"]) == old_pid,
            "worker identity changed before CI fault injection")
    command(["sudo", "kill", "-KILL", "--", str(old_pid)], timeout=10)

    def restarted():
        state = inspect(ids["processor"])
        labels = state["Config"]["Labels"]
        require(labels.get("com.docker.compose.project") == project
                and labels.get("com.docker.compose.service") == "processor",
                "processor container identity changed during restart")
        if (state["State"]["Running"]
                and int(state["State"]["Pid"]) > 1
                and int(state["State"]["Pid"]) != old_pid
                and int(state["RestartCount"]) > old_restart):
            return state
        return None

    after = wait_for(restarted, 45, "real Docker PID1 restart")
    new_pid = int(after["State"]["Pid"])
    new_restart = int(after["RestartCount"])
    events = command(["docker", "events", "--since", str(start_event),
                      "--until", str(int(time.time()) + 1),
                      "--filter", "container=" + ids["processor"],
                      "--filter", "event=die", "--format", "{{json .}}"],
                     timeout=15)
    decoded_events = [json.loads(line) for line in events.splitlines() if line.strip()]
    exit_codes = [int(event.get("Actor", {}).get("Attributes", {}).get("exitCode", -1))
                  for event in decoded_events]
    save("processor-die-events.json", {"events": decoded_events,
                                       "exit_codes": exit_codes})
    require(137 in exit_codes, "Docker did not record SIGKILL exit 137")
    new_instance = wait_for(
        lambda: (lambda value: value if value != original_instance else None)(
            worker_instance(ids["sqlserver"])), 35, "new heartbeat instance after crash")
    for service in SERVICES:
        if service == "processor":
            continue
        state = attest(ids[service], service, project)
        require(int(state["State"]["Pid"]) == int(states[service]["State"]["Pid"])
                and int(state["RestartCount"]) == int(states[service]["RestartCount"]),
                "SQL/API/other worker restarted during processor fault: " + service)

    # A separate committed read MUST find zero Silver rows: the earlier
    # READ UNCOMMITTED observation was part of a transaction rolled back.
    rolled_back = count(ids["sqlserver"],
                        f"SELECT COUNT_BIG(*) FROM silver.pessoa_observacao "
                        f"WHERE lote_id='{lote}';", "after-crash-silver-sql.json")
    require(rolled_back == 0, "Silver write was not rolled back after SIGKILL")
    after_crash_status = lote_state(ids["sqlserver"], lote)
    require(after_crash_status["status"] == "PROCESSANDO"
            and after_crash_status["lease"] == before["lease"],
            "Processor finished/released old lease instead of crashing mid-transaction")
    fault_time = now()
    save("fault-sql-and-docker.json", {"observed_at_utc": fault_time,
                                      "old_host_pid": old_pid, "new_host_pid": new_pid,
                                      "restart_before": old_restart,
                                      "restart_after": new_restart,
                                      "old_instance_id": original_instance,
                                      "new_instance_id": new_instance,
                                      "silver_committed_rows_after_kill": rolled_back,
                                      "old_lease_state": after_crash_status})

    def recovered():
        s = lote_state(ids["sqlserver"], lote)
        if (s["attempts"] >= before["attempts"] + 1
                and s["recoveries"] >= before["recoveries"] + 1
                and s["lease"] not in ("-", before["lease"])
                and s["heartbeat_lease"] == s["lease"]
                and s["status"] in ("VALIDANDO", "PROCESSANDO")):
            return s
        return None

    renewed = wait_for(recovered, 130, "expired lease recovery, fencing and new reservation")
    event_count = count(ids["sqlserver"], f"""SELECT COUNT_BIG(*)
        FROM dbo.ci_c3_2f2c_requeue_events
        WHERE lote_id='{lote}' AND old_lease_id='{before["lease"]}'
          AND old_recuperacao_count={before["recoveries"]}
          AND new_recuperacao_count={renewed["recoveries"]}
          AND new_status=N'PENDENTE';""", "requeue-audit-sql.json")
    require(event_count == 1, "no committed PENDENTE/requeue from real recovery UPDATE")
    old_remaining = count(ids["sqlserver"], f"""SELECT COUNT_BIG(*)
        FROM ingestao.lote_heartbeat
        WHERE lote_id='{lote}' AND lease_id='{before["lease"]}';""",
        "old-heartbeat-removed-sql.json")
    require(old_remaining == 0, "old lease heartbeat not fenced")
    sql_time = count(ids["sqlserver"], """SELECT DATEDIFF_BIG(SECOND,
       CONVERT(DATETIMEOFFSET,'1970-01-01T00:00:00+00:00'),
       SYSUTCDATETIME());""")
    require(sql_time > before["lease_expiration_epoch"],
            "old heartbeat lease did not expire in real SQL time")
    recovered_time = now()
    save("recovery-sql.json", {"observed_at_utc": recovered_time, **renewed,
                              "requeue_events": event_count,
                              "old_heartbeat_rows": old_remaining,
                              "sql_now_epoch": sql_time})

    def terminal():
        s = lote_state(ids["sqlserver"], lote)
        status = sql(ids["sqlserver"],
                     f"SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; "
                     f"SELECT status FROM ingestao.entrega WHERE entrega_id='{entrega}';")
        if s["status"] == "PROCESSADO" and status == "PROCESSADA":
            return s
        return None

    final = wait_for(terminal, 130, "final PROCESSADO/PROCESSADA after requeue")
    require(final["lease"] == "-" and final["heartbeat_lease"] == "-",
            "terminal lote still owns a lease")
    last = totals(ids["sqlserver"], entrega, lote, key)
    require(last == {"entregas": 1, "lotes": 1, "people": 1, "records": 1,
                      "items": 2, "gold": 1, "serving": 1,
                      "gold_serving_same_record": 1, "duplicate_keys": 0},
            "recovery materialization/uniqueness incorrect: " + str(last))
    final_time = now()
    save("terminal-sql.json", {"observed_at_utc": final_time,
                               "lote_state": final, "counts": last})
    same_entrega = post(ids["api"], candidates[0], key, path.name, payload)
    require(same_entrega == entrega, "HTTP replay created second Entrega after recovery")
    replay = totals(ids["sqlserver"], entrega, lote, key)
    require(replay == last, "HTTP replay duplicated Silver/Gold/Serving/processed items")
    replay_time = now()
    save("replay-sql.json", {"observed_at_utc": replay_time,
                             "same_entrega_id": same_entrega == entrega,
                             "counts": replay})
    require(all(earlier < later for earlier, later in zip(
        (before_time, fault_time, recovered_time, final_time),
        (fault_time, recovered_time, final_time, replay_time))),
        "evidence stages not chronological")

    def diff(field: str) -> int:
        return replay[field] - last[field]

    evidence = {
        "scenario": "C3.2f2_real_worker_lot_recovery",
        "environment": {"origin": "live_sql_in_ephemeral_ci",
                        "database": "JornadaE2E", "compose_project": project,
                        "github_run_id": run, "github_run_attempt": attempt},
        "batch": {"entrega_id": entrega, "lote_id": lote,
                  "idempotency_key": key, "payload_sha256": sha,
                  "expected_pessoas": 1, "expected_registros": 1},
        "before": {"observed_at_utc": before_time, "lote_status": before["status"],
                   "lease_id": before["lease"], "heartbeat_lease_id": before["heartbeat_lease"],
                   "lease_owner": before["owner"], "tentativa_count": before["attempts"],
                   "recuperacao_count": before["recoveries"]},
        "fault": {"observed_at_utc": fault_time,
                  "transaction_open_after_first_write": before["dirty_silver_people"] == 1,
                  "not_committed_before_crash": rolled_back == 0,
                  "old_worker_unexpected_exit": 137 in exit_codes,
                  "old_worker_exit_code": 137 if 137 in exit_codes else 0,
                  "worker_pid_changed": old_pid != new_pid,
                  "sql_transaction_rollback_observed": rolled_back == 0,
                  "before_host_pid": old_pid, "after_host_pid": new_pid,
                  "before_restart_count": old_restart, "after_restart_count": new_restart,
                  "before_instance_id": original_instance,
                  "after_instance_id": new_instance},
        "recovery": {"observed_at_utc": recovered_time,
                     "previous_token_expired": sql_time > before["lease_expiration_epoch"],
                     "old_lease_heartbeat_deleted": old_remaining == 0,
                     "old_lease_fenced": renewed["lease"] != before["lease"]
                         and old_remaining == 0,
                     "requeued_by_worker": event_count == 1,
                     "new_lease_id": renewed["lease"],
                     "recuperacao_count": renewed["recoveries"],
                     "tentativa_count": renewed["attempts"]},
        "terminal": {"observed_at_utc": final_time,
                     "lote_status": final["status"], "entrega_status": "PROCESSADA",
                     "lease_id": None, "lease_owner": None, "lote_heartbeat_rows": 0,
                     "silver_people": last["people"], "silver_records": last["records"],
                     "processed_items": last["items"],
                     "gold_and_serving_consistent": last["gold"] == 1
                         and last["serving"] == 1 and last["gold_serving_same_record"] == 1,
                     "duplicate_business_keys": last["duplicate_keys"]},
        "idempotency_replay": {"observed_at_utc": replay_time,
                               "same_zip_and_idempotency_key": same_entrega == entrega,
                               "additional_entregas": diff("entregas"),
                               "additional_lotes": diff("lotes"),
                               "additional_silver_rows": diff("people")+diff("records"),
                               "additional_gold_rows": diff("gold"),
                               "additional_serving_rows": diff("serving")},
        "sql_provenance": {
            "before": (OUT / "before-sigkill-sql.json").is_file(),
            "during_open_transaction": before["dirty_silver_people"] == 1
                and before["status"] == "PROCESSANDO",
            "after_crash": (OUT / "after-crash-silver-sql.json").is_file()
                and rolled_back == 0,
            "after_lease_recovery": (OUT / "requeue-audit-sql.json").is_file()
                and (OUT / "old-heartbeat-removed-sql.json").is_file(),
            "terminal": (OUT / "terminal-sql.json").is_file(),
            "replay": (OUT / "replay-sql.json").is_file(),
        },
    }
    save("real-recovery-evidence.json", evidence)
    command(["python3", "scripts/e2e-lot-recovery-evidence-gate.py",
             str(OUT / "real-recovery-evidence.json")])
    print("C3.2f2c: PASS real private SQL rollback, expired lease, fencing, requeue, "
          "second processing and idempotent ZIP replay")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, AssertionError, ValueError, OSError, KeyError, json.JSONDecodeError) as exc:
        print("C3.2f2c: REJECTED " + str(exc), file=sys.stderr)
        sys.exit(2)
