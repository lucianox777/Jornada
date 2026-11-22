#!/usr/bin/env python3
"""Offline identity-set comparison; never reads or modifies productive models."""
import argparse
import hashlib
import json
import os
from pathlib import Path


def load_unambiguous_json(path: Path):
    def unique_members(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("duplicate JSON member")
            result[key] = value
        return result

    def reject_nonfinite(value):
        raise ValueError("nonfinite JSON value")

    return json.loads(path.read_text(encoding="utf-8"),
                      object_pairs_hook=unique_members,
                      parse_constant=reject_nonfinite)


def compare(truth_path: Path, observed_path: Path, zip_path: Path | None = None) -> dict:
    if os.path.samefile(truth_path, observed_path):
        raise ValueError("truth and observed evidence must be separate files")
    if zip_path is not None and (
        os.path.samefile(zip_path, truth_path) or
        os.path.samefile(zip_path, observed_path)
    ):
        raise ValueError("physical ZIP must be separate from truth and observed evidence")
    truth = load_unambiguous_json(truth_path)
    observed = load_unambiguous_json(observed_path)
    if not isinstance(truth, dict) or set(truth) != {"zipSha256", "expectedIdentities"}:
        raise ValueError("invalid external truth contract")
    if not isinstance(observed, dict) or set(observed) != {"zipSha256", "observedIdentities"}:
        raise ValueError("invalid observed identity contract")
    digest = truth["zipSha256"]
    if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
        raise ValueError("invalid SHA256")
    if zip_path is not None:
        hasher = hashlib.sha256()
        with zip_path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                hasher.update(chunk)
        if hasher.hexdigest() != digest:
            raise ValueError("physical ZIP SHA256 differs from external truth")
    if observed["zipSha256"] != digest:
        raise ValueError("observed evidence belongs to a different ZIP")
    def identities(value, label):
        if not isinstance(value, list) or not all(isinstance(v, str) and v and v == v.strip() for v in value):
            raise ValueError(f"invalid {label} identity list")
        if len(value) != len(set(value)):
            raise ValueError(f"duplicate {label} identities")
        return set(value)
    expected = identities(truth["expectedIdentities"], "expected")
    actual = identities(observed["observedIdentities"], "observed")
    if not expected:
        raise ValueError("expected identities must not be empty")
    missing, unexpected = sorted(expected - actual), sorted(actual - expected)
    return {"status": "PASS" if not missing and not unexpected else "FAIL",
            "scope": "OFFLINE_IDENTITY_SET_ONLY_NOT_SCIENTIFIC_TEST",
            "expectedCount": len(expected), "observedCount": len(actual),
            "missingIdentities": missing, "unexpectedIdentities": unexpected,
            "zipSha256": digest}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--truth", required=True, type=Path)
    parser.add_argument("--observed", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--zip", type=Path, help="verify actual ZIP bytes against external truth SHA256")
    args = parser.parse_args()
    # Existing hardlinks and symlinks must never turn the output receipt
    # into a destructive overwrite of the independent inputs.
    inputs = (args.truth, args.observed) + ((args.zip,) if args.zip is not None else ())
    if any(args.receipt.resolve() == item.resolve() for item in inputs) or (
        args.receipt.exists() and any(os.path.samefile(args.receipt, item) for item in inputs)
    ):
        raise ValueError("receipt output must be separate from truth, observed and ZIP inputs")
    result = compare(args.truth, args.observed, args.zip)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if result["status"] != "PASS":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
