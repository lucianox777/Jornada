#!/usr/bin/env python3
"""Read-only, paginated GitHub Actions inventory with fail-closed coverage.

Produces a snapshot of unique workflow run IDs and failing jobs. Never
claims a global count if the requested window cannot be fully enumerated.
No GitHub writes, retry, scientific execution, or token persistence.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import sys
import urllib.parse
import urllib.request

REPO = "lucianox777/Jornada"
PAGE_SIZE = 100
MAX_PAGES = 100
CATEGORIES = ("approved", "running", "queued", "failed", "other")


def category(run):
    status, conclusion = run.get("status"), run.get("conclusion")
    if status == "completed":
        if conclusion == "success":
            return "approved"
        if conclusion in ("failure", "timed_out", "action_required"):
            return "failed"
        return "other"
    if status == "in_progress":
        return "running"
    if status in ("queued", "pending", "waiting", "requested"):
        return "queued"
    return "other"


def parse_time(value):
    if not isinstance(value, str):
        raise ValueError("Missing timestamp")
    value = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if value.tzinfo is None:
        raise ValueError("Timestamp must be timezone-aware")
    return value


def api_get(path, token):
    if not path.startswith("/") or "://" in path:
        raise ValueError("Expected relative GitHub API path")
    request = urllib.request.Request(
        "https://api.github.com/repos/" + REPO + path,
        headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "jornada-ci-inventory",
        },
    )
    with urllib.request.urlopen(request, timeout=25) as response:
        return json.load(response)


def enumerate_runs(get, since, until, max_pages=MAX_PAGES):
    """Return all runs within a created-at interval; raise on truncated pages.

    GitHub Actions runs endpoint has a 1000-result cap per search query.
    Narrow created ranges are required when total_count exceeds that cap.
    """
    if since.tzinfo is None or until.tzinfo is None:
        raise ValueError("UTC window requires timezone-aware endpoints")
    if since.utcoffset() != dt.timedelta(0) or until.utcoffset() != dt.timedelta(0):
        raise ValueError("UTC window requires UTC endpoints")
    if since.microsecond or until.microsecond:
        raise ValueError("UTC window boundaries must use whole seconds")
    if since >= until:
        raise ValueError("since must be before until")
    created = urllib.parse.quote(
        since.strftime("%Y-%m-%dT%H:%M:%SZ") + ".." +
        until.strftime("%Y-%m-%dT%H:%M:%SZ"), safe=""
    )
    found = {}
    total = None
    for page in range(1, max_pages + 1):
        payload = get(f"/actions/runs?per_page={PAGE_SIZE}&page={page}&created={created}")
        items = payload.get("workflow_runs")
        count = payload.get("total_count")
        if not isinstance(items, list) or type(count) is not int:
            raise ValueError("Incomplete GitHub Actions response")
        if total is None:
            total = count
            if total > 1000:
                raise ValueError("GitHub 1000-run search cap: split time window")
        elif count != total:
            raise ValueError("Run count changed during pagination: retry snapshot")
        for run in items:
            run_id = run.get("id")
            sha = run.get("head_sha")
            attempt = run.get("run_attempt")
            if type(run_id) is not int or run_id <= 0 or not isinstance(sha, str) or len(sha) != 40 or any(ch not in "0123456789abcdefABCDEF" for ch in sha):
                raise ValueError("Run without valid ID/SHA")
            if type(attempt) is not int or attempt < 1:
                raise ValueError("Run without valid attempt")
            created_at = parse_time(run.get("created_at"))
            if not since <= created_at <= until:
                raise ValueError("Run outside requested created-at window")
            if run_id in found:
                # Pagination changed during enumeration; do not trust totals.
                raise ValueError("Duplicate run ID across pages")
            found[run_id] = run
        if len(found) == total:
            return found
        if not items or len(found) > total:
            raise ValueError("Pagination incomplete or inconsistent")
    raise ValueError("Maximum pages reached: inventory incomplete")


def failing_jobs(get, run_id):
    found = []
    total = None
    seen_jobs = set()
    scanned = 0
    for page in range(1, MAX_PAGES + 1):
        payload = get(f"/actions/runs/{run_id}/jobs?per_page={PAGE_SIZE}&page={page}")
        items = payload.get("jobs")
        count = payload.get("total_count")
        if not isinstance(items, list) or type(count) is not int:
            raise ValueError("Incomplete jobs response")
        if total is None:
            total = count
        elif total != count:
            raise ValueError("Jobs changed during pagination")
        for job in items:
            job_id = job.get("id")
            if type(job_id) is not int or job_id in seen_jobs:
                raise ValueError("Missing or duplicate job ID during pagination")
            seen_jobs.add(job_id)
            if job.get("conclusion") in ("failure", "timed_out", "action_required"):
                found.append({"id": job.get("id"), "name": job.get("name"),
                              "conclusion": job.get("conclusion")})
        scanned += len(items)
        if scanned == total:
            return found
        if not items or scanned > total:
            raise ValueError("Incomplete jobs pagination")
    raise ValueError("Job pagination limit reached")


def snapshot(get, since, until):
    runs = enumerate_runs(get, since, until)
    counts = {key: 0 for key in CATEGORIES}
    rows = []
    failures = []
    for run_id, run in sorted(runs.items()):
        state = category(run)
        counts[state] += 1
        rows.append({
            "run_id": run_id, "attempt": run.get("run_attempt"),
            "workflow": run.get("name"), "event": run.get("event"),
            "branch": run.get("head_branch"), "sha": run["head_sha"],
            "created_at": run["created_at"], "updated_at": run.get("updated_at"),
            "status": run.get("status"), "conclusion": run.get("conclusion"),
            "category": state, "url": run.get("html_url"),
        })
        if state == "failed" or state == "running":
            jobs = failing_jobs(get, run_id)
            if jobs:
                failures.append({"run_id": run_id, "jobs": jobs})
    if sum(counts.values()) != len(rows):
        raise ValueError("Count reconciliation failed")
    return {"coverage": "complete_for_created_window", "repository": REPO,
            "since": since.isoformat(), "until": until.isoformat(),
            "total_unique_runs": len(rows), "counts": counts,
            "runs": rows, "failed_jobs": failures}


def compare_snapshots(previous, current):
    """Compare identical observation windows without double-counting run IDs."""
    if previous.get("repository") != current.get("repository"):
        raise ValueError("Snapshot repositories differ")
    if previous.get("since") != current.get("since") or previous.get("until") != current.get("until"):
        raise ValueError("Snapshot windows differ; cannot claim exact delta")
    if previous.get("coverage") != "complete_for_created_window" or current.get("coverage") != "complete_for_created_window":
        raise ValueError("Cannot compare incomplete snapshots")
    def indexed(snapshot):
        rows = snapshot.get("runs")
        if not isinstance(rows, list):
            raise ValueError("Snapshot runs missing")
        result = {}
        for row in rows:
            run_id = row.get("run_id")
            if type(run_id) is not int or run_id in result or row.get("category") not in CATEGORIES:
                raise ValueError("Invalid or duplicated run ID in snapshot")
            result[run_id] = row
        if len(result) != snapshot.get("total_unique_runs"):
            raise ValueError("Snapshot run total inconsistent")
        return result
    old, new = indexed(previous), indexed(current)
    if set(old) - set(new):
        raise ValueError("Previously observed run disappeared from same window")
    transitions = []
    for run_id in sorted(set(old) & set(new)):
        before, after = old[run_id], new[run_id]
        if before.get("sha") != after.get("sha"):
            raise ValueError("Run SHA changed between observations")
        old_attempt, new_attempt = before.get("attempt"), after.get("attempt")
        if (type(old_attempt) is not int or type(new_attempt) is not int
                or old_attempt < 1 or new_attempt < old_attempt):
            raise ValueError("Run attempt invalid or regressed between observations")
        for immutable in ("event", "created_at"):
            if (before.get(immutable) is not None and after.get(immutable) is not None
                    and before[immutable] != after[immutable]):
                raise ValueError("Run immutable metadata changed between observations")
        if before.get("category") != after.get("category") or before.get("attempt") != after.get("attempt"):
            transitions.append({
                "run_id": run_id,
                "from": before.get("category"), "to": after.get("category"),
                "attempt_before": before.get("attempt"), "attempt_after": after.get("attempt"),
            })
    return {"new_run_ids": sorted(set(new) - set(old)), "transitions": transitions,
            "previous_total": len(old), "current_total": len(new)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--since", required=True, help="UTC ISO-8601 start (inclusive)")
    parser.add_argument("--until", required=True, help="UTC ISO-8601 end (inclusive)")
    parser.add_argument("--output", required=True, help="Local JSON snapshot path")
    parser.add_argument("--previous", help="Prior JSON snapshot of the exact same window")
    args = parser.parse_args()
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if not token:
        parser.error("GH_TOKEN/GITHUB_TOKEN required; read-only Actions access")
    try:
        report = snapshot(lambda path: api_get(path, token),
                          parse_time(args.since), parse_time(args.until))
        if args.previous:
            with open(args.previous, encoding="utf-8") as stream:
                prior = json.load(stream)
            report["delta"] = compare_snapshots(prior, report)
        with open(args.output, "w", encoding="utf-8") as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        print(json.dumps({"total": report["total_unique_runs"],
                          "counts": report["counts"], "coverage": report["coverage"]}))
    except (ValueError, OSError) as exc:
        print("INCOMPLETE INVENTORY: " + str(exc), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
