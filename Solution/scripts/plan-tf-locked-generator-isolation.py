#!/usr/bin/env python3
"""Read-only migration planner for PO-approved historical TF generator isolation.

This tool NEVER writes to the repository, manifests, or synthetic datasets.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

REFERENCE = "b98a01180737909edf7cbd0bee4ba2757d52fe0f"
DIRECTORY = "Solution/src/Jornada.Linkage.SyntheticCorpus"


def git(repo, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True,
                          stdout=subprocess.PIPE).stdout


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inventory(repo, revision):
    paths = git(repo, "ls-tree", "-r", "--name-only", revision, "--", DIRECTORY).decode().splitlines()
    result = {}
    for path in sorted(paths):
        data = git(repo, "show", f"{revision}:{path}")
        result[path] = sha(data)
    lines = "".join(f"{digest}  {path}\n" for path, digest in result.items())
    return result, sha(lines.encode())


def plan(repo, historical_ref=REFERENCE, current_ref="HEAD"):
    old, historical_digest = inventory(repo, historical_ref)
    new, current_digest = inventory(repo, current_ref)
    all_paths = sorted(set(old) | set(new))
    changed = [p for p in all_paths if old.get(p) != new.get(p)]
    return {
        "mode": "READ_ONLY_PLANNING",
        "approved_architecture": "GOV-P0-TF-LOCKED-ISOLAMENTO-GERADOR-HISTORICO-20261120",
        "historical_ref": historical_ref,
        "current_ref": current_ref,
        "historical_file_count": len(old),
        "current_file_count": len(new),
        "historical_tree_sha256": historical_digest,
        "current_tree_sha256": current_digest,
        "changed_files": [{"path": p, "historical_sha256": old.get(p),
                           "current_sha256": new.get(p)} for p in changed],
        "changed_file_count": len(changed),
        "migration_executed": False,
        "manifests_modified": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--historical-ref", default=REFERENCE)
    parser.add_argument("--current-ref", default="HEAD")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    result = plan(args.repo.resolve(), args.historical_ref, args.current_ref)
    data = json.dumps(result, indent=2) + "\n"
    if args.out:
        args.out.write_text(data)
    else:
        print(data, end="")


if __name__ == "__main__":
    main()
