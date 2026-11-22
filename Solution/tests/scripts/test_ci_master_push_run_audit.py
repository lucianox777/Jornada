"""Offline tests for read-only master push evidence audit."""
import importlib.util
from pathlib import Path
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/ci-master-push-run-audit.py"
spec = importlib.util.spec_from_file_location("ci_master_push_audit", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MasterPushAuditTests(unittest.TestCase):
    def test_missing_run_is_not_success(self):
        with mock.patch.object(module, "fetch_pages", return_value=[]):
            result = module.audit("lucianox777/Jornada", "a" * 40, "test")
        self.assertEqual(result["evidenceStatus"], "NO_RUN_EVIDENCE")
        self.assertEqual(result["runs"], [])

    def test_only_exact_master_push_sha_is_selected(self):
        sha = "a" * 40
        runs = [
            {"id": 1, "head_sha": sha, "head_branch": "master", "event": "pull_request"},
            {"id": 2, "head_sha": "b" * 40, "head_branch": "master", "event": "push"},
            {"id": 3, "head_sha": sha, "head_branch": "master", "event": "push",
             "run_attempt": 2, "name": "jornada-ci", "status": "completed",
             "conclusion": "failure", "html_url": "https://example.invalid/run/3"},
        ]
        calls = []

        def fake_fetch(url, token, key):
            calls.append((url, key))
            if key == "workflow_runs":
                return runs
            return [{"name": "unit-runtime", "status": "completed", "conclusion": "failure"}]

        with mock.patch.object(module, "fetch_pages", side_effect=fake_fetch):
            result = module.audit("lucianox777/Jornada", sha, "test")
        self.assertEqual(result["evidenceStatus"], "FOUND")
        self.assertEqual(len(result["runs"]), 1)
        self.assertEqual(result["runs"][0]["runId"], 3)
        self.assertEqual(result["runs"][0]["conclusion"], "failure")
        self.assertEqual(result["runs"][0]["jobs"][0]["conclusion"], "failure")
        self.assertEqual(len(calls), 2)


if __name__ == "__main__":
    unittest.main()
