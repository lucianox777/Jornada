"""Fail-closed coverage regression: only a *physical* master 30k validates an ancestor."""
import importlib.util
import unittest
from pathlib import Path

P = Path(__file__).resolve().parents[2] / "scripts" / "v9-physical-coverage-baseline.py"
spec = importlib.util.spec_from_file_location("v9_coverage", P)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

REPO = "example/Jornada"
HEAD = "b" * 40
OLD = "a" * 40


def row(sha=OLD, conclusion="success", event="push"):
    return {"id": 512, "head_sha": sha, "head_branch": "master",
            "event": event, "status": "completed", "conclusion": conclusion,
            "path": m.WORKFLOW, "created_at": "2026-11-06T10:00:00Z",
            "head_repository": {"full_name": REPO}}


def jobs(conclusion="success"):
    return {"jobs": [{"name": m.PHYSICAL, "status": "completed",
                      "conclusion": conclusion}]}


def artifacts(sha=OLD, present=True):
    return {"artifacts": [{"id": 50, "expired": False, "size_in_bytes": 2400,
                           "name": f"v9-tf-45-physical-512-{sha}" if present else "other"}]}


class CumulativeCoverageTests(unittest.TestCase):
    def choose(self, entries, *, job_status="success", receipt=True,
               ancestor=None, current="999"):
        return m.select_baseline(entries, REPO, HEAD, current,
            ancestor or (lambda a, b: a == OLD and b == HEAD),
            lambda _: jobs(job_status), lambda _: artifacts(present=receipt))

    def test_authenticated_physical_ancestor_is_baseline(self):
        self.assertEqual(self.choose([row()]),
                         {"BaselineSha256": OLD, "BaselineRunId": 512})

    def test_successful_workflow_but_skipped_physical_cannot_cover_change(self):
        for state in ("skipped", "failure", "cancelled"):
            self.assertIsNone(self.choose([row()], job_status=state))

    def test_failed_or_non_push_workflow_cannot_cover_change(self):
        for state in ("failure", "skipped", "cancelled"):
            self.assertIsNone(self.choose([row(conclusion=state)]))
        self.assertIsNone(self.choose([row(event="workflow_dispatch")]))

    def test_missing_artifact_nonancestor_and_current_run_rejected(self):
        self.assertIsNone(self.choose([row()], receipt=False))
        self.assertIsNone(self.choose([row()], ancestor=lambda *_: False))
        self.assertIsNone(self.choose([row()], current="512"))
        self.assertIsNone(self.choose([row(sha=HEAD)]))

    def test_foreign_and_wrong_workflow_rejected(self):
        foreign = row()
        foreign["head_repository"] = {"full_name": "other/repo"}
        wrong = row()
        wrong["path"] = ".github/workflows/ci.yml"
        for item in (foreign, wrong):
            self.assertIsNone(self.choose([item]))

    def test_receipt_expired_empty_or_wrong_sha_rejected(self):
        a = artifacts()["artifacts"][0]
        for mutation in ({"expired": True}, {"size_in_bytes": 0},
                         {"name": "wrong"}):
            sample = {**a, **mutation}
            self.assertFalse(m.receipt_present({"artifacts": [sample]}, 512, OLD))

    def test_newer_green_skipped_run_does_not_hide_old_real_30k(self):
        newer = row(sha="c" * 40)
        newer["id"] = 513
        newer["created_at"] = "2026-11-07T09:00:00Z"
        self.assertEqual(self.choose([newer, row()])["BaselineSha256"], OLD)

    def test_missing_history_must_never_skip(self):
        self.assertIsNone(self.choose([]))
        self.assertIsNone(self.choose(None))
        self.assertFalse(m.physical_passed(None))
        self.assertFalse(m.receipt_present(None, 512, OLD))

    def test_workflow_uses_cumulative_baseline_not_before_sha(self):
        root = Path(__file__).resolve().parents[3]
        wf = (root / ".github/workflows/v9-physical-seed42-probe.yml").read_text()
        self.assertIn("v9-physical-coverage-baseline.py", wf)
        self.assertIn("--no-validated-baseline", wf)
        self.assertIn("git merge-base --is-ancestor", wf)
        self.assertNotIn("github.event.before", wf)


if __name__ == "__main__":
    unittest.main()
