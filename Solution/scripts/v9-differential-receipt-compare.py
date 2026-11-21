#!/usr/bin/env python3
"""Fail-closed comparison of two independently generated V9 diagnostic receipts.

This compares evidence; it does not generate independent runs or certify
production parity. Never provide TEST ground truth to this utility.
"""
import argparse
import hashlib
import json
from pathlib import Path


def _digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()


def _require_sha(value, name):
    if not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdef" for c in value):
        raise ValueError(f"{name}: expected lowercase SHA-256")
    return value


def _canonical(receipt):
    if not isinstance(receipt, dict) or receipt.get("schema") != "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1":
        raise ValueError("Unsupported differential receipt schema")
    if receipt.get("certified") is not False or receipt.get("testTruthUsed") is not False:
        raise ValueError("Receipt must be explicitly noncertifying and TEST-truth-free")
    if receipt.get("runtime") not in ("scientific", "production"):\n        raise ValueError("Receipt must identify its independent runtime")\n    identity = {key: _require_sha(receipt.get(key), key) for key in
                ("modelSha256", "universeSha256", "operationalEdgesSha256")}
    decisions = receipt.get("decisions")
    groups = receipt.get("groups")
    if not isinstance(decisions, list) or not isinstance(groups, list) or not decisions:
        raise ValueError("Missing decisions or groups")
    seen = set()
    canonical_decisions = []
    for item in decisions:
        if not isinstance(item, dict) or set(item) != {"observationId", "decision"}:
            raise ValueError("Invalid decision row")
        obs = item["observationId"]
        decision = item["decision"]
        if not isinstance(obs, int) or isinstance(obs, bool) or obs <= 0 or not isinstance(decision, str) or not decision:
            raise ValueError("Invalid observation or decision")
        if obs in seen:
            raise ValueError("Duplicate observation")
        seen.add(obs)
        canonical_decisions.append((obs, decision))
    members = set()
    canonical_groups = []
    for group in groups:
        if not isinstance(group, list) or not group:
            raise ValueError("Invalid group")
        if any(not isinstance(x, int) or isinstance(x, bool) or x <= 0 for x in group):
            raise ValueError("Invalid group member")
        if len(set(group)) != len(group) or members.intersection(group):
            raise ValueError("Duplicate group membership")
        members.update(group)
        canonical_groups.append(tuple(sorted(group)))
    if members != seen:
        raise ValueError("Groups must partition exactly the decision universe")
    return {**identity, "observationCount": len(seen),
            "decisions": sorted(canonical_decisions),
            "groups": sorted(canonical_groups)}


def compare(left, right):
    if not isinstance(left, dict) or not isinstance(right, dict) or left.get("runtime") != "scientific" or right.get("runtime") != "production":
        raise ValueError("Expected independent scientific and production receipts, in that order")
    a, b = _canonical(left), _canonical(right)
    differences = [key for key in a if a[key] != b[key]]
    return {"schema": "JORNADA_V9_DIFFERENTIAL_COMPARISON_V1",
            "equivalent": not differences, "differentFields": differences,
            "leftEvidenceSha256": _digest(a), "rightEvidenceSha256": _digest(b),
            "certified": False, "productionParityCertified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("left", type=Path)
    parser.add_argument("right", type=Path)
    args = parser.parse_args()
    if args.left.resolve() == args.right.resolve():
        raise ValueError("Cannot compare a receipt with itself")
    result = compare(json.loads(args.left.read_text(encoding="utf-8")),
                     json.loads(args.right.read_text(encoding="utf-8")))
    print(json.dumps(result, sort_keys=True))
    if not result["equivalent"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
