#!/usr/bin/env python3
"""Summarize GitHub Actions run/job snapshots without treating pending as failure.

Input is a JSON object containing workflow_runs and optionally jobs_by_run.
This is an offline reporting utility: it does not call GitHub or modify CI.
"""
import argparse
import json
from collections import Counter
from pathlib import Path


def summarize(snapshot):
    if not isinstance(snapshot, dict):
        raise ValueError("snapshot must be an object")
    runs = snapshot.get("workflow_runs")
    if not isinstance(runs, list):
        raise ValueError("workflow_runs must be a list")
    counts = Counter()
    details = []
    for run in runs:
        if not isinstance(run, dict):
            raise ValueError("each workflow_run must be an object")
        status = run.get("status")
        conclusion = run.get("conclusion")
        if status == "completed":
            category = conclusion if conclusion in ("success", "failure", "cancelled", "skipped", "timed_out") else "other_completed"
        elif status in ("queued", "pending", "in_progress", "requested", "waiting"):
            category = status
        else:
            category = "unknown"
        counts[category] += 1
        details.append({"id": run.get("id"), "name": run.get("name"), "state": category})
    jobs_by_run = snapshot.get("jobs_by_run", {})
    if not isinstance(jobs_by_run, dict):
        raise ValueError("jobs_by_run must be an object")
    job_counts = Counter()
    jobs_detail = {}
    for run_id, jobs in jobs_by_run.items():
        if not isinstance(jobs, list):
            raise ValueError("each jobs_by_run entry must be a list")
        jobs_detail[run_id] = []
        for job in jobs:
            if not isinstance(job, dict):
                raise ValueError("each job must be an object")
            if job.get("status") == "completed":
                conclusion = job.get("conclusion")
                category = conclusion if conclusion in ("success", "failure", "cancelled", "skipped", "timed_out") else "other_completed"
            elif job.get("status") in ("queued", "pending", "in_progress", "requested", "waiting"):
                category = job["status"]
            else:
                category = "unknown"
            job_counts[category] += 1
            jobs_detail[run_id].append({"id": job.get("id"), "name": job.get("name"), "state": category})
    # A job snapshot may be partial; never infer that every job was inspected.
    observed_run_ids = {str(run["id"]) for run in runs if run.get("id") is not None}
    unmatched_job_runs = sorted(str(key) for key in jobs_by_run if str(key) not in observed_run_ids)
    missing_job_run_ids = sorted(observed_run_ids - {str(key) for key in jobs_by_run})
    empty_job_run_ids = sorted(str(key) for key, jobs in jobs_by_run.items()
                               if str(key) in observed_run_ids and not jobs)
    incomplete_run_ids = [
        run.get("id") for run in runs
        if run.get("status") != "completed" or run.get("conclusion") != "success"
    ]
    # A success verdict requires job evidence for every identified run.
    # Missing/empty job lists cannot silently pass via vacuous all([]).
    complete_success = bool(runs) and not unmatched_job_runs and not missing_job_run_ids and not empty_job_run_ids and all(
        run.get("status") == "completed" and run.get("conclusion") == "success"
        for run in runs
    ) and all(
        job.get("status") == "completed" and job.get("conclusion") == "success"
        for jobs in jobs_by_run.values() for job in jobs
    )
    return {
        "total_runs": len(runs),
        "run_states": dict(sorted(counts.items())),
        "job_states": dict(sorted(job_counts.items())),
        "jobs_by_run": jobs_detail,
        "incomplete_run_ids": incomplete_run_ids,
        "unmatched_job_run_ids": unmatched_job_runs,
        "missing_job_run_ids": missing_job_run_ids,
        "empty_job_run_ids": empty_job_run_ids,
        "all_observed_runs_have_jobs": not missing_job_run_ids and not empty_job_run_ids,
        "job_coverage": "partial_or_unverified",
        "job_evidence_complete_for_observed_runs": False,
        "runs": details,
        "all_completed_success": complete_success,
        "approval_scope": "provided_runs_and_jobs_only_not_master_or_branch_protection",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("snapshot", type=Path, help="JSON snapshot of workflow_runs and optional jobs_by_run")
    args = parser.parse_args()
    print(json.dumps(summarize(json.loads(args.snapshot.read_text(encoding="utf-8"))),
                     ensure_ascii=False, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
