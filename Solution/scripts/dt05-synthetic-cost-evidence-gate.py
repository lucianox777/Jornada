#!/usr/bin/env python3
"""DT-05 cost evidence acceptance; no NAS, database, Docker or external data."""
from __future__ import annotations
import json
from pathlib import Path
import sys

FIELDS = {
    "schema_version", "status", "scope", "git_sha", "manifest_count",
    "occurrences", "unique_objects", "logical_bytes", "unique_bytes",
    "physical_bytes", "deduplicated_bytes", "deduplication_fraction",
    "scanned_elapsed_ms", "orphan_count", "deleted_objects",
    "performance_threshold_enforced", "real_nas_measurement",
    "real_cpf_or_ibge_data",
}

def accept(x: dict) -> None:
    assert set(x) == FIELDS, "unknown/missing metrics (do not include person data)"
    assert x["schema_version"] == "JORNADA_DT05_SYNTHETIC_COST_V1"
    assert x["status"] == "PASS"
    assert x["scope"] == "SYNTHETIC_EPHEMERAL_READ_ONLY"
    assert isinstance(x["git_sha"], str) and bool(x["git_sha"])
    for key in ("manifest_count", "occurrences", "unique_objects", "logical_bytes",
                "unique_bytes", "physical_bytes", "deduplicated_bytes",
                "scanned_elapsed_ms", "orphan_count", "deleted_objects"):
        assert type(x[key]) is int and x[key] >= 0, f"invalid metric {key}"
    assert x["manifest_count"] == 8 and x["unique_objects"] == 64
    assert x["occurrences"] == 512
    assert x["logical_bytes"] == x["unique_bytes"] * x["manifest_count"]
    assert x["deduplicated_bytes"] == x["logical_bytes"] - x["unique_bytes"]
    assert x["physical_bytes"] > x["unique_bytes"]  # one synthetic orphan
    assert x["orphan_count"] == 1 and x["deleted_objects"] == 0
    fraction = 1 - x["unique_bytes"] / x["logical_bytes"]
    assert isinstance(x["deduplication_fraction"], (int, float))
    assert abs(fraction - x["deduplication_fraction"]) < 1e-8
    assert x["performance_threshold_enforced"] is False
    assert x["real_nas_measurement"] is False
    assert x["real_cpf_or_ibge_data"] is False

def main() -> None:
    if len(sys.argv) == 2 and sys.argv[1] == "--self-test":
        valid = {
            "schema_version": "JORNADA_DT05_SYNTHETIC_COST_V1",
            "status": "PASS", "scope": "SYNTHETIC_EPHEMERAL_READ_ONLY",
            "git_sha": "TEST",
            "manifest_count": 8, "occurrences": 512, "unique_objects": 64,
            "logical_bytes": 8192, "unique_bytes": 1024,
            "physical_bytes": 1050, "deduplicated_bytes": 7168,
            "deduplication_fraction": 0.875, "scanned_elapsed_ms": 1,
            "orphan_count": 1, "deleted_objects": 0,
            "performance_threshold_enforced": False,
            "real_nas_measurement": False, "real_cpf_or_ibge_data": False,
        }
        accept(valid)
        for bad in (
            {**valid, "status": "SKIPPED"},
            {**valid, "unique_bytes": 0},
            {**valid, "real_nas_measurement": True},
            {**valid, "deleted_objects": 1},
            {**valid, "cpf": "unacceptable"},
        ):
            try:
                accept(bad)
            except (AssertionError, ZeroDivisionError):
                pass
            else:
                raise AssertionError("synthetic DT05 gate accepted an invalid fixture")
        print("DT05 synthetic cost evidence: PASS strict schema + negative gate")
        return
    if len(sys.argv) != 2:
        raise SystemExit("usage: script <real-test-evidence.json> | --self-test")
    path = Path(sys.argv[1])
    x = json.loads(path.read_text(encoding="utf-8"))
    accept(x)
    print("DT05 synthetic cost evidence: PASS 8 manifests, 64 unique objects, "
          "512 logical references, zero deletion; measured latency archived")

if __name__ == "__main__":
    main()
