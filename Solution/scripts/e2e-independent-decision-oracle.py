#!/usr/bin/env python3
"""Independent offline decision comparison. No Runner execution or scientific TEST."""
import argparse
import json
from pathlib import Path


def _read(path: Path, expected_key: str):
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
    if not isinstance(data, dict) or set(data) != {"zipSha256", expected_key}:
        raise ValueError("invalid decision evidence contract")
    digest = data["zipSha256"]
    if not isinstance(digest, str) or len(digest) != 64 or any(
        char not in "0123456789abcdef" for char in digest
    ):
        raise ValueError("invalid ZIP SHA256")
    rows = data[expected_key]
    if not isinstance(rows, list) or not rows:
        raise ValueError("decision rows must be a nonempty list")
    indexed = {}
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"observationId", "decision", "identityId"}:
            raise ValueError("invalid decision row")
        observation = row["observationId"]
        decision = row["decision"]
        identity = row["identityId"]
        if not isinstance(observation, str) or not observation or observation != observation.strip():
            raise ValueError("invalid observationId")
        if decision not in ("MATCH", "ABSTAIN", "NO_MATCH") or not isinstance(decision, str):
            raise ValueError("invalid decision")
        if decision == "MATCH":
            if not isinstance(identity, str) or not identity or identity != identity.strip():
                raise ValueError("MATCH requires canonical identityId")
        elif identity is not None:
            raise ValueError("non-MATCH must have null identityId")
        if observation in indexed:
            raise ValueError("duplicate observationId")
        indexed[observation] = (decision, identity)
    return digest, indexed


def compare(truth_path: Path, observed_path: Path):
    if truth_path.resolve() == observed_path.resolve():
        raise ValueError("truth and observed must be separate files")
    truth_digest, truth = _read(truth_path, "expectedDecisions")
    observed_digest, observed = _read(observed_path, "observedDecisions")
    if truth_digest != observed_digest:
        raise ValueError("decision evidence belongs to a different ZIP")
    missing = sorted(truth.keys() - observed.keys())
    unexpected = sorted(observed.keys() - truth.keys())
    divergent = sorted(key for key in truth.keys() & observed.keys()
                       if truth[key] != observed[key])
    return {
        "status": "PASS" if not (missing or unexpected or divergent) else "FAIL",
        "scope": "OFFLINE_DECISION_ROWS_ONLY_NOT_RUNNER_PARITY_OR_SCIENTIFIC_TEST",
        "zipSha256": truth_digest,
        "expectedCount": len(truth),
        "observedCount": len(observed),
        "missingObservations": missing,
        "unexpectedObservations": unexpected,
        "divergentObservations": divergent,
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
