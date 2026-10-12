#!/usr/bin/env python3
"""Record an untrusted completed CI run as a read-only repair checkpoint.

Never fetch or embed logs, environment variables, untrusted commands or secrets.
The incident is informational; only the independent watchdog decides infra retry.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import re
from pathlib import Path

BAD = {"failure", "timed_out", "cancelled", "action_required"}
AGGREGATES = {
    "unit": ("Verify contractual and runtime tests both passed",
             ("unit-contracts", "unit-runtime")),
    "e2e": ("Both independent E2E suites are mandatory",
            ("e2e-console", "e2e-private-sql")),
    "security-analysis": ("Fail closed unless static security and CodeQL both succeeded",
                          ("security-guards", "security-codeql")),
}


def clean(value: object, limit: int = 160) -> str:
    if not isinstance(value, str):
        return "unknown"
    return "".join(ch if 32 <= ord(ch) <= 126 else " " for ch in value)[:limit]


def markdown(value: object) -> str:
    return html.escape(clean(value), quote=False).replace("|", r"\|").replace(chr(96), "&#96;")


def snapshot(run: dict, response: dict) -> dict:
    """Reject partial API responses; never claim a missing job passed."""
    if run.get("status") != "completed" or not isinstance(run.get("id"), int) or run["id"] <= 0:
        raise ValueError("Expected completed CI run with numeric id")
    sha = run.get("head_sha", "")
    if not isinstance(sha, str) or not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise ValueError("Invalid exact HEAD SHA")
    if not isinstance(run.get("run_attempt"), int) or run["run_attempt"] < 1:
        raise ValueError("Missing run attempt")
    if run.get("name") != "jornada-ci":
        raise ValueError("Wrong workflow")
    jobs = response.get("jobs")
    count = response.get("total_count")
    if not isinstance(jobs, list) or type(count) is not int or count != len(jobs):
        raise ValueError("Incomplete job pagination")
    if any(not isinstance(j, dict) or j.get("status") != "completed" for j in jobs):
        raise ValueError("Jobs are incomplete")
    identities = [(j.get("id"), j.get("name")) for j in jobs]
    if any(type(jid) is not int or not isinstance(name, str) or not name for jid, name in identities):
        raise ValueError("Invalid job id/name")
    if len(set(identities)) != len(identities):
        raise ValueError("Duplicate jobs")
    by_name = {j["name"]: j for j in jobs}
    failures = []
    for j in jobs:
        if j.get("conclusion") not in BAD:
            continue
        steps = j.get("steps", [])
        if not isinstance(steps, list):
            raise ValueError("Malformed step list")
        failed_steps = [clean(s.get("name")) for s in steps if isinstance(s, dict)
                        and s.get("conclusion") in BAD]
        name = j["name"]
        tag = "unclassified_needs_log_diagnosis"
        parent_states = None
        if name in AGGREGATES:
            step_name, parents = AGGREGATES[name]
            states = {parent: by_name[parent].get("conclusion", "missing")
                      if parent in by_name else "missing" for parent in parents}
            parent_states = states
            if failed_steps == [step_name] and any(state == "failure" for state in states.values()):
                tag = "possible_dependency_failure_not_proven_infrastructure"
        failures.append({
            "job_id": j["id"],
            "job": clean(name),
            "conclusion": clean(j["conclusion"]),
            "failed_steps": failed_steps[:30],
            "classification": tag,
            **({"producer_states": parent_states} if parent_states else {}),
        })
    # Stable ID does not change when GitHub retries failed jobs within a run.
    stable_id = hashlib.sha256(
        f"jornada-ci/{run['id']}/{sha}".encode("ascii")
    ).hexdigest()[:24]
    return {
        "schema_version": 1,
        "incident_id": stable_id,
        "workflow": "jornada-ci",
        "run_id": run["id"],
        "run_attempt": run["run_attempt"],
        "head_sha": sha,
        "run_conclusion": clean(run.get("conclusion")),
        "total_jobs": count,
        "passed_jobs": sum(j.get("conclusion") == "success" for j in jobs),
        "skipped_jobs": sum(j.get("conclusion") == "skipped" for j in jobs),
        "failing_jobs": failures,
        "retry_authorization": "none_read_only_observation",
        "required_follow_up": (
            "Read authenticated job logs; verify head SHA, runner proof, mandatory "
            "gates, and branch protections independently before any action."
        ),
    }


def render(incident: dict) -> str:
    lines = [
        "## Same-run CI checkpoint / incident (read-only)",
        "",
        f"Incident: <code>{incident['incident_id']}</code> · Run: {incident['run_id']} "
        f"(attempt {incident['run_attempt']})",
        f"Exact HEAD: <code>{incident['head_sha']}</code>",
        f"- Preserved successful jobs: **{incident['passed_jobs']}**",
        f"- Failing/blocked jobs: **{len(incident['failing_jobs'])}**",
        f"- Conditional/skipped jobs: **{incident['skipped_jobs']}**",
        "",
        "| Job | Conclusion | Failed step | Preliminary diagnosis |",
        "| --- | --- | --- | --- |",
    ]
    for entry in incident["failing_jobs"]:
        steps = ", ".join(markdown(s) for s in entry["failed_steps"]) or "unavailable"
        lines.append(f"| {markdown(entry['job'])} | {markdown(entry['conclusion'])} "
                     f"| {steps} | {markdown(entry['classification'])} |")
    if not incident["failing_jobs"]:
        lines.append("| — | No failed jobs | — | — |")
    lines += [
        "",
        "**Never authorize retry or merge from this artifact.** "
        "It contains NO job logs, no runner proof, no test contents and no secrets. "
        "GitHub's original same-run job records remain the checkpoints. "
        "A new commit SHA requires new mandatory validations.",
    ]
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--jobs", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    opts = parser.parse_args()
    incident = snapshot(
        json.loads(opts.run.read_text(encoding="utf-8")),
        json.loads(opts.jobs.read_text(encoding="utf-8")),
    )
    opts.out.parent.mkdir(parents=True, exist_ok=True)
    opts.out.write_text(json.dumps(incident, ensure_ascii=True, sort_keys=True,
                                   indent=2) + "\n", encoding="utf-8")
    print(render(incident), end="")


if __name__ == "__main__":
    main()
