#!/usr/bin/env python3
"""Read-only provenance comparison of historical LOCKED generator vs current checkout.

No corpus generation, no manifest rewriting, no TEST access and no promotion.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

GENERATOR = "Solution/src/Jornada.Linkage.SyntheticCorpus"
LOCKS = (
    "Solution/docs/V9_TF_SEEDS_44_47_LOCK.json",
    "Solution/docs/V9_TF_SEEDS_48_57_LOCK.json",
)


def git(repo, *args):
    return subprocess.run(
        ["git", "-C", str(repo), *args], check=True, capture_output=True
    ).stdout


def digest(repo, ref):
    paths = sorted(p for p in git(repo, "ls-tree", "-r", "--name-only", ref, "--", GENERATOR)
                   .decode().splitlines() if p)
    lines = []
    for path in paths:
        blob = git(repo, "show", f"{ref}:{path}")
        lines.append(f"{hashlib.sha256(blob).hexdigest()}  {path}\n")
    return hashlib.sha256("".join(lines).encode()).hexdigest(), paths


def compare(repo):
    results = []
    for path in LOCKS:
        lock = json.loads((repo / path).read_text(encoding="utf-8"))
        gen = lock["generation"]
        historic_ref = gen["generator_commit"]
        historic_digest, historic_paths = digest(repo, historic_ref)
        current_digest, current_paths = digest(repo, "HEAD")
        historic_set, current_set = set(historic_paths), set(current_paths)
        modified = sorted(p for p in historic_set & current_set
                          if git(repo, "show", f"{historic_ref}:{p}") != git(repo, "show", f"HEAD:{p}"))
        results.append({
            "lock": path,
            "historical_ref": historic_ref,
            "manifest_generator_sha256": gen["generator_sha256"],
            "historical_generator_sha256": historic_digest,
            "current_generator_sha256": current_digest,
            "historical_matches_lock": historic_digest == gen["generator_sha256"],
            "current_matches_lock": current_digest == gen["generator_sha256"],
            "historical_file_count": len(historic_paths),
            "current_file_count": len(current_paths),
            "added_paths": sorted(current_set - historic_set),
            "removed_paths": sorted(historic_set - current_set),
            "modified_paths": modified,
            "corpus_byte_equivalence": "NOT_TESTED",
        })
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = {"status": "DIAGNOSTIC_ONLY", "locks": compare(args.repo.resolve())}
    data = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(data, encoding="utf-8")
    print(data)
    if any(not x["historical_matches_lock"] for x in report["locks"]):
        raise SystemExit("Historical generator does not match LOCK: investigate provenance")


if __name__ == "__main__":
    main()
