#!/usr/bin/env python3
"""Audit GitHub Actions push/master evidence for an exact integrated commit.

Read-only, fail-closed: absence of evidence never means success.
"""
import argparse
import json
import os
import urllib.error
import urllib.parse
import urllib.request


def fetch_pages(url, token, key):
    records = []
    page = 1
    while True:
        sep = "&" if "?" in url else "?"
        request = urllib.request.Request(
            f"{url}{sep}per_page=100&page={page}",
            headers={"Accept": "application/vnd.github+json",
                     "Authorization": f"Bearer {token}",
                     "X-GitHub-Api-Version": "2022-11-28"},
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
        batch = payload[key]
        records.extend(batch)
        if len(batch) < 100:
            return records
        page += 1


def audit(repo, sha, token, api_base="https://api.github.com"):
    repo_path = urllib.parse.quote(repo, safe="/")
    base = f"{api_base.rstrip('/')}/repos/{repo_path}/actions"
    runs = fetch_pages(f"{base}/runs?branch=master&event=push", token, "workflow_runs")
    matching = [run for run in runs if run.get("head_sha") == sha
                and run.get("head_branch") == "master" and run.get("event") == "push"]
    evidence = []
    for run in matching:
        run_id = run["id"]
        jobs = fetch_pages(f"{base}/runs/{run_id}/jobs?filter=latest", token, "jobs")
        evidence.append({
            "runId": run_id,
            "runAttempt": run.get("run_attempt"),
            "workflow": run.get("name"),
            "status": run.get("status"),
            "conclusion": run.get("conclusion"),
            "url": run.get("html_url"),
            "jobs": [{"name": job.get("name"), "status": job.get("status"),
                      "conclusion": job.get("conclusion")} for job in jobs],
        })
    return {"scope": "MASTER_PUSH_CI_EVIDENCE_ONLY", "repository": repo,
            "headSha": sha, "evidenceStatus": "FOUND" if evidence else "NO_RUN_EVIDENCE",
            "runs": evidence}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True, help="owner/repository")
    parser.add_argument("--sha", required=True, help="40-character master commit SHA")
    args = parser.parse_args()
    if len(args.sha) != 40 or any(c not in "0123456789abcdef" for c in args.sha):
        parser.error("--sha must be a lowercase 40-character hex SHA")
    if len(args.repo.split("/")) != 2 or not all(args.repo.split("/")):
        parser.error("--repo must be owner/repository")
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        parser.error("GITHUB_TOKEN is required (read-only Actions access)")
    try:
        result = audit(args.repo, args.sha, token)
    except (urllib.error.URLError, ValueError, KeyError) as error:
        parser.exit(2, f"CI evidence retrieval failed: {error}\n")
    print(json.dumps(result, indent=2))
    if result["evidenceStatus"] != "FOUND":
        parser.exit(1)


if __name__ == "__main__":
    main()
