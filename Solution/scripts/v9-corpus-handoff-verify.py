#!/usr/bin/env python3
"""Fail-closed PREPARE artifact admission by a distinct TRAIN-side runner.

This is an isolated transfer PROBE, not a scientific 100k TRAIN/TEST run.
It never opens independent sealed TEST data and never accepts a bare cache hit.
"""
import argparse
import importlib.util
import json
import os
import shutil
import tempfile
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

def materialize_train_input(corpus: Path, config_path: Path, receipt_path: Path, destination: Path, expected_fingerprint=None) -> dict:
    """Copy only an authenticated PREPARE snapshot to a fresh TRAIN input directory.

    This is an admission primitive, NOT authorization to execute scientific TRAIN.
    Never overwrite a generated corpus or accept a partial destination.
    """
    evidence = verify_handoff(corpus, config_path, receipt_path)
    if expected_fingerprint is not None and evidence["fingerprint"] != expected_fingerprint:
        raise ValueError("TRAIN expected generation fingerprint mismatch")
    if destination.exists() or destination.is_symlink():
        raise ValueError("TRAIN destination must be absent")
    # Reject symlink ancestors, not just the immediate parent. Otherwise an
    # attacker-controlled intermediate directory can redirect the materialization.
    for ancestor in destination.parents:
        if ancestor.is_symlink():
            raise ValueError("Unsafe TRAIN destination ancestor")
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".v9-train-admit-", dir=destination.parent))
    try:
        for name in (*snapshot.REQUIRED, "snapshot.json"):
            shutil.copyfile(corpus / name, temporary / name)
        config = json.loads(config_path.read_text(encoding="utf-8"))
        snapshot.verify(temporary, config)
        if destination.exists() or destination.is_symlink():
            raise ValueError("TRAIN destination appeared during admission")
        os.rename(temporary, destination)
        return {**evidence, "status": "TRAIN_INPUT_ADMITTED_NOT_EXECUTED", "destination": str(destination)}
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--prepare-receipt", type=Path, required=True)
    parser.add_argument("--train-destination", type=Path, help="Opt-in verified copy only; does not execute TRAIN")
    parser.add_argument("--expected-fingerprint", help="Required generation fingerprint when specified by TRAIN")
    args = parser.parse_args()
    if args.expected_fingerprint is not None and args.train_destination is None:
        parser.error("--expected-fingerprint requires --train-destination")
    evidence = (materialize_train_input(args.corpus, args.config, args.prepare_receipt, args.train_destination, args.expected_fingerprint)
                if args.train_destination is not None else
                verify_handoff(args.corpus, args.config, args.prepare_receipt))
    print("V9_CORPUS_CROSS_RUNNER_HANDOFF_PASS " + json.dumps(evidence, sort_keys=True))

if __name__ == "__main__":
    main()
