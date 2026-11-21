#!/usr/bin/env python3
"""Reserve a globally one-use TEST RNG seed before any independent corpus generation.

The GitHub tag is the atomic global seed ledger. Never delete a reservation on
subsequent TEST failure: exposure has occurred once reservation succeeds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import uuid

PREFIX = "refs/tags/v9-tf4-test-seed-v2-"
RESERVED = {42, 20261112001, 20261113001}


def derive_seed(execution_id: uuid.UUID) -> int:
    seed = int.from_bytes(hashlib.sha256(execution_id.bytes).digest()[:8], "big")
    return seed


def gh(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["gh", "api", *args], text=True, capture_output=True, check=False)


def reserve(repository: str, sha: str, run_id: str, attempt: str,
            max_attempts: int = 8) -> dict:
    if attempt != "1":
        raise RuntimeError("V9_TEST_RUN_ATTEMPT_MUST_BE_ONE")
    if not repository or len(sha) != 40 or any(c not in "0123456789abcdef" for c in sha):
        raise RuntimeError("V9_TEST_INVALID_REPOSITORY_OR_SHA")
    for _ in range(max_attempts):
        execution_id = uuid.uuid4()
        seed = derive_seed(execution_id)
        if seed in RESERVED or seed == 0:
            continue
        ref = PREFIX + str(seed)
        result = gh(f"repos/{repository}/git/refs", "--method", "POST",
                    "-f", f"ref={ref}", "-f", f"sha={sha}")
        if result.returncode == 0:
            return {"schema": "V9_TEST_SEED_RESERVATION_V2",
                    "executionGuid": str(execution_id), "seed": seed,
                    "exposureRef": ref, "codeSha": sha,
                    "runId": run_id, "runAttempt": attempt}
        # Only an existing reservation permits a retry. Authentication,
        # permissions, API outages, and all other errors must fail closed.
        existing = gh(f"repos/{repository}/git/ref/{ref.removeprefix('refs/')}")
        if existing.returncode == 0:
            continue
        raise RuntimeError("V9_TEST_SEED_RESERVATION_FAILED_NOT_COLLISION")
    raise RuntimeError("V9_TEST_SEED_COLLISION_RETRY_EXHAUSTED")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--winner-sha", required=True)
    args = parser.parse_args()
    if re.fullmatch(r"[0-9a-f]{64}", args.winner_sha) is None:
        raise RuntimeError("V9_TEST_INVALID_WINNER_SHA")
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("x", encoding="utf-8") as receipt:
        result = reserve(os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_SHA"],
                         os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"])
        result["winnerSnapshotSha256"] = args.winner_sha
        receipt.write(json.dumps(result, indent=2, sort_keys=True) + "\n")
    output = os.environ["GITHUB_OUTPUT"]
    with open(output, "a", encoding="utf-8") as stream:
        stream.write(f"test_seed={result['seed']}\n")
        stream.write(f"test_guid={result['executionGuid']}\n")
        stream.write(f"exposure_ref={result['exposureRef']}\n")
    print("V9_TEST_SEED_RESERVED", result["exposureRef"])
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RuntimeError, OSError, KeyError) as exc:
        print(f"::error::{exc}", file=sys.stderr)
        sys.exit(1)
