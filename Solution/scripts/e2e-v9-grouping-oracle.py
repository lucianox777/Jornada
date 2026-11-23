#!/usr/bin/env python3
"""Independent DEV grouping oracle; never scores, publishes, or certifies V9."""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
import os
from pathlib import Path

MAX_JSON_BYTES = 2 * 1024 * 1024
MAX_TRUTH_BYTES = 8 * 1024 * 1024
MAX_OBSERVATIONS = 4000
SCHEMA = "E2E_V9_GROUPING_OBSERVED_DEV_V1"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def reject_duplicate_members(pairs):
    obj = {}
    for key, value in pairs:
        require(key not in obj, "duplicate JSON member")
        obj[key] = value
    return obj


def reject_nonfinite(value):
    raise ValueError("nonfinite JSON value")


def parse_json(raw):
    return json.loads(raw, object_pairs_hook=reject_duplicate_members,
                      parse_constant=reject_nonfinite)


def checked_bytes(path, maximum):
    require(path.is_file() and not path.is_symlink(),
            "missing or symlinked oracle input")
    require(0 < path.stat().st_size <= maximum, "oracle input size out of bounds")
    return path.read_bytes()


def hex_digest(value, size):
    return (isinstance(value, str) and len(value) == size
            and all(c in "0123456789abcdefABCDEF" for c in value))


def choose2(n):
    return n * (n - 1) // 2


