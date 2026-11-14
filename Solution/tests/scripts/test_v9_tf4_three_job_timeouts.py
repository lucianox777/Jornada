"""Fail-closed contract for three 360m stages, standalone one-model TEST."""
import re
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class ThreeJobsV9TimeoutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = WORKFLOW.read_text(encoding="utf-8")
        train, tail = cls.source.split("\n  validation-100k:\n", 1)
        validation, test = tail.split("\n  test-100k:\n", 1)
        cls.train, cls.validation, cls.test = train, validation, test

    def test_three_independent_job_clocks_up_to_360_minutes(self):
        self.assertIn("\n  physical-100k:\n", self.train)
        self.assertRegex(self.train, r"(?m)^    timeout-minutes: 360$")
        self.assertRegex(self.validation, r"(?m)^    timeout-minutes: 360$")
        self.assertRegex(self.test, r"(?m)^    timeout-minutes: 360$")
        self.assertRegex(self.train, r"(?m)^        timeout-minutes: 340$")
        # Preserve 30m admission/download/cleanup margin in the VALIDATION job.
        self.assertRegex(self.validation, r"(?m)^        timeout-minutes: 330$")
        self.assertEqual(len(re.findall(r"(?m)^    timeout-minutes: 360$", self.source)), 3)

    def test_validation_requires_audited_train_and_new_one_use_sql(self):
        self.assertIn("needs: physical-100k", self.validation)
        self.assertIn("needs.physical-100k.result == 'success'", self.validation)
        self.assertIn("needs.physical-100k.outputs.has_finalists == 'true'", self.validation)
        self.assertIn("github.actor == github.repository_owner", self.validation)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', self.validation)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', self.validation)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', self.validation)
        self.assertIn('test "${{ inputs.test_required }}" != "true" || test "${{ inputs.prepare_sealed_test }}" = "true"', self.validation)
        self.assertIn("v9-v3-four-name-100k-handoff.py verify", self.validation)
        self.assertIn("v9-100k-load-frozen-ibge.sh", self.validation)
        self.assertIn("--v9-validate-finalists", self.validation)
        self.assertIn("Remove VALIDATION disposable SQL even after failure", self.validation)

    def test_test_job_is_standalone_and_uses_one_winner_only(self):
        self.assertIn("needs: [validation-100k, prepare-test-100k]", self.test)
        self.assertIn("inputs.test_required == true", self.test)
        self.assertIn("needs.validation-100k.result == 'success'", self.test)
        self.assertIn("needs.prepare-test-100k.result == 'success'", self.test)
        self.assertIn("github.actor == github.repository_owner", self.test)
        self.assertNotIn("if: ${{ false }}", self.test)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', self.test)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', self.test)
        self.assertIn("--v9-independent-test", self.test)
        self.assertIn("TEST_PRIVATE_KEY_B64", self.test)
        self.assertIn("V9_TEST_DATA_LOCK_SHA", self.test)
        self.assertIn("timeout-minutes: 330", self.test)
        self.assertIn("test-corpus.cms", (ROOT / "scripts/v9-tf4-sealed-test-preparation.py").read_text())
        self.assertNotIn("--v9-calibrate", self.test)
        self.assertNotIn("--v9-validate-finalists", self.test)
        self.assertNotIn("finalist-validation.json", self.test)
        self.assertNotIn("selection.json", self.test)
        self.assertNotIn("plan.json", self.test)
        self.assertNotIn("--people 100000", self.test)
        self.assertIn("TEST_NEEDS_INDEPENDENT_PRESEALED_CORPUS", self.train)
        self.assertIn("workflow_dispatch:", self.train)
        self.assertIn("github.actor == github.repository_owner", self.train)
        self.assertIn("inputs.security_preflight_approved == true", self.train)
        self.assertNotIn("\n  push:", self.source)
        self.assertNotIn("\n  schedule:", self.source)

    def test_training_hypotheses_and_science_unchanged(self):
        self.assertIn("324 V3-only four-name hypotheses", self.train)
        self.assertIn("v9-v3-four-name-100k-audit.py", self.train)
        self.assertIn('v9-100k-load-frozen-ibge.sh "$root/bound/plan.json"', self.train)
        self.assertIn("v9-tf4-validation-handoff-", self.train)
        self.assertIn("testUsed", self.validation)
        self.assertIn("v9-tf4-test-one-winner-", self.validation)
        self.assertIn("refitted", self.validation)
        self.assertIn("activated", self.validation)


if __name__ == "__main__":
    unittest.main()
