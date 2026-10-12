#!/usr/bin/env python3
"""Offline proof: CI incident checkpoints are complete, read-only, no log claims."""
from __future__ import annotations

import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "ci_failure_checkpoint", ROOT / "scripts" / "ci-failure-checkpoint.py")
checkpoint = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checkpoint)


def fixture():
    run = {
        "name": "jornada-ci", "id": 123, "status": "completed",
        "conclusion": "failure", "head_sha": "a" * 40, "run_attempt": 1,
    }
    jobs = {
        "total_count": 3,
        "jobs": [
            {"id": 5, "name": "unit-runtime", "status": "completed",
             "conclusion": "failure", "steps": [
                 {"name": "Build for CodeQL", "conclusion": "failure"}]},
            {"id": 6, "name": "unit-contracts", "status": "completed",
             "conclusion": "success", "steps": []},
            {"id": 7, "name": "unit", "status": "completed", "conclusion": "failure",
             "steps": [{"name": "Verify contractual and runtime tests both passed",
                        "conclusion": "failure"}]},
        ],
    }
    return run, jobs


class IncidentTests(unittest.TestCase):
    def test_preserves_checkpoint_and_marks_dependent_as_unproven(self):
        run, jobs = fixture()
        incident = checkpoint.snapshot(run, jobs)
        self.assertEqual(incident["passed_jobs"], 1)
        self.assertEqual(len(incident["failing_jobs"]), 2)
        self.assertEqual(incident["failing_jobs"][-1]["classification"],
                         "possible_dependency_failure_not_proven_infrastructure")
        self.assertEqual(incident["retry_authorization"], "none_read_only_observation")
        self.assertIn("NO job logs", checkpoint.render(incident))

    def test_synthetic_aggregate_without_failing_parent_is_not_causal(self):
        run, jobs = fixture()
        jobs["jobs"][0]["conclusion"] = "success"
        incident = checkpoint.snapshot(run, jobs)
        self.assertEqual(incident["failing_jobs"][0]["classification"],
                         "unclassified_needs_log_diagnosis")

    def test_incomplete_job_listing_and_active_runs_fail_closed(self):
        run, jobs = fixture()
        jobs["total_count"] = 100
        with self.assertRaisesRegex(ValueError, "pagination"):
            checkpoint.snapshot(run, jobs)
        run, jobs = fixture()
        jobs["jobs"][0]["status"] = "in_progress"
        with self.assertRaisesRegex(ValueError, "incomplete"):
            checkpoint.snapshot(run, jobs)
        run, jobs = fixture()
        run["status"] = "in_progress"
        with self.assertRaisesRegex(ValueError, "completed"):
            checkpoint.snapshot(run, jobs)

    def test_incident_id_is_stable_after_same_run_retry(self):
        run, jobs = fixture()
        first = checkpoint.snapshot(run, jobs)
        run["run_attempt"] = 2
        jobs["jobs"][0]["conclusion"] = "success"
        other = checkpoint.snapshot(run, jobs)
        self.assertEqual(first["incident_id"], other["incident_id"])
        self.assertEqual(other["run_attempt"], 2)

    def test_job_name_markdown_is_escaped(self):
        run, jobs = fixture()
        jobs["jobs"][0]["name"] = "<script>|whoops" + chr(96)
        report = checkpoint.render(checkpoint.snapshot(run, jobs))
        self.assertNotIn("<script>", report)
        self.assertIn("&lt;script&gt;\\|whoops&#96;", report)

    def test_no_replay_or_untrusted_checkout_in_diagnostics_workflow(self):
        workflow = (ROOT.parent / ".github/workflows/ci-diagnostics.yml").read_text()
        self.assertIn("ci-failure-checkpoint.py", workflow)
        self.assertIn("ref: master", workflow)
        self.assertIn("name: ci-failure-checkpoints", workflow)
        self.assertIn("retention-days: 7", workflow)
        self.assertIn("actions: read", workflow)
        self.assertNotIn("actions: write", workflow)
        self.assertNotIn("contents: write", workflow)
        self.assertNotIn("rerun-failed-jobs", workflow)
        ci = (ROOT.parent / ".github/workflows/ci.yml").read_text()
        self.assertIn("test_ci_failure_checkpoint.py", ci)


if __name__ == "__main__":
    unittest.main()
