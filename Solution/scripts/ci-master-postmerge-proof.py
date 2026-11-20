#!/usr/bin/env python3
"""Read-only fail-closed proof that required CI ran on an exact merged master SHA."""
import argparse
import json
import os
import re
import sys
import urllib.parse
import urllib.request

REPO = "lucianox777/Jornada"
REQUIRED = {"jornada-ci", "jornada-pr-required-fast"}


def evaluate(runs, sha):
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("Exact 40-character SHA required")
    relevant = {}
    for run in runs:
        if run.get("head_sha") != sha or run.get("head_branch") != "master":
            continue
        if run.get("event") != "push":
            continue
        name = run.get("name")
        if name not in REQUIRED:
            continue
        relevant.setdefault(name, []).append(run)
    results = {}
    for name in sorted(REQUIRED):
        items = relevant.get(name, [])
        if not items:
            results[name] = {"state": "missing", "run_ids": []}
        elif any(r.get("status") == "completed" and r.get("conclusion") == "success" for r in items):
            results[name] = {"state": "success", "run_ids": [r["id"] for r in items]}
        elif any(r.get("status") != "completed" for r in items):
            results[name] = {"state": "pending", "run_ids": [r["id"] for r in items]}
        else:
            results[name] = {"state": "failed_or_inconclusive", "run_ids": [r["id"] for r in items]}
    return {"sha": sha, "verified": all(v["state"] == "success" for v in results.values()),
            "workflows": results}


def fetch(sha, token):
    params = urllib.parse.urlencode({"head_sha": sha, "branch": "master",
                                     "event": "push", "per_page": 100, "page": 1})
    req = urllib.request.Request(
        f"https://api.github.com/repos/{REPO}/actions/runs?{params}",
        headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                 "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "jornada-postmerge-proof"})
    with urllib.request.urlopen(req, timeout=25) as response:
        payload = json.load(response)
    items = payload.get("workflow_runs")
    total = payload.get("total_count")
    if not isinstance(items, list) or type(total) is not int or total != len(items):
        raise ValueError("Incomplete Actions pagination; cannot prove post-merge")
    return items


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sha", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.sha):
        parser.error("Exact 40-character lowercase commit SHA required")
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if not token:
        parser.error("Read-only GH_TOKEN/GITHUB_TOKEN required")
    try:
        report = evaluate(fetch(args.sha, token), args.sha)
    except (ValueError, OSError) as exc:
        print("UNVERIFIED: " + str(exc), file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2))
    return 0 if report["verified"] else 1


if __name__ == "__main__":
    sys.exit(main())
