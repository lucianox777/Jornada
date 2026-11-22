#!/usr/bin/env python3
"""Offline candidate-edge set oracle; not a scoring, Runner, or scientific test."""
import argparse
import json
from pathlib import Path


def _read(path: Path, field: str):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("duplicate JSON member")
            result[key] = value
        return result

    data = json.loads(path.read_text(encoding="utf-8"),
                      object_pairs_hook=unique,
                      parse_constant=lambda value: (_ for _ in ()).throw(
                          ValueError("nonfinite JSON value")))
    if not isinstance(data, dict) or set(data) != {"zipSha256", field}:
        raise ValueError("invalid candidate edge evidence contract")
    digest = data["zipSha256"]
    if not isinstance(digest, str) or len(digest) != 64 or any(
        c not in "0123456789abcdef" for c in digest
    ):
        raise ValueError("invalid ZIP SHA256")
    rows = data[field]
    if not isinstance(rows, list):
        raise ValueError("candidate edges must be a list")
    edges = set()
    for row in rows:
        if not isinstance(row, list) or len(row) != 2:
            raise ValueError("candidate edge must be a pair")
        left, right = row
        if any(not isinstance(v, str) or not v or v != v.strip() for v in row):
            raise ValueError("invalid candidate endpoint")
        if left == right:
            raise ValueError("candidate self-edge forbidden")
        edge = tuple(sorted((left, right)))
        if edge in edges:
            raise ValueError("duplicate undirected candidate edge")
        edges.add(edge)
    return digest, edges


def compare(truth_path: Path, observed_path: Path):
    if truth_path.resolve() == observed_path.resolve():
        raise ValueError("truth and observed must be separate files")
    digest, expected = _read(truth_path, "expectedEdges")
    observed_digest, actual = _read(observed_path, "observedEdges")
    if digest != observed_digest:
        raise ValueError("candidate evidence belongs to a different ZIP")
    missing = sorted(expected - actual)
    unexpected = sorted(actual - expected)
    return {
        "status": "PASS" if not missing and not unexpected else "FAIL",
        "scope": "OFFLINE_UNDIRECTED_EDGE_SET_ONLY_NOT_SCORING_OR_SCIENTIFIC_TEST",
        "zipSha256": digest,
        "expectedCount": len(expected),
        "observedCount": len(actual),
        "missingEdges": [list(edge) for edge in missing],
        "unexpectedEdges": [list(edge) for edge in unexpected],
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
