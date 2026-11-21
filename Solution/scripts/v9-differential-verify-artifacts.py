#!/usr/bin/env python3
"""Verify declared runtime artifact hashes against local bytes, fail closed.

Diagnostic only: checking a file digest cannot prove which binary was executed.
"""
import argparse
import hashlib
import json
from pathlib import Path


def verify(receipt, artifact):
    if not isinstance(receipt, dict) or receipt.get("schema") != "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1":
        raise ValueError("Invalid receipt schema")
    provenance = receipt.get("provenance")
    if not isinstance(provenance, dict):
        raise ValueError("Missing provenance")
    claimed = provenance.get("runtimeArtifactSha256")
    if not isinstance(claimed, str) or len(claimed) != 64 or any(c not in "0123456789abcdef" for c in claimed):
        raise ValueError("Invalid runtime artifact digest")
    path = Path(artifact)
    if not path.is_file() or path.is_symlink():
        raise ValueError("Runtime artifact must be a regular non-symlink file")
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != claimed:
        raise ValueError("Runtime artifact bytes do not match declared SHA-256")
    return {"schema": "JORNADA_V9_ARTIFACT_VERIFICATION_V1",
            "runtime": receipt.get("runtime"), "artifactSha256": digest,
            "verified": True, "certified": False, "productionParityCertified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("receipt", type=Path)
    parser.add_argument("artifact", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(json.loads(args.receipt.read_text(encoding="utf-8")), args.artifact), sort_keys=True))


if __name__ == "__main__":
    main()
