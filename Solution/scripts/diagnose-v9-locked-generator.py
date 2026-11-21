#!/usr/bin/env python3
"""Read-only P0-A diagnosis: compare checked-out generator with frozen LOCKs.

Never rewrites manifests, regenerates corpora, accesses SQL or executes TEST.
Run from any directory: python3 Solution/scripts/diagnose-v9-locked-generator.py
"""
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GEN = "Solution/src/Jornada.Linkage.SyntheticCorpus"
LOCKS = (
    "Solution/docs/V9_TF_SEEDS_44_47_LOCK.json",
    "Solution/docs/V9_TF_SEEDS_48_57_LOCK.json",
)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def diagnose():
    paths = subprocess.run(
        ["git", "-C", str(ROOT), "ls-files", "--", GEN],
        check=True, capture_output=True, text=True,
    ).stdout.splitlines()
    paths = sorted(paths)
    if not paths:
        raise RuntimeError("Generator tree not tracked")
    lines = "".join(
        f"{sha256((ROOT / p).read_bytes())}  {p}\n" for p in paths
    )
    observed_tree = sha256(lines.encode("utf-8"))
    observed_partition = sha256(
        (ROOT / GEN / "SyntheticCorpusGenerator.cs").read_bytes()
    )
    results = []
    for name in LOCKS:
        lock = json.loads((ROOT / name).read_bytes())
        generation = lock["generation"]
        expected_tree = generation["generator_sha256"]
        expected_partition = generation["partition_contract"]["generator_file_sha256"]
        results.append({
            "manifest": name,
            "status": lock.get("status"),
            "tracked_generator_files": len(paths),
            "expected_generator_tree_sha256": expected_tree,
            "observed_generator_tree_sha256": observed_tree,
            "generator_tree_matches": expected_tree == observed_tree,
            "expected_partition_file_sha256": expected_partition,
            "observed_partition_file_sha256": observed_partition,
            "partition_file_matches": expected_partition == observed_partition,
            "file_count_matches": len(paths) == 25,
        })
    return results


if __name__ == "__main__":
    results = diagnose()
    print(json.dumps(results, indent=2, sort_keys=True))
    if not all(
        r["status"] == "LOCKED"
        and r["generator_tree_matches"]
        and r["partition_file_matches"]
        and r["file_count_matches"]
        for r in results
    ):
        raise SystemExit("P0-A: generator differs from frozen LOCKED reference")