def compare_groupings(manifest_path, truth_path, observed_path,
                      expected_commit, expected_winner):
    """Only aggregated pairs; no observation IDs, truth rows or group IDs leave here."""
    require(hex_digest(expected_commit, 40), "invalid commit SHA")
    require(hex_digest(expected_winner, 64), "invalid expected winner SHA")
    paths = (manifest_path, truth_path, observed_path)
    require(len({p.resolve() for p in paths}) == 3,
            "independent truth and observed inputs must be separate")
    for i in range(len(paths)):
        for j in range(i + 1, len(paths)):
            require(not os.path.samefile(paths[i], paths[j]),
                    "oracle inputs must be separate")
    require(manifest_path.parent.resolve() == truth_path.parent.resolve(),
            "bridge manifest and sidecar must be colocated")
    manifest_bytes = checked_bytes(manifest_path, MAX_JSON_BYTES)
    manifest = parse_json(manifest_bytes)
    require(isinstance(manifest, dict), "invalid bridge manifest")
    sidecar = manifest.get("truthSidecar")
    require(isinstance(sidecar, dict)
            and sidecar.get("path") == "bridge-truth.jsonl"
            and sidecar.get("containsSyntheticGroundTruth") is True
            and sidecar.get("allowedForScoring") is False
            and truth_path.name == "bridge-truth.jsonl",
            "external truth must be isolated from scoring")
    require(hex_digest(sidecar.get("sha256"), 64), "invalid external truth digest")
    truth_bytes = checked_bytes(truth_path, MAX_TRUTH_BYTES)
    require(hashlib.sha256(truth_bytes).hexdigest().upper()
            == sidecar["sha256"].upper(), "external truth sidecar digest mismatch")
    truth_by_source = {}
    for line in truth_bytes.splitlines():
        require(bool(line.strip()), "blank synthetic truth row")
        obj = parse_json(line)
        require(isinstance(obj, dict)
                and obj.get("Status") == "MATERIALIZADA"
                and obj.get("ExclusionReason") is None
                and isinstance(obj.get("OpaquePersonId"), str)
                and obj["OpaquePersonId"].startswith("SYNTH-")
                and isinstance(obj.get("BasePersonId"), str)
                and bool(obj["BasePersonId"].strip())
                and obj["BasePersonId"] == obj["BasePersonId"].strip(),
                "invalid synthetic grouping truth row")
        source = obj["OpaquePersonId"]
        require(source not in truth_by_source, "duplicate source in external truth")
        truth_by_source[source] = obj["BasePersonId"]
        require(len(truth_by_source) <= MAX_OBSERVATIONS, "grouping DEV bound exceeded")
    require(len(truth_by_source) >= 2, "insufficient grouping truth")
    observed = parse_json(checked_bytes(observed_path, MAX_JSON_BYTES))
    require(isinstance(observed, dict)
            and set(observed) == {"schema", "bridgeManifestSha256",
                                  "commitSha", "modelWinnerSha256", "rows"}
            and observed["schema"] == SCHEMA,
            "invalid observed grouping contract")
    manifest_sha = hashlib.sha256(manifest_bytes).hexdigest().upper()
    require(observed["bridgeManifestSha256"] == manifest_sha,
            "observed grouping not bound to bridge manifest")
    require(observed["commitSha"] == expected_commit,
            "observed grouping not bound to checkout SHA")
    require(hex_digest(observed["modelWinnerSha256"], 64)
            and observed["modelWinnerSha256"].upper() == expected_winner.upper(),
            "observed grouping not bound to selected winner")
    rows = observed["rows"]
    require(isinstance(rows, list) and len(rows) == len(truth_by_source),
            "observed grouping does not cover all sources")
    seen = set()
    groups = Counter()
    by_group_truth = defaultdict(Counter)
    states = Counter()
    without_group = 0
    for row in rows:
        require(isinstance(row, dict)
                and set(row) == {"opaquePersonId", "groupId", "identityState"},
                "invalid observed grouping row")
        source, group, state = (row["opaquePersonId"], row["groupId"],
                                row["identityState"])
        require(isinstance(source, str) and source in truth_by_source
                and source not in seen, "unknown or duplicated observed source")
        seen.add(source)
        require(state in ("PROVISORIA", "REFERENCIA", "INDEFINIDA"),
                "invalid observed identity state")
        require(group is None or (isinstance(group, str)
                and 0 < len(group) <= 128 and group == group.strip()),
                "invalid observed group")
        states[state] += 1
        if group is None:
            without_group += 1
        else:
            groups[group] += 1
            by_group_truth[group][truth_by_source[source]] += 1
    require(seen == set(truth_by_source), "missing observed sources")
    actual_base = Counter(truth_by_source.values())
    true_pairs = sum(choose2(n) for n in actual_base.values())
    require(true_pairs > 0, "no positive pairs in external truth")
    tp = sum(choose2(n) for split in by_group_truth.values()
             for n in split.values())
    fp = sum(choose2(n) for n in groups.values()) - tp
    fn = true_pairs - tp
    require(tp >= 0 and fp >= 0 and fn >= 0, "invalid pair counts")
    predicted_pairs = tp + fp
    return {
        "status": "MEASURED_NOT_CERTIFIED",
        "scope": "GROUPING_ONLY_NO_BLOCKING_SCORES_OR_SCIENTIFIC_TEST",
        "sha": expected_commit,
        "modelWinnerSha256": expected_winner.upper(),
        "bridgeManifestSha256": manifest_sha,
        "observations": len(seen),
        "truthIdentities": len(actual_base),
        "predictedGroups": len(groups),
        "withoutGroup": without_group,
        "identityStates": {key: states[key] for key in
                           ("PROVISORIA", "REFERENCIA", "INDEFINIDA")},
        "truePositivePairs": tp, "falsePositivePairs": fp,
        "falseNegativePairs": fn,
        "pairwiseRecallPercent": round(100 * tp / true_pairs, 6),
        "observedPairwiseFdrPercent":
            round(100 * fp / predicted_pairs, 6) if predicted_pairs else None,
        "scientificValidation": False,
        "operationalParityCertified": False,
        "activated": False,
        "E2E_NUCLEO_PASS": False,
        "E2E_INTEGRAL_PASS": False,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bridge-manifest", type=Path, required=True)
    parser.add_argument("--truth-sidecar", type=Path, required=True)
    parser.add_argument("--observed", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--expected-winner", required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    sources = (args.bridge_manifest, args.truth_sidecar, args.observed)
    require(not args.receipt.is_symlink()
            and all(args.receipt.resolve() != p.resolve()
                    and (not args.receipt.exists()
                         or not os.path.samefile(args.receipt, p))
                    for p in sources), "receipt output aliases input evidence")
    receipt = compare_groupings(args.bridge_manifest, args.truth_sidecar,
                                args.observed, args.expected_sha, args.expected_winner)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
