#!/usr/bin/env python3
"""Find the last trustworthy physical 30k success on an ancestor of master.

A green workflow with its physical job skipped DOES NOT cover a commit.
No trusted GitHub API history / failed job / missing receipt => execute 30k.
This read-only gate does not open SQL, TEST, Gold, models or raw corpus.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.request

SHA = re.compile(r"[0-9a-f]{40}\Z")
REPO = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
WORKFLOW = ".github/workflows/v9-physical-seed42-probe.yml"
PHYSICAL = "V9 full physical 30k SQL TRAIN (when impacted)"


def valid_run(row, repo, head, current_id, ancestor):
    if not isinstance(row, dict):
        return False
    sha = row.get("head_sha")
    source = row.get("head_repository")
    return (isinstance(sha, str) and SHA.fullmatch(sha) is not None
            and sha != head and str(row.get("id")) != str(current_id)
            and row.get("status") == "completed"
            and row.get("conclusion") == "success"
            and row.get("event") == "push"
            and row.get("head_branch") == "master"
            and row.get("path") == WORKFLOW
            and isinstance(source, dict) and source.get("full_name") == repo
            and ancestor(sha, head))


def physical_passed(response):
    return isinstance(response, dict) and any(
        isinstance(job, dict) and job.get("name") == PHYSICAL
        and job.get("status") == "completed" and job.get("conclusion") == "success"
        for job in (response.get("jobs") or []))


def receipt_present(response, run_id, sha):
    expected = f"v9-tf-45-physical-{run_id}-{sha}"
    return isinstance(response, dict) and any(
        isinstance(a, dict) and a.get("name") == expected
        and a.get("expired") is False and type(a.get("size_in_bytes")) is int
        and a["size_in_bytes"] > 0 and type(a.get("id")) is int and a["id"] > 0
        for a in (response.get("artifacts") or []))


def select_baseline(runs, repo, head, current_id, ancestor, jobs, artifacts):
    if not isinstance(runs, list):
        return None
    sorted_runs = sorted(
        (r for r in runs if isinstance(r, dict)),
        key=lambda r: (str(r.get("created_at") or ""), int(r.get("id") or 0)),
        reverse=True)
    for row in sorted_runs:
        if not valid_run(row, repo, head, current_id, ancestor):
            continue
        run_id, sha = row["id"], row["head_sha"]
        if physical_passed(jobs(run_id)) and receipt_present(artifacts(run_id), run_id, sha):
            return {"BaselineSha256": sha, "BaselineRunId": run_id}
    return None


def github_json(token, endpoint):
    req = urllib.request.Request(
        "https://api.github.com/" + endpoint,
        headers={"Authorization": "Bearer " + token,
                 "Accept": "application/vnd.github+json",
                 "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(req, timeout=15) as response:
        raw = response.read(8_000_001)
    if len(raw) > 8_000_000:
        raise ValueError("Untrusted API size")
    return json.loads(raw)


def ancestor(a, b):
    return subprocess.run(["git", "merge-base", "--is-ancestor", a, b],
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                          check=False).returncode == 0


def online_baseline(repo, head):
    token = os.getenv("GH_TOKEN") or os.getenv("GITHUB_TOKEN")
    if not token:
        return None
    base = "repos/" + repo + "/actions"
    runs = github_json(token, base + "/workflows/v9-physical-seed42-probe.yml/runs"
                       "?branch=master&event=push&per_page=100")
    return select_baseline(
        runs.get("workflow_runs"), repo, head, os.getenv("GITHUB_RUN_ID"),
        ancestor,
        lambda rid: github_json(token, base + f"/runs/{rid}/jobs?per_page=100"),
        lambda rid: github_json(token, base + f"/runs/{rid}/artifacts?per_page=100"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repository", required=True)
    ap.add_argument("--head-sha", required=True)
    ap.add_argument("--output", type=Path, required=True)
    a = ap.parse_args()
    if not REPO.fullmatch(a.repository) or not SHA.fullmatch(a.head_sha):
        raise SystemExit("Invalid SHA/repository; fail closed")
    if os.getenv("GITHUB_SHA") != a.head_sha or os.getenv("GITHUB_REPOSITORY") != a.repository:
        raise SystemExit("Exact GitHub checkout identity mismatch")
    checkout = subprocess.run(["git", "rev-parse", "HEAD"], text=True,
                              capture_output=True, check=False)
    if checkout.returncode != 0 or checkout.stdout.strip() != a.head_sha:
        raise SystemExit("Checkout HEAD does not match push SHA")
    try:
        found = online_baseline(a.repository, a.head_sha)
    except Exception:
        # No exception or URL printed in public logs: an unavailable API
        # means automatically run the expensive physical gate, NOT skip it.
        found = None
    result = {
        "Contract": "V9_PHYSICAL_CUMULATIVE_BASELINE_V1",
        "HeadSha256": a.head_sha,
        "BaselineSha256": found["BaselineSha256"] if found else None,
        "BaselineRunId": found["BaselineRunId"] if found else None,
        "Decision": "COMPARE_FROM_LAST_VERIFIED_PHYSICAL" if found
                    else "RUN_30K_UNVALIDATED_OR_UNAVAILABLE",
        "TestUsed": False, "Certified": False, "Activated": False}
    with a.output.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, sort_keys=True)
        out.write("\n")
    print("PASS: 30k cumulative provenance evaluated; " + result["Decision"])


if __name__ == "__main__":
    main()
