"""Fail-closed contract for V9 TF4 3-job/360m architecture: TEST stays sealed."""
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
        validation, test = tail.split("\n  test-100k-locked:\n", 1)
        cls.train, cls.validation, cls.test = train, validation, test

    def test_three_independent_job_clocks_up_to_360_minutes(self):
        self.assertIn("\n  physical-100k:\n", self.train)
        self.assertRegex(self.train, r"(?m)^    timeout-minutes: 360$")
        self.assertRegex(self.validation, r"(?m)^    timeout-minutes: 360$")
        self.assertRegex(self.test, r"(?m)^    timeout-minutes: 360$")
        self.assertRegex(self.train, r"(?m)^        timeout-minutes: 315$")
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
        self.assertIn('test "${{ inputs.test_required }}" = "false"', self.validation)
        self.assertIn("v9-v3-four-name-100k-handoff.py verify", self.validation)
        self.assertIn("v9-100k-load-frozen-ibge.sh", self.validation)
        self.assertIn("--v9-validate-finalists", self.validation)
        self.assertIn("Remove VALIDATION disposable SQL even after failure", self.validation)

    def test_test_job_is_structural_only_and_permanently_disabled(self):
        self.assertIn("needs: validation-100k", self.test)
        self.assertRegex(self.test, r"(?m)^    if: \${\{ false \}\}$")
        self.assertIn('name: "V9 TEST', self.test)
        self.assertIn("V9_TEST_LOCKED:", self.test)
        self.assertIn("exit 1", self.test)
        self.assertNotIn("actions/checkout", self.test)
        self.assertNotIn("download-artifact", self.test)
        self.assertNotIn("--v9-test", self.test)
        self.assertNotIn("--people", self.test)
        self.assertNotIn("docker run", self.test)
        self.assertNotIn("TEST_DATA", self.test)
        self.assertNotIn("TEST_SEED", self.test)
        self.assertIn('if [[ "${{ inputs.test_required }}" == "true" ]]; then', self.train)
        self.assertIn("TEST_REQUIRED_UNSUPPORTED", self.train)
        self.assertIn("exit 1", self.train)
        # Only the owner can dispatch after an affirmative security preflight.
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
        self.assertIn("refitted", self.validation)
        self.assertIn("activated", self.validation)


if __name__ == "__main__":
    unittest.main()
