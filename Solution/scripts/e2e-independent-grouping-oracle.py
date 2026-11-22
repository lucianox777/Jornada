#!/usr/bin/env python3
"""Offline grouping partition oracle; not a V9 Runner or scientific certification."""
import argparse
import json
from pathlib import Path


def load(path: Path, key: str):
    def unique(pairs):
        result = {}
        for name, value in pairs:
            if name in result:
                raise ValueError("duplicate JSON member")
            result[name] = value
        return result

    data = json.loads(path.read_text(encoding="utf-8"),
                      object_pairs_hook=unique,
                      parse_constant=lambda value: (_ for _ in ()).throw(
                          ValueError("nonfinite JSON value")))
    if not isinstance(data, dict) or set(data) != {"zipSha256", key}:
        raise ValueError("invalid grouping evidence contract")
    digest = data["zipSha256"]
    if not isinstance(digest, str) or len(digest) != 64 or any(
        char not in "0123456789abcdef" for char in digest
    ):
        raise ValueError("invalid ZIP SHA256")
    groups = data[key]
    if not isinstance(groups, list) or not groups:
        raise ValueError("groups must be a nonempty list")
    seen = set()
    canonical = set()
    for group in groups:
        if not isinstance(group, list) or not group:
            raise ValueError("group must be a nonempty list")
        members = []
        for member in group:
            if not isinstance(member, str) or not member or member != member.strip():
                raise ValueError("invalid group member")
            if member in seen:
                raise ValueError("duplicate observation across groups")
            seen.add(member)
            members.append(member)
        canonical.add(tuple(sorted(members)))
    return digest, canonical, seen


def compare(truth_path: Path, observed_path: Path):
    if truth_path.resolve() == observed_path.resolve():
        raise ValueError("truth and observed must be separate files")
    expected_digest, expected, expected_members = load(truth_path, "expectedGroups")
    observed_digest, observed, observed_members = load(observed_path, "observedGroups")
    if expected_digest != observed_digest:
        raise ValueError("grouping evidence belongs to a different ZIP")
    missing = sorted(expected - observed)
    unexpected = sorted(observed - expected)
    return {
        "status": "PASS" if not missing and not unexpected else "FAIL",
        "scope": "OFFLINE_GROUP_PARTITION_ONLY_NOT_RUNNER_PARITY_OR_SCIENTIFIC_TEST",
        "zipSha256": expected_digest,
        "expectedGroupCount": len(expected),
        "observedGroupCount": len(observed),
        "missingObservations": sorted(expected_members - observed_members),
        "unexpectedObservations": sorted(observed_members - expected_members),
        "missingGroups": [list(group) for group in missing],
        "unexpectedGroups": [list(group) for group in unexpected],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--truth", type=Path, required=True)
    parser.add_argument("--observed", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    result = compare(args.truth, args.observed)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if result["status"] != "PASS":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
