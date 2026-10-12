#!/usr/bin/env python3
"""Read-only, evidence-based GitHub CI runtime report.

No Github write permissions, no untrusted checkout, no secrets, and no
claim that measured wall-time equals the dependency-graph critical path.
"""
from __future__ import annotations

import argparse
import datetime as dt
import html
import json
from pathlib import Path


def parsed(value: str | None) -> dt.datetime | None:
    if not value:
        return None
    try:
        result = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return result if result.tzinfo else None


def duration(start: str | None, end: str | None) -> float | None:
    a, b = parsed(start), parsed(end)
    if not a or not b or b < a:
        return None
    return (b - a).total_seconds()


def escape(value: object) -> str:
    return html.escape(str(value), quote=False).replace("|", r"\|").replace("\n", " ")


def observed_build_steps(jobs: list[dict]) -> list[tuple[str, str, float | None, int]]:
    """Lower bound based on explicit step names, not hidden script internals."""
    result: list[tuple[str, str, float | None, int]] = []
    for job in jobs:
        if job.get("conclusion") == "skipped":
            continue
        for step in job.get("steps", []):
            if step.get("conclusion") != "success":
                continue
            name = step.get("name", "")
            normalized = name.lower()
            if "dotnet build jornada.sln" in normalized or "build for codeql" in normalized:
                count = 1
            elif "prove deterministic double build" in normalized:
                count = 2
            else:
                continue
            result.append((
                str(job.get("name", "")),
                name,
                duration(step.get("started_at"), step.get("completed_at")),
                count,
            ))
    return result


def render(run: dict, payload: dict) -> str:
    jobs = payload.get("jobs")
    if not isinstance(jobs, list):
        raise ValueError("GitHub job list missing")
    total = payload.get("total_count")
    if type(total) is not int or len(jobs) != total:
        raise ValueError("Incomplete job pagination: profiling must not silently omit jobs")
    if run.get("status") != "completed":
        raise ValueError("CI run is not completed")
    if not isinstance(run.get("id"), int):
        raise ValueError("Run ID missing")
    if not isinstance(run.get("head_sha"), str) or len(run["head_sha"]) != 40:
        raise ValueError("HEAD SHA missing or invalid")

    timings = []
    for job in jobs:
        if job.get("conclusion") == "skipped":
            continue
        seconds = duration(job.get("started_at"), job.get("completed_at"))
        timings.append((job.get("name", "unknown"), seconds, job.get("conclusion", "unknown")))
    elapsed = [seconds for _, seconds, _ in timings if seconds is not None]
    starts = [parsed(job.get("started_at")) for job in jobs if job.get("conclusion") != "skipped"]
    ends = [parsed(job.get("completed_at")) for job in jobs if job.get("conclusion") != "skipped"]
    starts = [s for s in starts if s]
    ends = [e for e in ends if e]
    wall_seconds = (max(ends) - min(starts)).total_seconds() if starts and ends else None
    build_steps = observed_build_steps(jobs)

    lines = [
        "## CI elapsed-time profile (read-only)",
        "",
        f"Run: {run['id']} · Exact HEAD: \`{escape(run['head_sha'])}\`",
        "",
        f"- Non-skipped jobs: **{len(timings)}** (of {total})",
        f"- Observed elapsed time from first job start to last finish: **{wall_seconds / 60:.2f} min**" if wall_seconds is not None else "- Elapsed span: unavailable",
        f"- Sum of observed non-skipped job execution times: **{sum(elapsed) / 60:.2f} runner-min**",
        f"- Explicit .NET compilation steps observed: **{sum(x[3] for x in build_steps)}** "
        f"(lower bound; inferred from step names, not a complete build trace)",
        "",
        "### Longest observed jobs",
        "",
        "| Job | Duration | Conclusion |",
        "| --- | ---: | --- |",
    ]
    for name, seconds, conclusion in sorted(timings, key=lambda x: x[1] or -1, reverse=True)[:10]:
        value = f"{seconds / 60:.2f} min" if seconds is not None else "unavailable"
        lines.append(f"| {escape(name)} | {value} | {escape(conclusion)} |")
    lines += [
        "",
        "### Observable .NET build steps",
        "",
        "| Job | Step | Duration | Explicit compilations |",
        "| --- | --- | ---: | ---: |",
    ]
    for job, step, seconds, count in build_steps[:30]:
        value = f"{seconds:.0f} s" if seconds is not None else "unavailable"
        lines.append(f"| {escape(job)} | {escape(step)} | {value} | {count} |")
    if not build_steps:
        lines.append("| — | No explicitly named .NET build steps | — | 0 |")
    lines += [
        "",
        "Job durations overlap. Runner-min is not wall time; the longest job is "
        "**not** necessarily the DAG critical path. Reuse of compiled binaries "
        "requires SHA/provenance and equivalence gates before removing any build.",
    ]
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--jobs", type=Path, required=True)
    opts = parser.parse_args()
    print(render(
        json.loads(opts.run.read_text(encoding="utf-8")),
        json.loads(opts.jobs.read_text(encoding="utf-8")),
    ), end="")


if __name__ == "__main__":
    main()
