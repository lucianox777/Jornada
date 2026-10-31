#!/usr/bin/env python3
"""Fail-closed verification of exact-SHA manual V9 physical TRAIN evidence."""
import json
import os
import sys
import urllib.request

PHYSICAL_JOB = "V9 full physical 30k SQL TRAIN (when impacted)"


def valid_run(run, sha):
    return (
        run.get("event") == "workflow_dispatch"
        and run.get("head_sha") == sha
        and run.get("status") == "completed"
        and run.get("conclusion") == "success"
    )


def valid_physical_job(jobs):
    return any(
        job.get("name") == PHYSICAL_JOB
        and job.get("status") == "completed"
        and job.get("conclusion") == "success"
        for job in jobs
    )


def fetch_json(url, token):
    req = urllib.request.Request(url, headers={
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    })
    with urllib.request.urlopen(req, timeout=20) as response:
        return json.load(response)


def check(repo, sha, token, fetch=fetch_json):
    if len(sha) != 40 or any(c not in "0123456789abcdef" for c in sha):
        raise ValueError("Invalid exact commit SHA")
    base = f"https://api.github.com/repos/{repo}"
    page = 1
    while True:
        url = f"{base}/actions/workflows/v9-physical-seed42-probe.yml/runs?event=workflow_dispatch&head_sha={sha}&per_page=100&page={page}"
        runs = fetch(url, token).get("workflow_runs", [])
        for run in runs:
            if not valid_run(run, sha):
                continue
            jobs_url = f"{base}/actions/runs/{run['id']}/jobs?per_page=100"
            jobs = fetch(jobs_url, token).get("jobs", [])
            if valid_physical_job(jobs):
                return True
        if len(runs) < 100:
            return False
        page += 1


def main():
    try:
        if not check(os.environ["REPOSITORY"], os.environ["HEAD_SHA"], os.environ["GH_TOKEN"]):
            raise RuntimeError("No successful physical TRAIN job in manual run on exact HEAD SHA")
    except Exception as exc:
        print(f"::error title=DEFERRED_NOT_CERTIFIED::{exc}", file=sys.stderr)
        return 1
    print("PASS: manual physical TRAIN job verified for exact HEAD SHA; DT-15 still separate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
