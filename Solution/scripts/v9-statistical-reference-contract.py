#!/usr/bin/env python3
"""Validate a PII-free statistical reference envelope, never individual observations."""
import argparse
import hashlib
import json
import math
from pathlib import Path

SCHEMA = "v9-statistical-reference-v1"
CATEGORIES = ("person_first", "person_surname", "mother_first", "mother_surname", "birth_day")


def validate(payload):
    if not isinstance(payload, dict) or set(payload) != {"schema", "source", "distributions"}:
        raise ValueError("invalid envelope keys")
    if payload["schema"] != SCHEMA:
        raise ValueError("unsupported schema")
    source = payload["source"]
    if not isinstance(source, dict) or set(source) != {"database_fingerprint_sha256", "snapshot_sha256"}:
        raise ValueError("source must contain only non-identifying fingerprints")
    if any(not isinstance(v, str) or len(v) != 64 or any(c not in "0123456789abcdef" for c in v)
           for v in source.values()):
        raise ValueError("invalid source fingerprint")
    distributions = payload["distributions"]
    if not isinstance(distributions, dict) or set(distributions) != set(CATEGORIES):
        raise ValueError("incomplete distributions")
    for category in CATEGORIES:
        rows = distributions[category]
        if not isinstance(rows, list) or not rows or len(rows) > 100000:
            raise ValueError("invalid distribution size")
        previous = None
        for row in rows:
            if not isinstance(row, dict) or set(row) != {"bucket", "count"}:
                raise ValueError("invalid bucket keys")
            bucket, count = row["bucket"], row["count"]
            if not isinstance(bucket, str) or not bucket or len(bucket) > 128:
                raise ValueError("invalid bucket")
            if previous is not None and bucket <= previous:
                raise ValueError("buckets must be sorted and unique")
            previous = bucket
            if isinstance(count, bool) or not isinstance(count, int) or count < 20 or count > 1000000000:
                raise ValueError("bucket count below suppression threshold or invalid")
    canonical = json.dumps(payload, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode()
    return hashlib.sha256(canonical).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()
    data = json.loads(args.manifest.read_text(encoding="utf-8"))
    print(json.dumps({"schema": SCHEMA, "manifest_sha256": validate(data), "status": "CONTRACT_VALID_ONLY_NOT_APPROVED_FOR_GENERATION"}))


if __name__ == "__main__":
    main()
