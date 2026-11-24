#!/usr/bin/env python3
"""Classifica PRs apenas em docs-only ou substantive.

A regra é intencionalmente conservadora: somente documentação reconhecida pode
dispensar o CI integral. Qualquer outra alteração exige todos os gates.
"""
from __future__ import annotations
import argparse
import html
import json
import os
import re
import subprocess
from pathlib import Path

DT10_PREFIXES = (
    "Solution/tests/Jornada.Integration.Tests/Integration/DT10Publication",
)
DT10_EXACT = {
    ".github/workflows/ci.yml",
    ".github/workflows/dt10-evidence.yml",
    "Solution/tests/Jornada.Integration.Tests/Integration/SqlBatchRunner.cs",
    "Solution/tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj",
    "Solution/database/Jornada_Dev_SyntheticScale.sql",
    "Solution/database/migrations/20260910_Schema_Consolidation_370.sql",
    "Solution/database/migrations/20260926_Linkage_Publicacao_Progressiva_Lote.sql",
    "Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs",
}

# Complete unit suite also validates changes to its own gate (pilot/self-change).
UNIT_FULL_PREFIXES = ("Solution/src/", "Solution/tests/")
UNIT_FULL_EXACT = {
    ".github/workflows/jornada-pr-required-fast.yml",
    ".github/workflows/ci.yml",
    "Solution/scripts/ci-impact-classifier.py",
    "Solution/scripts/test-evidence-gate.py",
}

# Closed documentation paths used by every automatic workflow.
DOC_PATH = re.compile(r"Solution/docs/[A-Za-z0-9_-]+\.(?:md|txt)")
PROTECTED_NAMES = ("lock", "manifest", "fixture", "openapi", "solutionschema")


def is_document(path: str) -> bool:
    return path == "README.md" or (
        DOC_PATH.fullmatch(path) is not None
        and not any(term in path.lower() for term in PROTECTED_NAMES)
    )


def classify(paths: list[str]) -> dict:
    docs = [path for path in paths if is_document(path)]
    substantive = [path for path in paths if not is_document(path)]
    docs_only = bool(paths) and not substantive
    return {
        "classification": "docs-only" if docs_only else "substantive",
        "docs_only": docs_only,
        "run_full": not docs_only,
        "run_unit_full": any(p in UNIT_FULL_EXACT or p.startswith(UNIT_FULL_PREFIXES)
                             for p in paths) or not paths,
        "run_dt10": any(p in DT10_EXACT or p.startswith(DT10_PREFIXES) for p in paths),
        "paths": paths,
        "docs": docs,
        "substantive": substantive,
    }


def git(*args: str) -> bytes:
    return subprocess.run(["git", *args], check=True, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE).stdout


def event_impact(event: dict, event_name: str, checkout_sha: str) -> dict:
    base = head = ""
    paths = []
    try:
        if event_name == "pull_request":
            pr = event["pull_request"]
            base, head = pr["base"]["sha"], pr["head"]["sha"]
            if pr["base"]["ref"] != "master":
                raise ValueError("PR targets another branch")
        elif event_name == "push":
            base, head = event["before"], event["after"]
            if (event.get("forced") is not False or event["ref"] != "refs/heads/master"
                    or head != checkout_sha):
                raise ValueError("unverified master push")
        else:
            raise ValueError("event outside automatic documentation policy")
        if not all(isinstance(s, str) and re.fullmatch(r"[0-9a-f]{40}", s)
                   and s != "0" * 40 for s in (base, head)):
            raise ValueError("invalid or missing event SHA")
        if event_name == "pull_request":
            base = git("merge-base", base, head).decode().strip()
        else:
            git("merge-base", "--is-ancestor", base, head)
        # No rename detection: both old and new paths must qualify.
        raw = git("diff", "--raw", "-z", "--no-renames", "--no-ext-diff", base, head)
        entries = raw.split(b"\0")[:-1]
        modes_ok = True
        for i in range(0, len(entries), 2):
            old, new = entries[i].split()[:2]
            modes_ok &= old in (b":000000", b":100644") and new in (b"000000", b"100644")
            paths.append(os.fsdecode(entries[i + 1]))
        if not paths or not modes_ok:
            raise ValueError("empty diff or non-document Git mode")
        result = classify(paths)
        if result["docs_only"]:
            stats = git("diff", "--numstat", "-z", "--no-renames", "--no-ext-diff",
                        "--no-textconv", base, head).split(b"\0")[:-1]
            if len(stats) != len(paths) or any(row.startswith(b"-\t-\t") for row in stats):
                raise ValueError("binary or unverified document diff")
        # Preserve the complete existing post-merge suite for substantive pushes.
        if event_name == "push" and result["run_full"]:
            result["run_dt10"] = True
        reason = "verified git diff"
    except (KeyError, TypeError, ValueError, IndexError, OSError, subprocess.CalledProcessError) as err:
        result = classify(paths)
        result.update(docs_only=False, run_full=True, run_dt10=True,
                      run_unit_full=True, classification="substantive")
        reason = str(err)
    result.update(base=base, head=head, reason=reason)
    return result


def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--files-from")
    source.add_argument("--github-event", action="store_true")
    parser.add_argument("--summary")
    args = parser.parse_args()
    if args.github_event:
        try:
            event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_bytes())
        except (OSError, KeyError, ValueError):
            event = {}
        result = event_impact(event, os.environ["GITHUB_EVENT_NAME"], os.environ["GITHUB_SHA"])
        payload = json.dumps(result, indent=2, ensure_ascii=True)
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            # Escape untrusted changed filenames in the job summary.
            summary.write("### Impact classification\n<pre>" + html.escape(payload) + "</pre>\n")
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="ascii") as output:
            for key in ("docs_only", "run_full", "run_dt10", "run_unit_full"):
                print(f"{key}={str(result[key]).lower()}", file=output)
    else:
        result = classify(Path(args.files_from).read_text().splitlines())
    if args.summary:
        Path(args.summary).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(result["classification"])
    for key in ("docs_only", "run_full", "run_dt10", "run_unit_full"):
        print(f"{key}={str(result[key]).lower()}")


if __name__ == "__main__":
    main()
