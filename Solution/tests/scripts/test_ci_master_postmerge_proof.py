import importlib.util
import pathlib
import unittest

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

    def test_invalid_sha_rejected(self):
        with self.assertRaises(ValueError):
            module.evaluate([], "a" * 7)


if __name__ == "__main__":
    unittest.main()
