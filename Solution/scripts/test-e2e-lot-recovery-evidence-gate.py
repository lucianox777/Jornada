#!/usr/bin/env python3
"""No-SQL, no-Docker mutation tests for the C3.2f2 evidence validator."""
from __future__ import annotations

from copy import deepcopy
import json
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
GATE = ROOT / "scripts/e2e-lot-recovery-evidence-gate.py"
OLD = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
NEW = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
ENTREGA = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
LOTE = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
OLD_WORKER = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
NEW_WORKER = "ffffffff-ffff-4fff-8fff-ffffffffffff"


def fixture() -> dict:
    return {
        "scenario": "C3.2f2_real_worker_lot_recovery",
        "environment": {
            "origin": "live_sql_in_ephemeral_ci", "database": "JornadaE2E",
            "compose_project": "jornada-workers-e2e-ci378828444111"
        },
        "batch": {
            "entrega_id": ENTREGA, "lote_id": LOTE,
            "idempotency_key": "ci-e2e-recovery-fixture",
            "payload_sha256": "a" * 64,
            "expected_pessoas": 1, "expected_registros": 1
        },
        "before": {
            "lote_status": "PROCESSANDO",
            "lease_id": OLD, "heartbeat_lease_id": OLD,
            "lease_owner": "synthetic-processor",
            "tentativa_count": 1, "recuperacao_count": 0
        },
        "fault": {
            "transaction_open_after_first_write": True,
            "not_committed_before_crash": True,
            "old_worker_unexpected_exit": True, "worker_pid_changed": True,
            "sql_transaction_rollback_observed": True,
            "before_host_pid": 1002, "after_host_pid": 1019,
            "before_instance_id": OLD_WORKER, "after_instance_id": NEW_WORKER,
            "before_restart_count": 0, "after_restart_count": 1
        },
        "recovery": {
            "previous_token_expired": True,
            "old_lease_heartbeat_deleted": True,
            "old_lease_fenced": True, "requeued_by_worker": True,
            "new_lease_id": NEW, "recuperacao_count": 1, "tentativa_count": 2
        },
        "terminal": {
            "lote_status": "PROCESSADO", "entrega_status": "PROCESSADA",
            "lease_id": None, "lease_owner": None,
            "lote_heartbeat_rows": 0, "silver_people": 1,
            "silver_records": 1, "processed_items": 2,
            "gold_and_serving_consistent": True, "duplicate_business_keys": 0
        },
        "idempotency_replay": {
            "same_zip_and_idempotency_key": True, "additional_entregas": 0,
            "additional_lotes": 0, "additional_silver_rows": 0,
            "additional_gold_rows": 0, "additional_serving_rows": 0
        },
        "sql_provenance": {
            "before": True, "during_open_transaction": True,
            "after_crash": True, "after_lease_recovery": True,
            "terminal": True, "replay": True
        }
    }


def run(evidence: dict, expected: int, reason: str) -> None:
    with TemporaryDirectory(prefix="jornada-evidence-test-") as tmp:
        path = Path(tmp) / "proof.json"
        path.write_text(json.dumps(evidence), encoding="utf-8")
        result = subprocess.run([sys.executable, str(GATE), str(path)],
                                capture_output=True, text=True, timeout=5)
    assert result.returncode == expected, (reason, result.returncode, result.stdout, result.stderr)


def main() -> None:
    run(fixture(), 0, "internally consistent synthetic fixture")
    changes = [
        ("environment", "database", "JornadaLocal"),
        ("environment", "origin", "CI_config_only"),
        ("environment", "compose_project", "production"),
        ("batch", "entrega_id", "not-uuid"),
        ("batch", "payload_sha256", "not-hash"),
        ("batch", "expected_pessoas", 0),
        ("before", "lote_status", "PROCESSADO"),
        ("before", "heartbeat_lease_id", NEW),
        ("before", "lease_owner", ""),
        ("fault", "transaction_open_after_first_write", False),
        ("fault", "not_committed_before_crash", False),
        ("fault", "worker_pid_changed", False),
        ("fault", "after_host_pid", 1002),
        ("fault", "sql_transaction_rollback_observed", False),
        ("fault", "after_restart_count", 0),
        ("fault", "after_instance_id", OLD_WORKER),
        ("recovery", "previous_token_expired", False),
        ("recovery", "old_lease_heartbeat_deleted", False),
        ("recovery", "old_lease_fenced", False),
        ("recovery", "new_lease_id", OLD),
        ("recovery", "recuperacao_count", 0),
        ("recovery", "tentativa_count", 1),
        ("terminal", "lote_status", "PROCESSANDO"),
        ("terminal", "entrega_status", "VALIDANDO"),
        ("terminal", "lease_id", NEW),
        ("terminal", "lote_heartbeat_rows", 1),
        ("terminal", "silver_people", 2),
        ("terminal", "silver_records", 0),
        ("terminal", "processed_items", 1),
        ("terminal", "gold_and_serving_consistent", False),
        ("terminal", "duplicate_business_keys", 1),
        ("idempotency_replay", "same_zip_and_idempotency_key", False),
        ("idempotency_replay", "additional_lotes", 1),
        ("idempotency_replay", "additional_silver_rows", 1),
        ("sql_provenance", "during_open_transaction", False),
        ("sql_provenance", "after_lease_recovery", False)
    ]
    for section, key, value in changes:
        invalid = deepcopy(fixture())
        invalid[section][key] = value
        run(invalid, 1, f"{section}.{key} should fail")
    print(f"C3.2f2: PASS 1 synthetic consistency example and {len(changes)} negative cases (NO SQL/CI)")

if __name__ == "__main__":
    main()
