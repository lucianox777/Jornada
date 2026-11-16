#!/usr/bin/env python3
"""Fail-closed durable-store preflight for a provisioned external POSIX mount.

Does not provision storage, infer durability from local files, or accept Actions
cache as a durable provider. Requires explicit operator attestation.
"""
import argparse
import json
import os
from pathlib import Path


def validate(root: Path, attestation: dict, expected_repository: str):
    if not isinstance(attestation, dict) or attestation.get("schema") != "v9-external-store-attestation-v1":
        raise ValueError("Invalid external store attestation schema")
    if attestation.get("repository") != expected_repository:
        raise ValueError("External store repository binding mismatch")
    if attestation.get("provider_type") != "external-posix-shared":
        raise ValueError("Only externally provisioned shared POSIX mounts are supported")
    if attestation.get("cross_run_persistence_verified") is not True:
        raise ValueError("Cross-run durability has not been independently verified")
    if not isinstance(attestation.get("evidence_run_ids"), list) or len(set(attestation["evidence_run_ids"])) < 2:
        raise ValueError("At least two distinct run IDs are required as evidence")
    if any(not isinstance(x, int) or x <= 0 for x in attestation["evidence_run_ids"]):
        raise ValueError("Invalid cross-run evidence IDs")
    if not root.is_absolute() or any(p.is_symlink() for p in (root, *root.parents)):
        raise ValueError("External store path must be absolute and symlink-free")
    if not root.is_dir():
        raise ValueError("External store mount is absent")
    if root == Path("/"):
        raise ValueError("Filesystem root cannot be an external corpus store")
    if not os.path.ismount(root):
        raise ValueError("External store must be a mounted filesystem")
    if not os.access(root, os.R_OK | os.W_OK | os.X_OK):
        raise ValueError("External store mount permissions are insufficient")
    return {"status": "EXTERNAL_POSIX_PREFLIGHT_PASS", "root": str(root),
            "evidence_run_ids": attestation["evidence_run_ids"]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--store", type=Path, required=True)
    parser.add_argument("--attestation", type=Path, required=True)
    parser.add_argument("--repository", required=True)
    args = parser.parse_args()
    if args.attestation.is_symlink() or not args.attestation.is_file():
        parser.error("Attestation missing or unsafe")
    result = validate(args.store, json.loads(args.attestation.read_text(encoding="utf-8")), args.repository)
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
