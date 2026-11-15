#!/usr/bin/env python3
"""Fail-closed PREPARE artifact admission by a distinct TRAIN-side runner.

This is an isolated transfer PROBE, not a scientific 100k TRAIN/TEST run.
It never opens independent sealed TEST data and never accepts a bare cache hit.
"""
import argparse
import importlib.util
import json
from pathlib import Path

module_path = Path(__file__).with_name("v9-corpus-snapshot.py")
spec = importlib.util.spec_from_file_location("v9_corpus_snapshot_verify", module_path)
snapshot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(snapshot)

def verify_handoff(corpus: Path, config_path: Path, receipt_path: Path) -> dict:
    if config_path.is_symlink() or receipt_path.is_symlink():
        raise ValueError("Untrusted symlink in PREPARE artifact metadata")
    if not config_path.is_file() or not receipt_path.is_file():
        raise ValueError("Missing PREPARE transfer metadata")
    config = json.loads(config_path.read_text(encoding="utf-8"))
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if not isinstance(config, dict) or not config or not isinstance(receipt, dict):
        raise ValueError("Invalid PREPARE transfer metadata")
    manifest = snapshot.verify(corpus, config)
    if receipt.get("status") not in ("GENERATED", "REUSED"):
        raise ValueError("Unexpected PREPARE state")
    handoff = receipt.get("handoff")
    if not isinstance(handoff, dict) or handoff.get("status") != "HANDOFF_VERIFIED":
        raise ValueError("Unverified PREPARE handoff receipt")
    if receipt.get("fingerprint") != manifest["fingerprint"] \
            or handoff.get("fingerprint") != manifest["fingerprint"]:
        raise ValueError("Handoff fingerprint does not match content")
    if receipt.get("files") != manifest["files"]:
        raise ValueError("Handoff content allowlist/hash/size diverged")
    return {
        "status": "TRANSFER_VERIFIED_NONSCIENTIFIC",
        "fingerprint": manifest["fingerprint"],
        "file_count": len(manifest["files"]),
        "consumer": "TRAIN_ADMISSION_PROBE_ONLY",
        "scientific_test_used": False,
    }

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--prepare-receipt", type=Path, required=True)
    args = parser.parse_args()
    evidence = verify_handoff(args.corpus, args.config, args.prepare_receipt)
    print("V9_CORPUS_CROSS_RUNNER_HANDOFF_PASS " + json.dumps(evidence, sort_keys=True))

if __name__ == "__main__":
    main()
