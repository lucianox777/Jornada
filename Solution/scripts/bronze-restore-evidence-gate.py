#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida evidência do drill SQL+Bronze, incluindo missing/corruption fail-closed.")
    ap.add_argument("report")
    args = ap.parse_args()
    path = Path(args.report).resolve()
    if not path.is_file():
        raise SystemExit(f"ERRO: report.json ausente: {path}")
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    errors = []
    if data.get("status") != "PASS":
        errors.append("status != PASS")
    for key in ["sourceVerifyPassed", "restoredVerifyPassed", "missingObjectDetected", "corruptObjectDetected", "finalVerifyPassed", "deepVerifyPassed", "gcDryRunPlanPassed"]:
        if data.get(key) is not True:
            errors.append(f"{key} != true")
    if not data.get("bronzeSha256") or not data.get("sqlBackup"):
        errors.append("hash Bronze ou nome do backup ausente")
    if errors:
        for error in errors:
            print(f"ERRO: {error}", file=sys.stderr)
        return 2
    print("BRONZE RESTORE EVIDENCE GATE: OK (source+restore+missing+corrupt+final+deep+gc-dry-run)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (json.JSONDecodeError, OSError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(2)
