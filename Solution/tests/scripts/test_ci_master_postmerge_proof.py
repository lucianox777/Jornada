import importlib.util
import pathlib
import unittest
from unittest import mock
import io
import json

path = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "ci-master-postmerge-proof.py"
spec = importlib.util.spec_from_file_location("postmerge_proof", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SHA = "a" * 40


def item(name, run_id, status="completed", conclusion="success", **changes):
    value = {"name": name, "id": run_id, "head_sha": SHA, "head_branch": "master",
             "event": "push", "status": status, "conclusion": conclusion}
    value.update(changes)
    return value


class PostmergeProofTests(unittest.TestCase):
    def test_both_workflows_pass(self):
        result = module.evaluate([item("jornada-ci", 1),
                                  item("jornada-pr-required-fast", 2)], SHA)
        self.assertTrue(result["verified"])

    def test_newer_failed_or_pending_run_cannot_be_masked_by_old_success(self):
        for status, conclusion, expected in (
            ("completed", "failure", "failed_or_inconclusive"),
            ("in_progress", None, "pending"),
        ):
            with self.subTest(status=status):
                runs = [item("jornada-ci", 100),
                        item("jornada-ci", 101, status, conclusion),
                        item("jornada-pr-required-fast", 102)]
                result = module.evaluate(runs, SHA)
                self.assertFalse(result["verified"])
                self.assertEqual(result["workflows"]["jornada-ci"]["state"], expected)

    def test_newer_success_supersedes_older_failure(self):
        runs = [item("jornada-ci", 100, "completed", "failure"),
                item("jornada-ci", 101),
                item("jornada-pr-required-fast", 102)]
        self.assertTrue(module.evaluate(runs, SHA)["verified"])

    def test_missing_workflow_fails_closed(self):
        result = module.evaluate([item("jornada-ci", 1)], SHA)
        self.assertFalse(result["verified"])
        self.assertEqual(result["workflows"]["jornada-pr-required-fast"]["state"], "missing")

    def test_pending_and_failure_are_not_success(self):
        runs = [item("jornada-ci", 1, "in_progress", None),
                item("jornada-pr-required-fast", 2, "completed", "failure")]
        result = module.evaluate(runs, SHA)
        self.assertFalse(result["verified"])
        self.assertEqual(result["workflows"]["jornada-ci"]["state"], "pending")

    def test_other_sha_pr_and_manual_runs_do_not_prove_master(self):
        runs = [item("jornada-ci", 1, head_sha="b" * 40),
                item("jornada-pr-required-fast", 2, event="pull_request"),
                item("jornada-ci", 3, event="workflow_dispatch")]
        self.assertFalse(module.evaluate(runs, SHA)["verified"])

    def test_malformed_run_entries_fail_closed(self):
        for malformed in (None, "not a run", {"name": "jornada-ci"},
                          item("jornada-ci", True), item("jornada-ci", -1)):
            with self.subTest(malformed=malformed):
                with self.assertRaises(ValueError):
                    module.evaluate([malformed, item("jornada-pr-required-fast", 2)], SHA)

    def test_invalid_id_in_filtered_run_fails_closed(self):
        # Filtering an unrelated SHA/event must not conceal corrupt API data.
        for invalid_id in (None, True, 0, -1, "123"):
            with self.subTest(invalid_id=invalid_id):
                unrelated = item("jornada-ci", invalid_id, head_sha="b" * 40,
                                 event="pull_request")
                with self.assertRaises(ValueError):
                    module.evaluate([unrelated, item("jornada-ci", 1),
                                     item("jornada-pr-required-fast", 2)], SHA)

    def test_duplicate_run_ids_fail_closed(self):
        with self.assertRaises(ValueError):
            module.evaluate([item("jornada-ci", 1), item("jornada-ci", 1),
                             item("jornada-pr-required-fast", 2)], SHA)

    def test_duplicate_id_across_workflows_fails_closed(self):
        # A GitHub Actions run ID identifies one run, not one run per workflow.
        with self.assertRaises(ValueError):
            module.evaluate([item("jornada-ci", 17),
                             item("jornada-pr-required-fast", 17)], SHA)

    def test_duplicate_id_in_filtered_run_fails_closed(self):
        with self.assertRaises(ValueError):
            module.evaluate([item("jornada-ci", 17, head_sha="b" * 40),
                             item("jornada-ci", 17),
                             item("jornada-pr-required-fast", 18)], SHA)

    def test_fetch_accepts_complete_page_and_scopes_exact_master_push(self):
        payload = {"total_count": 2, "workflow_runs": [item("jornada-ci", 1),
                                                      item("jornada-pr-required-fast", 2)]}
        response = io.BytesIO(json.dumps(payload).encode("utf-8"))
        with mock.patch.object(module.urllib.request, "urlopen", return_value=response) as request:
            runs = module.fetch(SHA, "fake-read-only-token")
        self.assertTrue(module.evaluate(runs, SHA)["verified"])
        url = request.call_args.args[0].full_url
        query = module.urllib.parse.parse_qs(module.urllib.parse.urlparse(url).query)
        self.assertEqual(query["head_sha"], [SHA])
        self.assertEqual(query["branch"], ["master"])
        self.assertEqual(query["event"], ["push"])
        self.assertEqual(query["per_page"], ["100"])

    def test_fetch_rejects_incomplete_pagination(self):
        payload = {"total_count": 101, "workflow_runs": [item("jornada-ci", 1)]}
        response = io.BytesIO(json.dumps(payload).encode("utf-8"))
        with mock.patch.object(module.urllib.request, "urlopen", return_value=response):
            with self.assertRaisesRegex(ValueError, "Incomplete Actions pagination"):
                module.fetch(SHA, "fake-read-only-token")

    def test_fetch_rejects_malformed_total_count(self):
        payload = {"total_count": True, "workflow_runs": []}
        response = io.BytesIO(json.dumps(payload).encode("utf-8"))
        with mock.patch.object(module.urllib.request, "urlopen", return_value=response):
            with self.assertRaises(ValueError):
                module.fetch(SHA, "fake-read-only-token")

    def test_main_emits_json_evidence_on_api_failure_without_leaking_details(self):
        from contextlib import redirect_stdout, redirect_stderr
        from unittest import mock
        stdout, stderr = io.StringIO(), io.StringIO()
        with mock.patch.object(module.sys, "argv", ["proof", "--sha", SHA]), \\
             mock.patch.dict(module.os.environ, {"GH_TOKEN": "fake-token"}), \\
             mock.patch.object(module, "fetch", side_effect=ValueError("secret-marker")), \\
             redirect_stdout(stdout), redirect_stderr(stderr):
            exit_code = module.main()
        self.assertEqual(exit_code, 2)
        report = json.loads(stdout.getvalue())
        self.assertEqual(report["sha"], SHA)
        self.assertFalse(report["verified"])
        self.assertEqual(report["state"], "unverified")
        self.assertNotIn("secret-marker", stdout.getvalue() + stderr.getvalue())

    def test_invalid_sha_rejected(self):
        with self.assertRaises(ValueError):
            module.evaluate([], "a" * 7)


if __name__ == "__main__":
    unittest.main()
