#!/usr/bin/env python3
"""Fail-closed diff classifier for the costly V9 30k TRAIN-only SQL probe.

Only an audited Gold-exclusive allowlist or safe Markdown can opt out.
All unrecognized/ambiguous executable changes require the full probe.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

GOLD_ONLY_RUNNER = frozenset({
    "GoldPartitionBcubedAudit.cs",
    "GoldTrainLowerNonMatchMatrixCommand.cs",
    "GoldTrainThreeOrderAggregateCommand.cs",
    "GoldTrainThreeOrderProtocol.cs",
    "V9GoldDisposableLowerProcedurePatch.cs",
    "V9GoldEndToEndAudit.cs",
    "V9GoldHarnessCommand.cs",
    "V9GoldHarnessDatabasePreparation.cs",
    "V9GoldPhysicalCreationAudit.cs",
    "V9GoldTrainPhysicalEvidence.cs",
    "V9LowerNonMatchStudyCli.cs",
})
GOLD_ONLY_TESTS = frozenset({
    "GoldPartitionBcubedAuditTests.cs",
    "GoldTrainThreeOrderAggregateCommandTests.cs",
    "GoldTrainThreeOrderProtocolTests.cs",
    "V9GoldEndToEndAuditTests.cs",
    "V9GoldHarnessSafetyTests.cs",
    "V9GoldLowerNonMatchExperimentTests.cs",
    "V9GoldPhysicalCreationAuditTests.cs",
    "V9LowerNonMatchStudyCliTests.cs",
})
GOLD_ONLY_SCRIPTS = frozenset({
    "Solution/scripts/v9-gold-12-trial-localdb.ps1",
})


def reason_for_safe(path: str) -> str | None:
    """Whitelist, never a negative broad substring filter over V9 files."""
    if path.startswith("Solution/docs/") and path.lower().endswith((".md", ".txt")):
        return "documentation"
    if path.startswith("Documentos/") and path.lower().endswith(".md"):
        return "documentation"
    if path in {"README.md", "Solution/README.md"}:
        return "documentation"
    runner = "Solution/src/Jornada.Linkage.Runner/"
    if path.startswith(runner) and path[len(runner):] in GOLD_ONLY_RUNNER:
        return "gold-exclusive-runner"
    tests = "Solution/tests/Jornada.Tests/Unit/"
    if path.startswith(tests) and path[len(tests):] in GOLD_ONLY_TESTS:
        return "gold-exclusive-test"
    if path in GOLD_ONLY_SCRIPTS:
        return "gold-exclusive-orchestration"
    return None


def parse_name_status_z(raw: bytes) -> list[tuple[str, str]]:
    """Parse Git's -z records, including both ends of a rename/copy."""
    if not raw or not raw.endswith(b"\0"):
        raise ValueError("Empty or truncated Git diff; do not skip the probe")
    tokens = raw[:-1].split(b"\0")
    entries: list[tuple[str, str]] = []
    i = 0
    while i < len(tokens):
        status = tokens[i].decode("utf-8", errors="strict")
        i += 1
        if not re.fullmatch(r"(?:[ACDMRTUXB]|R\d{1,3}|C\d{1,3})", status):
            raise ValueError(f"Unrecognized git status {status!r}")
        paths_count = 2 if status[0] in "RC" else 1
        if i + paths_count > len(tokens):
            raise ValueError("Truncated Git rename/copy record")
        for token in tokens[i:i + paths_count]:
            path = token.decode("utf-8", errors="strict")
            if not path or path.startswith("/") or "\\" in path or ".." in path.split("/"):
                raise ValueError("Invalid changed path; do not skip the probe")
            entries.append((status, path))
        i += paths_count
    return entries


def classify(entries: list[tuple[str, str]], *, manual: bool = False) -> dict:
    if manual:
        return {"run_physical_30k": True, "decision": "RUN", "reason": "manual-dispatch-always-full", "changes": []}
    if not entries:
        return {"run_physical_30k": True, "decision": "RUN", "reason": "empty-or-unreadable-diff", "changes": []}
    changes: list[dict] = []
    require_probe = False
    for status, path in entries:
        safe = reason_for_safe(path)
        # Conservative: only normal modifications and additions of known-safe paths.
        # Renames, deletions, copies and type changes can conceal a dependency move.
        if status not in {"M", "A"}:
            safe = None
        if safe is None:
            require_probe = True
        changes.append({"status": status, "path": path, "classification": safe or "probe-required-or-unknown"})
    return {
        "run_physical_30k": require_probe,
        "decision": "RUN" if require_probe else "NOT_REQUIRED_FOR_THIS_DIFF",
        "reason": "shared-unknown-or-ambiguous-impact" if require_probe else "only-audited-docs-or-gold-exclusive-paths",
        "changes": changes,
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    group = ap.add_mutually_exclusive_group(required=True)
    group.add_argument("--name-status-z", type=Path)
    group.add_argument("--manual", action="store_true")
    ap.add_argument("--summary", type=Path, required=True)
    ap.add_argument("--github-output", type=Path, required=True)
    args = ap.parse_args()
    result = classify(parse_name_status_z(args.name_status_z.read_bytes()) if args.name_status_z else [], manual=args.manual)
    args.summary.write_text(json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    with args.github_output.open("a", encoding="utf-8") as fh:
        fh.write(f"run_physical_30k={str(result['run_physical_30k']).lower()}\n")
    print(json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
