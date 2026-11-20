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
    seen_ids = set()
    for run in runs:
        if not isinstance(run, dict):
            raise ValueError("Malformed Actions run entry")
        # Validate identity before filtering: malformed entries cannot silently
        # disappear and make the proof look complete.
        if type(run.get("id")) is not int or run["id"] <= 0:
            raise ValueError("Invalid Actions run ID")
        if run["id"] in seen_ids:
            raise ValueError("Duplicate Actions run ID")
        seen_ids.add(run["id"])
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
        else:
            ids = [r["id"] for r in items]
            if len(ids) != len(set(ids)):
                raise ValueError("Duplicate Actions run ID")
            # A historical green run cannot override a newer failed/pending run.
            # GitHub run IDs are monotonically assigned across workflow runs.
            latest = max(items, key=lambda r: r["id"])
            if latest.get("status") != "completed":
                state = "pending"
            elif latest.get("conclusion") == "success":
                state = "success"
            else:
                state = "failed_or_inconclusive"
            results[name] = {"state": state, "run_ids": [r["id"] for r in items]}
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
    if not isinstance(payload, dict):
        raise ValueError("Malformed Actions API response")
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
        # Keep a machine-readable failure artifact even when the API is
        # unavailable or returns malformed/incomplete data. Never emit tokens.
        print(json.dumps({"sha": args.sha, "verified": False,
                          "state": "unverified", "error_type": type(exc).__name__},
                         indent=2))
        print("UNVERIFIED: " + type(exc).__name__, file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2))
    return 0 if report["verified"] else 1


if __name__ == "__main__":
    sys.exit(main())
