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
    if receipt.get("certified") is not False or receipt.get("testTruthUsed") is not False:
        raise ValueError("Only noncertifying TEST-truth-free receipts may be verified")
    if receipt.get("runtime") not in ("scientific", "production"):
        raise ValueError("Unknown runtime")
    provenance = receipt.get("provenance")
    if not isinstance(provenance, dict) or set(provenance) != {"runId", "runtimeArtifactSha256"} or not isinstance(provenance.get("runId"), str) or not provenance["runId"].strip():
        raise ValueError("Missing provenance")
    claimed = provenance.get("runtimeArtifactSha256")
    if not isinstance(claimed, str) or len(claimed) != 64 or any(c not in "0123456789abcdef" for c in claimed):
        raise ValueError("Invalid runtime artifact digest")
    path = Path(artifact)
    if not path.is_file() or path.is_symlink():
        raise ValueError("Runtime artifact must be a regular non-symlink file")
    hasher = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(chunk)
    digest = hasher.hexdigest()
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
