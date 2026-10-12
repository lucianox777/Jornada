#!/usr/bin/env python3
"""Offline CI profiling tests; no GitHub token or runner required."""
from __future__ import annotations

import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("ci_run_profile", ROOT / "scripts" / "ci-run-profile.py")
profile = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(profile)


def step(name, started="2026-10-12T12:00:00Z", completed="2026-10-12T12:00:30Z"):
    return {"name": name, "conclusion": "success", "started_at": started, "completed_at": completed}


def fixture():
    run = {"id": 42, "status": "completed", "head_sha": "a" * 40}
    jobs = {
        "total_count": 3,
        "jobs": [
            {
                "name": "unit", "status": "completed", "conclusion": "success",
                "started_at": "2026-10-12T12:00:00Z",
                "completed_at": "2026-10-12T12:03:00Z",
                "steps": [step("Run dotnet build Jornada.sln --configuration Release --no-restore")],
            },
            {
                "name": "deterministic-build", "status": "completed", "conclusion": "success",
                "started_at": "2026-10-12T12:01:00Z",
                "completed_at": "2026-10-12T12:05:00Z",
                "steps": [step("Prove deterministic double build")],
            },
            {
                "name": "scale-harness", "status": "completed", "conclusion": "skipped",
                "started_at": "2026-10-12T12:00:00Z",
                "completed_at": "2026-10-12T12:00:00Z",
                "steps": [],
            },
        ],
    }
    return run, jobs


class ProfileTests(unittest.TestCase):
    def test_elapsed_and_runner_minutes_are_different(self):
        report = profile.render(*fixture())
        self.assertIn("5.00 min", report)
        self.assertIn("7.00 runner-min", report)
        self.assertIn("Explicit .NET compilation steps observed: **3**", report)
        self.assertIn("not** necessarily the DAG critical path", report)

    def test_incomplete_pagination_is_not_reported_as_complete(self):
        run, jobs = fixture()
        jobs["total_count"] = 50
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            profile.render(run, jobs)

    def test_only_successful_named_build_steps_are_counted(self):
        _, jobs = fixture()
        jobs["jobs"][0]["steps"] += [
            {**step("Run dotnet build Jornada.sln"), "conclusion": "failure"},
            step("Tests include word build in the name"),
        ]
        result = profile.observed_build_steps(jobs["jobs"])
        self.assertEqual(sum(x[3] for x in result), 3)

    def test_escape_untrusted_job_names(self):
        run, jobs = fixture()
        jobs["jobs"][0]["name"] = '<script>bad|job</script>'
        report = profile.render(run, jobs)
        self.assertNotIn("<script>", report)
        self.assertIn("&lt;script&gt;bad\\|job&lt;/script&gt;", report)

    def test_required_run_metadata(self):
        run, jobs = fixture()
        run["status"] = "in_progress"
        with self.assertRaisesRegex(ValueError, "not completed"):
            profile.render(run, jobs)

    def test_invalid_clock_values_not_fabricated(self):
        self.assertIsNone(profile.duration("2026-10-12T12:00:00Z", "2026-10-12T11:00:00Z"))
        self.assertIsNone(profile.duration("bad", "2026-10-12T12:00:00Z"))


if __name__ == "__main__":
    unittest.main()
