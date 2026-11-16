#!/usr/bin/env python3
"""Audit two independent REF store observations; never infer durability from CI artifacts.

Operator must separately establish that both observations read the SAME
externally provisioned REF mount. This script does not certify that claim.
"""
import argparse
import json
from pathlib import Path


def audit(first: dict, second: dict) -> dict:
    required = ("repository", "run_id", "run_attempt", "fingerprint", "files", "store_identity")
    for receipt in (first, second):
        if not isinstance(receipt, dict) or receipt.get("schema") != "v9-ref-cross-run-observation-v1":
            raise ValueError("Invalid REF observation schema")
        if any(key not in receipt for key in required):
            raise ValueError("Incomplete REF observation")
        if type(receipt["run_id"]) is not int or receipt["run_id"] <= 0:
            raise ValueError("Invalid run ID")
        if type(receipt["run_attempt"]) is not int or receipt["run_attempt"] != 1:
            raise ValueError("Rerun is not independent evidence")
        if not isinstance(receipt["files"], dict) or not receipt["files"]:
            raise ValueError("Missing physical file hashes")
        if not isinstance(receipt["store_identity"], str) or not receipt["store_identity"]:
            raise ValueError("Missing external store identity")
        if not isinstance(receipt["fingerprint"], str) or len(receipt["fingerprint"]) != 64:
            raise ValueError("Invalid fingerprint")
    if first["run_id"] == second["run_id"]:
        raise ValueError("Evidence must originate in distinct workflow runs")
    for key in ("repository", "fingerprint", "files", "store_identity"):
        if first[key] != second[key]:
            raise ValueError("Cross-run REF observation mismatch: " + key)
    return {"status": "TWO_OBSERVATIONS_CONSISTENT_NOT_DURABILITY_CERTIFIED",
            "run_ids": sorted([first["run_id"], second["run_id"]]),
            "fingerprint": first["fingerprint"],
            "requires_independent_mount_attestation": True}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--first", type=Path, required=True)
    parser.add_argument("--second", type=Path, required=True)
    args = parser.parse_args()
    for path in (args.first, args.second):
        if path.is_symlink() or not path.is_file():
            parser.error("Unsafe or missing observation")
    print(json.dumps(audit(json.loads(args.first.read_text()),
                           json.loads(args.second.read_text())), sort_keys=True))


if __name__ == "__main__":
    main()
