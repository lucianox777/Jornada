#!/usr/bin/env python3
"""Read-only audit of the two frozen TF generator identities; never regenerates LOCKED."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

LOCKS = {
    "Solution/docs/V9_TF_SEEDS_44_47_LOCK.json": "V9-TF-ONE-MF-SEEDS-44-47",
    "Solution/docs/V9_TF_SEEDS_48_57_LOCK.json": "V9-TF-ONE-MF-3SIGMA-SEEDS-48-57",
}
GENERATOR = "Solution/src/Jornada.Linkage.SyntheticCorpus"
PARTITION = GENERATOR + "/SyntheticCorpusGenerator.cs"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(repo, locks_root=None):
    repo = repo.resolve()
    locks_root = (locks_root or repo).resolve()
    tracked = subprocess.run(
        ["git", "-C", str(repo), "ls-files", "--", GENERATOR],
        check=True, capture_output=True, text=True,
    ).stdout.splitlines()
    if not tracked:
        raise ValueError("Generator tracked tree missing")
    if any(not (repo / p).is_file() for p in tracked):
        raise ValueError("Generator tracked file missing")
    lines = "".join(f"{sha256(repo / p)}  {p}\n" for p in sorted(tracked))
    tree = hashlib.sha256(lines.encode("utf-8")).hexdigest()
    partition = sha256(repo / PARTITION)
    results = []
    for relative in LOCKS:
        lock = json.loads((locks_root / relative).read_bytes())
        if not isinstance(lock, dict):
            raise ValueError("LOCKED manifest must be a JSON object: " + relative)
        generation = lock.get("generation")
        if not isinstance(generation, dict):
            raise ValueError("LOCKED generation must be an object: " + relative)
        contract = generation.get("partition_contract")
        if not isinstance(contract, dict):
            raise ValueError("LOCKED partition contract must be an object: " + relative)
        expected_tree = generation.get("generator_sha256")
        expected_partition = contract.get("generator_file_sha256")
        result = {
            "manifest": relative,
            "experiment_id": lock.get("experiment_id"),
            "status": lock.get("status"),
            "tracked_file_count": len(tracked),
            "expected_file_count": 25,
            "actual_generator_sha256": tree,
            "expected_generator_sha256": expected_tree,
            "actual_partition_sha256": partition,
            "expected_partition_sha256": expected_partition,
            "matches": (
                lock.get("status") == "LOCKED"
                and lock.get("experiment_id") == LOCKS[relative]
                and len(tracked) == 25
                and tree == expected_tree
                and partition == expected_partition
            ),
        }
        results.append(result)
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--locks-root", type=Path, help="Read frozen manifests from a separate checkout")
    args = parser.parse_args()
    results = audit(args.repo, args.locks_root)
    print(json.dumps({"audit": "READ_ONLY_NO_CORPUS", "locks": results}, indent=2))
    if not all(result["matches"] for result in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
