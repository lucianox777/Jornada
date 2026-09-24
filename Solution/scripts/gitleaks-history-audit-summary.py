#!/usr/bin/env python3
"""Aggregate redacted Gitleaks evidence; never export raw findings or secret matches."""
import argparse
from collections import Counter
import json
from pathlib import Path
import tempfile


def aggregate(path: Path) -> dict:
    if not path.is_file():
        return {"findings": 0, "byRuleAndFile": []}
    rows = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(rows, list):
        raise ValueError(f"Expected a Gitleaks JSON findings list: {path}")
    counts = Counter(
        (
            str(item.get("RuleID") or "UNKNOWN"),
            str(item.get("File") or "UNKNOWN"),
        )
        for item in rows
        if isinstance(item, dict)
    )
    if sum(counts.values()) != len(rows):
        raise ValueError(f"Malformed findings in {path}")
    return {
        "findings": len(rows),
        "byRuleAndFile": [
            {"ruleId": rule, "file": file, "count": count}
            for (rule, file), count in sorted(counts.items())
        ],
    }


def build_summary(current: Path, history: Path, exit_code: int) -> dict:
    if exit_code not in (0, 1):
        raise ValueError(f"Operational scanner error: {exit_code}")
    tree, git = aggregate(current), aggregate(history)
    total = tree["findings"] + git["findings"]
    if exit_code == 1 and total == 0:
        raise ValueError("Scanner reported findings but produced no readable reports.")
    if exit_code == 0 and total != 0:
        raise ValueError("Scanner reported success although its reports contain findings.")
    return {
        "schemaVersion": 1,
        "scope": "current-tree-and-all-reachable-git-refs",
        "scannerExitCode": exit_code,
        "outcome": "FINDINGS_REQUIRE_TRIAGE" if total else "NO_FINDINGS_DETECTED",
        "currentTree": tree,
        "gitHistory": git,
        "note": "Aggregate discovery evidence only; not a clearance or an automatic allowlist.",
    }


def self_test() -> None:
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        current, history = root / "tree.json", root / "history.json"
        sample = [
            {"RuleID": "generic-api-key", "File": "tests/fixture.json",
             "Secret": "MUST_NOT_APPEAR", "Match": "MUST_NOT_APPEAR"},
            {"RuleID": "generic-api-key", "File": "tests/fixture.json",
             "Secret": "ANOTHER_SECRET"},
        ]
        current.write_text(json.dumps(sample), encoding="utf-8")
        history.write_text("[]", encoding="utf-8")
        summary = build_summary(current, history, 1)
        assert summary["currentTree"]["findings"] == 2
        assert summary["currentTree"]["byRuleAndFile"][0]["count"] == 2
        assert "MUST_NOT_APPEAR" not in json.dumps(summary)
        assert "ANOTHER_SECRET" not in json.dumps(summary)
        try:
            build_summary(current, history, 0)
        except ValueError:
            pass
        else:
            raise AssertionError("Report/exit mismatch must fail closed")
        try:
            build_summary(root / "missing", root / "also-missing", 1)
        except ValueError:
            pass
        else:
            raise AssertionError("Missing evidence must fail closed")
        assert build_summary(history, history, 0)["gitHistory"]["findings"] == 0
    print("PASS: aggregate audit summary self-test (no raw secret disclosure)")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--current", type=Path)
    parser.add_argument("--history", type=Path)
    parser.add_argument("--scanner-exit", type=int)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if any(x is None for x in (args.current, args.history, args.scanner_exit, args.output)):
        parser.error("current, history, scanner-exit and output are required")
    summary = build_summary(args.current, args.history, args.scanner_exit)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(summary, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"History audit: {summary['outcome']}; current-tree="
          f"{summary['currentTree']['findings']}; git-history="
          f"{summary['gitHistory']['findings']}. Raw reports stay on runner.")


if __name__ == "__main__":
    main()
