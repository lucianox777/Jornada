#!/usr/bin/env python3
"""C3.2f2: fail-closed evaluation of SQL recovery evidence (read-only JSON).

This validates a proof bundle's internal coherence; it DOES NOT create data,
inject failures, open SQL connections or independently attest runtime origin.
The future E2E producer MUST read these values from disposable JornadaE2E.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

UUID = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
SHA256 = re.compile(r"^[0-9a-fA-F]{64}$")
PROJECT = re.compile(r"^jornada-workers-e2e-ci[0-9]{7,19}$")


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def obj(value: object, name: str) -> dict:
    require(type(value) is dict, f"{name} must be an object")
    return value


def field(value: dict, key: str) -> object:
    require(key in value, f"missing {key}")
    return value[key]


def uuid(value: object, name: str) -> str:
    require(type(value) is str and UUID.fullmatch(value) is not None, f"invalid {name}")
    return value.lower()


def true(value: object, name: str) -> None:
    require(value is True, f"{name} must be proven true")


def number(value: object, name: str, at_least: int = 0) -> int:
    require(type(value) is int and value >= at_least, f"invalid {name}")
    return value


def validate(data: dict) -> None:
    env = obj(field(data, "environment"), "environment")
    require(field(env, "database") == "JornadaE2E", "database must be private")
    require(field(env, "origin") == "live_sql_in_ephemeral_ci", "only runtime SQL evidence is eligible")
    project = field(env, "compose_project")
    require(type(project) is str and PROJECT.fullmatch(project) is not None,
            "invalid ephemeral Compose project")
    require(field(data, "scenario") == "C3.2f2_real_worker_lot_recovery",
            "wrong scenario")

    batch = obj(field(data, "batch"), "batch")
    entrega = uuid(field(batch, "entrega_id"), "entrega_id")
    lote = uuid(field(batch, "lote_id"), "lote_id")
    require(entrega != lote, "delivery and batch IDs must differ")
    key = field(batch, "idempotency_key")
    require(type(key) is str and key.startswith("ci-e2e-") and 8 <= len(key) <= 120,
            "idempotency key is not synthetic and scoped")
    payload = field(batch, "payload_sha256")
    require(type(payload) is str and SHA256.fullmatch(payload) is not None,
            "ZIP checksum absent")
    number(field(batch, "expected_pessoas"), "expected_pessoas", 1)
    number(field(batch, "expected_registros"), "expected_registros", 1)

    before = obj(field(data, "before"), "before")
    require(field(before, "lote_status") == "PROCESSANDO",
            "fault must target real PROCESSANDO batch")
    old_lease = uuid(field(before, "lease_id"), "old lease")
    require(uuid(field(before, "heartbeat_lease_id"), "old heartbeat token") == old_lease,
            "heartbeat must match reserved token")
    require(type(field(before, "lease_owner")) is str and field(before, "lease_owner"),
            "old lease owner absent")
    attempts_before = number(field(before, "tentativa_count"), "attempts before", 1)
    recovered_before = number(field(before, "recuperacao_count"), "recoveries before")

    fault = obj(field(data, "fault"), "fault")
    for name in ("transaction_open_after_first_write", "not_committed_before_crash",
                 "old_worker_unexpected_exit", "worker_pid_changed",
                 "sql_transaction_rollback_observed"):
        true(field(fault, name), name)
    before_pid = number(field(fault, "before_host_pid"), "before PID", 2)
    after_pid = number(field(fault, "after_host_pid"), "after PID", 2)
    require(before_pid != after_pid, "no worker PID change")
    require(uuid(field(fault, "before_instance_id"), "before worker instance") !=
            uuid(field(fault, "after_instance_id"), "after worker instance"),
            "SQL heartbeat worker instance unchanged")
    require(number(field(fault, "after_restart_count"), "restarts after", 1) >
            number(field(fault, "before_restart_count"), "restarts before"),
            "Docker restart count unchanged")

    recovery = obj(field(data, "recovery"), "recovery")
    for name in ("previous_token_expired", "old_lease_heartbeat_deleted",
                 "old_lease_fenced", "requeued_by_worker"):
        true(field(recovery, name), name)
    new_lease = uuid(field(recovery, "new_lease_id"), "new lease")
    require(new_lease != old_lease, "new lease reused old token")
    require(number(field(recovery, "recuperacao_count"), "recovery count", recovered_before+1)
            > recovered_before, "recovery counter not incremented")
    require(number(field(recovery, "tentativa_count"), "reprocessing attempts", attempts_before+1)
            > attempts_before, "no second processing attempt")

    terminal = obj(field(data, "terminal"), "terminal")
    require(field(terminal, "lote_status") == "PROCESSADO" and
            field(terminal, "entrega_status") == "PROCESSADA", "batch not committed")
    require(field(terminal, "lease_id") is None and
            field(terminal, "lease_owner") is None, "terminal lease must be NULL")
    require(number(field(terminal, "lote_heartbeat_rows"), "heartbeat rows") == 0,
            "terminal heartbeat must be cleared")
    require(number(field(terminal, "silver_people"), "Silver people") ==
            batch["expected_pessoas"], "Silver people cardinality differs")
    require(number(field(terminal, "silver_records"), "Silver records") ==
            batch["expected_registros"], "Silver fact cardinality differs")
    require(number(field(terminal, "processed_items"), "processed items") ==
            batch["expected_pessoas"] + batch["expected_registros"],
            "item_processado cardinality differs")
    true(field(terminal, "gold_and_serving_consistent"), "Gold/Serving consistency")
    require(number(field(terminal, "duplicate_business_keys"), "duplicate keys") == 0,
            "duplicate business keys detected")

    replay = obj(field(data, "idempotency_replay"), "idempotency_replay")
    true(field(replay, "same_zip_and_idempotency_key"), "replay payload")
    for name in ("additional_entregas", "additional_lotes",
                 "additional_silver_rows", "additional_gold_rows",
                 "additional_serving_rows"):
        require(number(field(replay, name), name) == 0, f"idempotency replay created {name}")

    queries = obj(field(data, "sql_provenance"), "sql_provenance")
    for stage in ("before", "during_open_transaction", "after_crash",
                  "after_lease_recovery", "terminal", "replay"):
        true(field(queries, stage), f"runtime SQL query for {stage}")


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: e2e-lot-recovery-evidence-gate.py <evidence.json>", file=sys.stderr)
        return 2
    try:
        data = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
        validate(obj(data, "evidence"))
    except (ValueError, OSError, UnicodeError, json.JSONDecodeError) as exc:
        print(f"C3.2f2 evidence REJECTED: {exc}", file=sys.stderr)
        return 1
    print("C3.2f2 evidence STRUCTURE PASS — verify actual SQL runtime/job separately")
    return 0


if __name__ == "__main__":
    sys.exit(main())
