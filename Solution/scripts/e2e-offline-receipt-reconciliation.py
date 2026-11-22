#!/usr/bin/env python3
"""Offline receipt reconciliation; not a physical E2E or scientific certification."""
import argparse
import hashlib
import json
import os
from pathlib import Path

ALLOWED_SCOPES = {
    "OFFLINE_IDENTITY_SET_ONLY_NOT_SCIENTIFIC_TEST",
    "OFFLINE_DECISION_ROWS_ONLY_NOT_RUNNER_PARITY_OR_SCIENTIFIC_TEST",
    "OFFLINE_GROUP_PARTITION_ONLY_NOT_RUNNER_PARITY_OR_SCIENTIFIC_TEST",
    "OFFLINE_UNDIRECTED_EDGE_SET_ONLY_NOT_SCORING_OR_SCIENTIFIC_TEST",
}


def reconcile(paths, zip_path=None):
    if not paths:
        raise ValueError("at least one receipt is required")
    if any(os.path.samefile(left, right) for i, left in enumerate(paths)\n           for right in paths[i + 1:]):\n        raise ValueError("duplicate receipt path")\n    if zip_path is not None and any(os.path.samefile(zip_path, p) for p in paths):\n        raise ValueError("ZIP input must be separate from receipt inputs")
    digests = set()
    scopes = set()
    for path in paths:
        def unique(pairs):
            result = {}
            for key, value in pairs:
                if key in result:
                    raise ValueError("duplicate JSON member")
                result[key] = value
            return result

        receipt = json.loads(path.read_text(encoding="utf-8"),
                             object_pairs_hook=unique,
                             parse_constant=lambda _: (_ for _ in ()).throw(
                                 ValueError("nonfinite JSON value")))
        if not isinstance(receipt, dict):
            raise ValueError("invalid receipt")
        status = receipt.get("status")
        scope = receipt.get("scope")
        digest = receipt.get("zipSha256")
        if status not in ("PASS", "FAIL") or not isinstance(status, str):
            raise ValueError("invalid receipt status")
        if scope not in ALLOWED_SCOPES or not isinstance(scope, str):
            raise ValueError("unrecognized receipt scope")
        if scope in scopes:
            raise ValueError("duplicate receipt scope")
        if not isinstance(digest, str) or len(digest) != 64 or any(
            c not in "0123456789abcdef" for c in digest
        ):
            raise ValueError("invalid receipt ZIP SHA256")
        if status != "PASS":
            raise ValueError("input receipt is not PASS")
        scopes.add(scope)
        digests.add(digest)
    if len(digests) != 1:
        raise ValueError("receipts belong to different ZIPs")
    if zip_path is not None:
        hasher = hashlib.sha256()
        with zip_path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                hasher.update(chunk)
        if hasher.hexdigest() != next(iter(digests)):
            raise ValueError("receipt ZIP digest differs from supplied ZIP bytes")
    return {
        "status": "PASS",
        "scope": "OFFLINE_RECEIPT_RECONCILIATION_ONLY_NOT_E2E_CERTIFICATION",
        "zipSha256": next(iter(digests)),
        "receiptCount": len(paths),
        "verifiedScopes": sorted(scopes),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--receipt", type=Path, action="append", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--zip", type=Path, help="Optional ZIP bytes to verify against all receipt digests")
    args = parser.parse_args()
    if args.output.resolve() in {p.resolve() for p in args.receipt} or (\n        args.output.exists() and any(os.path.samefile(args.output, p) for p in args.receipt)\n    ):\n        raise ValueError("output must be separate from input receipts")\n    if args.zip is not None and (args.output.resolve() == args.zip.resolve() or (\n        args.output.exists() and os.path.samefile(args.output, args.zip)\n    )):\n        raise ValueError("output must be separate from ZIP input")
    result = reconcile(args.receipt, zip_path=args.zip)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
