"""Offline tests for GitHub Actions status classification; no API or CI required."""
import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/github-actions-status-summary.py"
spec = importlib.util.spec_from_file_location("github_actions_status_summary", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GithubActionsStatusSummaryTests(unittest.TestCase):
    def test_unmatched_jobs_never_allow_false_green(self):
        result = module.summarize({
            "workflow_runs": [{"id": 1, "status": "completed", "conclusion": "success"}],
            "jobs_by_run": {"999": [
                {"id": 9, "status": "completed", "conclusion": "success"}]},
        })
        self.assertEqual(result["unmatched_job_run_ids"], ["999"])
        self.assertFalse(result["all_completed_success"])
        self.assertEqual(
            result["approval_scope"],
            "provided_runs_and_jobs_only_not_master_or_branch_protection",
        )

    def test_required_fast_executes_status_regressions(self):
        workflow = (Path(__file__).resolve().parents[3] /
                    ".github/workflows/jornada-pr-required-fast.yml").read_text(encoding="utf-8")
        self.assertIn(
            "python3 -m unittest Solution/tests/scripts/test_github_actions_status_summary.py",
            workflow,
        )

    def test_mixed_states_are_not_misreported_as_green(self):
        result = module.summarize({
            "workflow_runs": [
                {"id": 1, "name": "P0", "status": "completed", "conclusion": "success"},
                {"id": 2, "name": "IBGE", "status": "in_progress", "conclusion": None},
                {"id": 3, "name": "queued", "status": "queued", "conclusion": None},
                {"id": 4, "name": "failed", "status": "completed", "conclusion": "failure"},
            ],
            "jobs_by_run": {"1": [{"status": "completed", "conclusion": "success"}],
                            "2": [{"status": "in_progress", "conclusion": None}]},
        })
        self.assertEqual(result["run_states"], {
            "failure": 1, "in_progress": 1, "queued": 1, "success": 1})
        self.assertEqual(result["job_states"], {"in_progress": 1, "success": 1})
        self.assertFalse(result["all_completed_success"])

    def test_all_success_only_when_every_run_completed(self):
        self.assertTrue(module.summarize({"workflow_runs": [
            {"status": "completed", "conclusion": "success"}]})["all_completed_success"])
        self.assertFalse(module.summarize({"workflow_runs": []})["all_completed_success"])
        self.assertFalse(module.summarize({"workflow_runs": [
            {"status": "pending", "conclusion": None}]})["all_completed_success"])

    def test_successful_run_with_failed_or_running_job_is_not_green(self):
        for status, conclusion in (("completed", "failure"), ("in_progress", None)):
            with self.subTest(status=status):
                result = module.summarize({
                    "workflow_runs": [{"id": 7, "status": "completed", "conclusion": "success"}],
                    "jobs_by_run": {"7": [{"status": status, "conclusion": conclusion}]},
                })
                self.assertFalse(result["all_completed_success"])
                self.assertEqual(result["job_states"][conclusion or status], 1)

    def test_job_identifiers_are_preserved_per_run(self):
        result = module.summarize({
            "workflow_runs": [{"id": 10, "name": "audit", "status": "in_progress"}],
            "jobs_by_run": {"10": [
                {"id": 101, "name": "frozen-bytes", "status": "completed", "conclusion": "success"},
                {"id": 102, "name": "runtime", "status": "in_progress", "conclusion": None},
            ]},
        })
        self.assertEqual(result["jobs_by_run"]["10"], [
            {"id": 101, "name": "frozen-bytes", "state": "success"},
            {"id": 102, "name": "runtime", "state": "in_progress"},
        ])
        self.assertEqual(result["job_states"], {"in_progress": 1, "success": 1})
        self.assertFalse(result["all_completed_success"])

    def test_unresolved_runs_and_partial_job_coverage_are_explicit(self):
        result = module.summarize({
            "workflow_runs": [
                {"id": 11, "status": "completed", "conclusion": "success"},
                {"id": 12, "status": "queued", "conclusion": None},
            ],
            "jobs_by_run": {"11": [{"id": 110, "status": "completed", "conclusion": "success"}],
                            "99": [{"id": 990, "status": "in_progress"}]},
        })
        self.assertEqual(result["incomplete_run_ids"], [12])
        self.assertEqual(result["unmatched_job_run_ids"], ["99"])
        self.assertEqual(result["job_coverage"], "partial_or_unverified")
        self.assertFalse(result["all_completed_success"])

    def test_malformed_entries_are_rejected(self):
        for snapshot in (
            None,
            {"workflow_runs": [None]},
            {"workflow_runs": [], "jobs_by_run": {"1": [None]}},
        ):
            with self.subTest(snapshot=snapshot), self.assertRaises(ValueError):
                module.summarize(snapshot)

    def test_unknown_job_conclusion_does_not_count_as_success(self):
        result = module.summarize({
            "workflow_runs": [{"status": "completed", "conclusion": "success"}],
            "jobs_by_run": {"1": [{"status": "completed", "conclusion": "unexpected"}]},
        })
        self.assertEqual(result["job_states"], {"other_completed": 1})
        self.assertFalse(result["all_completed_success"])

    def test_bad_snapshot_is_rejected(self):
        with self.assertRaises(ValueError):
            module.summarize({"workflow_runs": {}})
        with self.assertRaises(ValueError):
            module.summarize({"workflow_runs": [], "jobs_by_run": {"x": {}}})


if __name__ == "__main__":
    unittest.main()
