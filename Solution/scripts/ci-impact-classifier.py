#!/usr/bin/env python3
"""Classifica PRs apenas em docs-only ou substantive.

A regra é intencionalmente conservadora: somente documentação reconhecida pode
dispensar o CI integral. Qualquer outra alteração exige todos os gates.
"""
from __future__ import annotations
import argparse, json
from pathlib import Path

def classify(paths: list[str]) -> dict:
    normalized=[p.replace("\\","/").lstrip("./") for p in paths]
    docs=[p for p in normalized if p.lower().startswith("solution/docs/") or p.lower().endswith((".md",".txt"))]
    substantive=[p for p in normalized if p not in docs]
    docs_only=bool(normalized) and not substantive
    return {
        "classification": "docs-only" if docs_only else "substantive",
        "docs_only": docs_only,
        "run_full": not docs_only,
        "paths": normalized,
        "docs": docs,
        "substantive": substantive,
    }

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--files-from",required=True)
    ap.add_argument("--summary")
    a=ap.parse_args()
    paths=[x.strip() for x in Path(a.files_from).read_text().splitlines() if x.strip()]
    result=classify(paths)
    payload=json.dumps(result,ensure_ascii=False,indent=2,sort_keys=True)
    if a.summary:
        Path(a.summary).write_text(payload+"\n",encoding="utf-8")
    print(payload)
    print(f"docs_only={'true' if result['docs_only'] else 'false'}")
    print(f"run_full={'true' if result['run_full'] else 'false'}")

if __name__=="__main__":
    main()
