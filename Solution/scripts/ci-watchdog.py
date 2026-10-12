#!/usr/bin/env python3
"""Fail-closed hourly CI infrastructure watchdog.

The only write is GitHub's rerun-failed-jobs endpoint. No PR edits, merges,
code generation, approval bypass, cancellations, or local environment access.
All calls use the GitHub Actions run-scoped token, never a stored PAT.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

REPO = "lucianox777/Jornada"
MAX_AGE_HOURS = 24
MAX_RUNS_PER_TICK = 30
MAX_CANDIDATES_PER_TICK = 4
MAX_LOG_BYTES = 2_000_000
# Ambiguous timeouts, test assertions and dependency failures are NOT retryable.
INFRA_LOG_MARKERS = (
    "the hosted runner has encountered an error while running your job",
    "the runner has received a shutdown signal",
    "the runner has stopped communicating with the server",
    "the job has been abandoned because the runner has stopped communicating",
    "the runner lost communication with the server",
    "runner is disconnected from the server",
)
ALLOWED_LOG_HOST_SUFFIXES = (
    ".actions.githubusercontent.com",
    ".blob.core.windows.net",
    ".s3.amazonaws.com",
)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


class GithubApi:
    def __init__(self, token: str):
        if not token:
            raise ValueError("GH_TOKEN missing; never fall back to unauthenticated writes")
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect)

    def request(self, path: str, *, method: str = "GET") -> Any:
        if not path.startswith("/") or "://" in path:
            raise ValueError("Expected relative GitHub REST API path")
        url = "https://api.github.com/repos/" + REPO + path
        request = urllib.request.Request(
            url,
            headers={
                "Authorization": "Bearer " + self.token,
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "jornada-ci-watchdog",
            },
            method=method,
        )
        with self.opener.open(request, timeout=20) as response:
            payload = response.read(2_000_000)
        return json.loads(payload) if payload else {}

    def job_logs(self, job_id: int) -> str:
        """Follow only a trusted signed GitHub log URL, WITHOUT Authorization."""
        path = "/actions/jobs/" + str(int(job_id)) + "/logs"
        try:
            self.request(path)
            return ""  # Endpoint should redirect; no evidence means no retry.
        except urllib.error.HTTPError as exc:
            if exc.code not in (301, 302, 303, 307, 308):
                return ""
            location = exc.headers.get("Location", "")
        parsed = urllib.parse.urlparse(location)
        host = (parsed.hostname or "").lower()
        if (
            parsed.scheme != "https"
            or not host.endswith(ALLOWED_LOG_HOST_SUFFIXES)
            or parsed.username is not None
            or parsed.password is not None
        ):
            return ""
        # Never forward the GitHub token to the logs CDN.
        request = urllib.request.Request(
            location, headers={"User-Agent": "jornada-ci-watchdog"}
        )
        try:
            with urllib.request.urlopen(request, timeout=25) as response:
                raw = response.read(MAX_LOG_BYTES + 1)
        except (OSError, urllib.error.URLError):
            return ""
        if len(raw) > MAX_LOG_BYTES:
            return ""  # Partial logs are not sufficient evidence.
        return raw.decode("utf-8", errors="replace")


def utc_time(value: str) -> dt.datetime:
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


def is_current_run(run: dict, api: Any, master_sha: str, now: dt.datetime) -> bool:
    if (
        run.get("name") != "jornada-ci"
        or run.get("status") != "completed"
        or run.get("conclusion") != "failure"
        or run.get("run_attempt") != 1
        or run.get("event") not in ("push", "pull_request")
    ):
        return False
    try:
        age = now - utc_time(run["created_at"])
    except (KeyError, ValueError, TypeError):
        return False
    if not dt.timedelta(0) <= age <= dt.timedelta(hours=MAX_AGE_HOURS):
        return False
    sha = run.get("head_sha")
    if run["event"] == "push":
        return run.get("head_branch") == "master" and sha == master_sha
    if run.get("head_repository", {}).get("full_name") != REPO:
        return False
    prs = run.get("pull_requests") or []
    if len(prs) != 1:
        return False
    number = prs[0].get("number")
    if not isinstance(number, int):
        return False
    pr = api.request(f"/pulls/{number}")
    return (
        pr.get("state") == "open"
        and pr.get("base", {}).get("ref") == "master"
        and pr.get("head", {}).get("sha") == sha
        and pr.get("head", {}).get("repo", {}).get("full_name") == REPO
    )


def retryable_jobs(payload: dict, logs) -> tuple[bool, str]:
    """Retry only if EVERY failed job has explicit runner-infrastructure evidence."""
    jobs = payload.get("jobs", [])
    if not isinstance(jobs, list) or payload.get("total_count") != len(jobs):
        return False, "incomplete job listing"
    if any(job.get("status") != "completed" for job in jobs):
        return False, "jobs still running"
    failed = [
        j for j in jobs
        if j.get("conclusion") in ("failure", "timed_out", "action_required", "cancelled")
    ]
    if not failed:
        return False, "no failed jobs"
    if any(j.get("conclusion") != "failure" for j in failed):
        return False, "cancelled, timed-out or approval-blocked job"
    for job in failed:
        text = logs(job["id"]).lower()
        if not any(marker in text for marker in INFRA_LOG_MARKERS):
            return False, "no explicit runner-infrastructure evidence"
    return True, f"{len(failed)} runner-infrastructure failure(s)"


def summarize_checkpoint(payload: dict) -> str:
    """Successful jobs are the durable checkpoint in the *original* Actions run.

    A new commit is NOT an equivalent checkpoint: it must pass its own gates.
    """
    jobs = payload.get("jobs")
    if not isinstance(jobs, list) or type(payload.get("total_count")) is not int:
        return "checkpoint unverified"
    if len(jobs) != payload["total_count"]:
        return "checkpoint incomplete"
    passed = sum(j.get("conclusion") == "success" for j in jobs)
    failed = sum(j.get("conclusion") in ("failure", "timed_out", "cancelled", "action_required") for j in jobs)
    skipped = sum(j.get("conclusion") == "skipped" for j in jobs)
    return f"checkpoint: {passed} succeeded, {failed} failed/blocked, {skipped} skipped in same run"


def supervise(api: Any, apply: bool, now: dt.datetime) -> list[str]:
    lines = [
        "## Jornada CI watchdog",
        "",
        f"Mode: {'guarded retry' if apply else 'read-only dry run'}",
        "Scope: latest exact HEAD only, <24h, one attempt, infra-only.",
        "",
    ]
    master_sha = api.request("/branches/master")["commit"]["sha"]
    result = api.request(f"/actions/workflows/ci.yml/runs?per_page={MAX_RUNS_PER_TICK}")
    seen = set()
    candidates = 0
    for run in result.get("workflow_runs", []):
        key = (run.get("event"), run.get("head_branch"), run.get("head_sha"))
        if key in seen:
            continue
        seen.add(key)  # Never retry a previous attempt if a newer run exists.
        if run.get("status") != "completed" or run.get("conclusion") != "failure":
            continue
        if candidates >= MAX_CANDIDATES_PER_TICK:
            break
        if not is_current_run(run, api, master_sha, now):
            continue
        candidates += 1
        run_id = run["id"]
        jobs = api.request(f"/actions/runs/{int(run_id)}/jobs?per_page=100&filter=latest")
        retry, why = retryable_jobs(jobs, api.job_logs)
        checkpoint = summarize_checkpoint(jobs)
        if retry and apply:
            api.request(f"/actions/runs/{int(run_id)}/rerun-failed-jobs", method="POST")
            action = "retried failed jobs once"
        elif retry:
            action = "eligible (dry run)"
        else:
            action = "no retry"
        lines.append(f"- Run {run_id} ({str(run.get('head_sha', ''))[:12]}): {action}; {why}; {checkpoint}.")
    if candidates == 0:
        lines.append("- No eligible failed runs on a current HEAD.")
    lines += [
        "",
        "Selective rerun uses the original Actions run ID and SHA: completed green jobs stay checkpoints.",
        "A changed commit SHA requires fresh exact-HEAD CI verification; no cross-SHA reuse is certified.",
        "Never cancels an active run, suppresses a gate, edits a PR, or merges.",
    ]
    return lines


def main() -> int:
    if os.environ.get("GH_REPO") != REPO:
        print("Refusing to run outside the canonical repository", file=sys.stderr)
        return 2
    api = GithubApi(os.environ.get("GH_TOKEN", ""))
    now = dt.datetime.now(dt.timezone.utc)
    lines = supervise(api, os.environ.get("WATCHDOG_APPLY", "").lower() == "true", now)
    message = "\n".join(lines) + "\n"
    print(message)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as handle:
            handle.write(message)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
