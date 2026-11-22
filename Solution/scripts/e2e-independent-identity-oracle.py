#!/usr/bin/env python3
"""Offline identity-set comparison; never reads or modifies productive models."""
import argparse
import json
from pathlib import Path


def compare(truth_path: Path, observed_path: Path) -> dict:
    if truth_path.resolve() == observed_path.resolve():
        raise ValueError("truth and observed evidence must be separate files")
    truth = json.loads(truth_path.read_text(encoding="utf-8"))
    observed = json.loads(observed_path.read_text(encoding="utf-8"))
    if not isinstance(truth, dict) or set(truth) != {"zipSha256", "expectedIdentities"}:
        raise ValueError("invalid external truth contract")
    if not isinstance(observed, dict) or set(observed) != {"zipSha256", "observedIdentities"}:
        raise ValueError("invalid observed identity contract")
    digest = truth["zipSha256"]
    if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
        raise ValueError("invalid SHA256")
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
    args = parser.parse_args()
    result = compare(args.truth, args.observed)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if result["status"] != "PASS":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
