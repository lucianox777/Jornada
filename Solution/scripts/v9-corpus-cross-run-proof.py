#!/usr/bin/env python3
"""Audit two independent REF store observations; never infer durability from CI artifacts.

Operator must separately establish that both observations read the SAME
externally provisioned REF mount. This script does not certify that claim.
"""
import argparse
import importlib.util
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



def observe(corpus: Path, config_path: Path, *, repository: str, run_id: int,
            store_identity: str, run_attempt: int = 1) -> dict:
    """Generate observation from freshly verified physical bytes, never caller hashes."""
    if config_path.is_symlink() or not config_path.is_file():
        raise ValueError("Unsafe generation configuration")
    if any(p.is_symlink() for p in (corpus, *corpus.parents)):
        raise ValueError("Unsafe corpus path")
    script = Path(__file__).with_name("v9-corpus-snapshot.py")
    spec = importlib.util.spec_from_file_location("ref_snapshot_for_observation", script)
    snapshot = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(snapshot)
    config = json.loads(config_path.read_text(encoding="utf-8"))
    if not isinstance(config, dict) or not config:
        raise ValueError("Invalid generation configuration")
    physical = snapshot.verify(corpus, config)
    observation = {"schema": "v9-ref-cross-run-observation-v1",
                   "repository": repository, "run_id": run_id,
                   "run_attempt": run_attempt, "fingerprint": physical["fingerprint"],
                   "files": physical["files"], "store_identity": store_identity}
    audit(observation, {**observation, "run_id": run_id + 1})
    return observation

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--first", type=Path)
    parser.add_argument("--second", type=Path)
    parser.add_argument("--corpus", type=Path)
    parser.add_argument("--config", type=Path)
    parser.add_argument("--repository")
    parser.add_argument("--run-id", type=int)
    parser.add_argument("--run-attempt", type=int, default=1)
    parser.add_argument("--store-identity")
    args = parser.parse_args()
    if args.corpus is not None:
        if not all((args.config, args.repository, args.run_id, args.store_identity)) or args.first or args.second:
            parser.error("Observation requires corpus, config, repository, run ID and store identity")
        print(json.dumps(observe(args.corpus, args.config, repository=args.repository,
                                 run_id=args.run_id, run_attempt=args.run_attempt,
                                 store_identity=args.store_identity), sort_keys=True))
        return
    if args.first is None or args.second is None:
        parser.error("Audit requires both observations")
    for path in (args.first, args.second):
        if path.is_symlink() or not path.is_file():
            parser.error("Unsafe or missing observation")
    print(json.dumps(audit(json.loads(args.first.read_text()),
                           json.loads(args.second.read_text())), sort_keys=True))


if __name__ == "__main__":
    main()
