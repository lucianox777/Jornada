#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def parse_trx(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    results = [node for node in root.iter() if node.tag.split('}')[-1] == 'UnitTestResult']
    counts = {"total": len(results), "passed": 0, "failed": 0, "skipped": 0, "other": 0}
    for result in results:
        outcome = (result.attrib.get("outcome") or "").strip().lower()
        if outcome == "passed":
            counts["passed"] += 1
        elif outcome == "failed":
            counts["failed"] += 1
        elif outcome in {"notexecuted", "skipped", "notrunnable", "inconclusive"}:
            counts["skipped"] += 1
        else:
            counts["other"] += 1
    return counts


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida evidência TRX de testes críticos sem aceitar skip silencioso.")
    ap.add_argument("trx", nargs="+", help="arquivo(s) .trx")
    ap.add_argument("--forbid-skipped", action="store_true", help="falha se houver teste não executado/skipped")
    ap.add_argument("--minimum-tests", type=int, default=1)
    ap.add_argument("--summary", help="grava JSON consolidado")
    args = ap.parse_args()

    if args.minimum_tests < 1:
        raise SystemExit("ERRO: --minimum-tests deve ser >= 1")

    rows = []
    total = {"total": 0, "passed": 0, "failed": 0, "skipped": 0, "other": 0}
    for raw in args.trx:
        path = Path(raw).resolve()
        if not path.is_file():
            raise SystemExit(f"ERRO: TRX ausente: {path}")
        counts = parse_trx(path)
        rows.append({"file": str(path), **counts})
        for key in total:
            total[key] += counts[key]

    errors = []
    if total["total"] < args.minimum_tests:
        errors.append(f"somente {total['total']} testes; mínimo={args.minimum_tests}")
    if total["failed"]:
        errors.append(f"{total['failed']} teste(s) falharam")
    if total["other"]:
        errors.append(f"{total['other']} resultado(s) TRX desconhecido(s)")
    if args.forbid_skipped and total["skipped"]:
        errors.append(f"{total['skipped']} teste(s) crítico(s) foram ignorados/não executados")

    summary = {
        "schemaVersion": 1,
        "forbidSkipped": args.forbid_skipped,
        "minimumTests": args.minimum_tests,
        "aggregate": total,
        "files": rows,
        "status": "FAIL" if errors else "PASS",
        "errors": errors,
    }
    if args.summary:
        out = Path(args.summary)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    if errors:
        for error in errors:
            print(f"ERRO: {error}", file=sys.stderr)
        return 2
    print(
        "TRX EVIDENCE GATE: OK "
        f"(total={total['total']}; passed={total['passed']}; skipped={total['skipped']})"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ET.ParseError, OSError, ValueError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(2)
