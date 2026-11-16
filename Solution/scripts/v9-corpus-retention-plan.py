#!/usr/bin/env python3
"""Read-only REF corpus retention planner. Never deletes scientific bytes.

The planner intentionally requires explicit evidence before any future GC
implementation. No evidence supplied means no deletion, not assumed safety.
"""
import argparse
import json
from pathlib import Path


def plan(index: dict, *, active_fingerprints: set[str], protected_fingerprints: set[str]):
    if index.get("schema") != "v9-ref-retention-index-v1":
        raise ValueError("Unsupported REF retention index")
    entries = index.get("entries")
    if not isinstance(entries, list) or not entries:
        raise ValueError("Missing corpus entries")
    if not all(isinstance(item, dict) for item in entries):
        raise ValueError("Malformed corpus entry")
    fingerprints = [item.get("fingerprint") for item in entries]
    if any(not isinstance(x, str) or len(x) != 64 or
           any(c not in "0123456789abcdef" for c in x) for x in fingerprints):
        raise ValueError("Invalid corpus fingerprint")
    if len(set(fingerprints)) != len(fingerprints):
        raise ValueError("Duplicate corpus fingerprint")
    if not active_fingerprints.issubset(set(fingerprints)):
        raise ValueError("Unknown active corpus fingerprint")
    if not protected_fingerprints.issubset(set(fingerprints)):
        raise ValueError("Unknown protected corpus fingerprint")
    groups = {}
    for item in entries:
        key = item.get("configuration")
        if not isinstance(key, str) or not key:
            raise ValueError("Missing configuration identifier")
        if item.get("state") not in ("CURRENT", "PREVIOUS", "HISTORICAL"):
            raise ValueError("Invalid corpus lifecycle state")
        groups.setdefault(key, []).append(item)
    for group in groups.values():
        if sum(item["state"] == "CURRENT" for item in group) != 1:
            raise ValueError("Exactly one validated CURRENT corpus required per configuration")
        current = next(item for item in group if item["state"] == "CURRENT")
        if current.get("validated") is not True or current.get("approved") is not True:
            raise ValueError("CURRENT corpus must be validated and approved")
    results = []
    for item in entries:
        fingerprint = item["fingerprint"]
        # Historical results can require byte-level replay. An index cannot
        # establish absence of dependencies or archival completeness.
        results.append({"fingerprint": fingerprint, "configuration": item["configuration"],
                        "state": item["state"], "action": "RETAIN",
                        "reason": ("CURRENT_APPROVED" if item["state"] == "CURRENT" else
                                   "ACTIVE_READER" if fingerprint in active_fingerprints else
                                   "PROTECTED_REPRODUCIBILITY" if fingerprint in protected_fingerprints else
                                   "GC_NOT_AUTHORIZED_WITHOUT_INDEPENDENT_DEPENDENCY_PROOF")})
    return {"status": "READ_ONLY_NO_DELETION", "entries": results}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.index.is_symlink() or not args.index.is_file():
        parser.error("Missing or unsafe REF index")
    if args.output.exists() or args.output.is_symlink():
        parser.error("Output must be fresh and absent")
    result = plan(json.loads(args.index.read_text(encoding="utf-8")),
                  active_fingerprints=set(), protected_fingerprints=set())
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
