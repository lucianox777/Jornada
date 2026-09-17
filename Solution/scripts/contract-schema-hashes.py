#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "config" / "contracts"
APPROVALS = ROOT / "config" / "governance" / "schema-approvals.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    actual = {
        path.relative_to(ROOT).as_posix(): sha256(path)
        for path in sorted(CONTRACTS.rglob("*.json"))
    }
    approved = json.loads(APPROVALS.read_text(encoding="utf-8"))
    listed = {
        row["path"]: row.get("sha256")
        for row in approved.get("contracts", [])
        if isinstance(row, dict) and isinstance(row.get("path"), str)
    }

    missing = [
        {"path": path, "sha256": digest, "status": "PENDENTE", "approval": None}
        for path, digest in actual.items()
        if path not in listed
    ]
    divergent = [
        {"path": path, "listedSha256": listed[path], "actualSha256": digest}
        for path, digest in actual.items()
        if path in listed and listed[path] != digest
    ]
    stale = sorted(path for path in listed if path not in actual)

    print(json.dumps({
        "contractCount": len(actual),
        "missingFromApprovals": missing,
        "divergentHashes": divergent,
        "staleApprovalPaths": stale,
        "contracts": [{"path": path, "sha256": digest} for path, digest in actual.items()],
    }, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
