"""Fail-closed contract for separate physical TRAIN, AUDIT, VALIDATION and TEST.

GitHub Actions owns the native job deadline. No YAML step or job deadlines.
"""
import re
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class ThreeJobsV9TimeoutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = WORKFLOW.read_text(encoding="utf-8")
        cls.preparation, tail = cls.source.split("\n  physical-100k:\n", 1)
        cls.train, tail = tail.split("\n  audit-100k:\n", 1)
        cls.audit, tail = tail.split("\n  validation-100k:\n", 1)
        cls.validation, cls.test = tail.split("\n  test-100k:\n", 1)

    def test_native_github_timeouts_only(self):
        self.assertNotIn("timeout-minutes:", self.source)
        self.assertIn("Run physical V9 CSharp on 324", self.train)
        self.assertIn("Independently audit all 324", self.audit)
        self.assertIn("Mandatory fresh independent VALIDATION", self.validation)
        self.assertIn("Apply one frozen winner to TEST", self.test)

    def test_audit_independent_job_has_verified_immutable_handoff(self):
        self.assertIn("needs: physical-100k", self.audit)
        self.assertIn("always() && !cancelled()", self.audit)
        self.assertIn("needs.physical-100k.result == 'success'", self.audit)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', self.audit)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', self.audit)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', self.audit)
        self.assertIn("v9-v3-four-name-100k-preaudit.py verify", self.audit)
        self.assertIn("v9-v3-four-name-100k-audit.py", self.audit)
        self.assertIn("v9-v3-four-name-100k-handoff.py prepare", self.audit)
        self.assertIn("has_finalists: ${{ steps.finalists.outputs.has_finalists }}".replace("\\$", "$"), self.audit)
        self.assertIn("v9-tf4-calibration-preaudit-", self.train)
        self.assertIn("v9-tf4-calibration-preaudit-", self.audit)
        self.assertNotIn("v9-v3-four-name-100k-audit.py", self.train)
        self.assertNotIn("--v9-calibrate", self.audit)
        self.assertNotIn("observacoes.csv", self.audit)

    def test_validation_requires_audited_train_and_new_one_use_sql(self):
        self.assertIn("needs: audit-100k", self.validation)
        self.assertIn("needs.audit-100k.result == 'success'", self.validation)
        self.assertIn("needs.audit-100k.outputs.has_finalists == 'true'", self.validation)
        self.assertIn("github.actor == github.repository_owner", self.validation)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', self.validation)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', self.validation)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', self.validation)
        self.assertIn('test "${{ inputs.test_required }}" != "true" || test "${{ inputs.prepare_sealed_test }}" = "true"'.replace("\\$", "$"), self.validation)
        self.assertIn("v9-v3-four-name-100k-handoff.py verify", self.validation)
        self.assertIn("v9-100k-load-frozen-ibge.sh", self.validation)
        self.assertIn("--v9-validate-finalists", self.validation)
        self.assertIn("Remove VALIDATION disposable SQL even after failure", self.validation)

    def test_optional_test_preparation_skip_does_not_suppress_validation(self):
        header = self.validation.split("\n    steps:", 1)[0]
        match = re.search(r"(?m)^    if: (.*)$", header)
        self.assertIsNotNone(match)
        condition = match.group(1)
        self.assertIn("always()", condition)
        self.assertIn("!cancelled()", condition)
        self.assertIn("needs.audit-100k.result == 'success'", condition)
        self.assertIn("needs.audit-100k.outputs.has_finalists == 'true'", condition)
        self.assertIn("inputs.security_preflight_approved == true", condition)
        self.assertIn("github.actor == github.repository_owner", condition)
        self.assertNotIn("needs.prepare-test-100k", condition)
        self.assertIn("needs.prepare-test-100k.result == 'success'", self.test)
        self.assertIn("inputs.test_required == true", self.test)

    def test_test_job_is_standalone_and_uses_one_winner_only(self):
        self.assertIn("needs: [validation-100k, prepare-test-100k]", self.test)
        self.assertIn("inputs.test_required == true", self.test)
        self.assertIn("needs.validation-100k.result == 'success'", self.test)
        self.assertIn("needs.prepare-test-100k.result == 'success'", self.test)
        self.assertIn("github.actor == github.repository_owner", self.test)
        self.assertNotIn("if: ${{ false }}".replace("\\$", "$"), self.test)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', self.test)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', self.test)
        self.assertIn("--v9-independent-test", self.test)
        self.assertIn("TEST_PRIVATE_KEY_B64", self.test)
        self.assertIn("V9_TEST_DATA_LOCK_SHA", self.test)
        self.assertIn("test-corpus.cms", (ROOT / "scripts/v9-tf4-sealed-test-preparation.py").read_text())
        self.assertNotIn("--v9-calibrate", self.test)
        self.assertNotIn("--v9-validate-finalists", self.test)
        self.assertNotIn("finalist-validation.json", self.test)
        self.assertNotIn("selection.json", self.test)
        self.assertNotIn("plan.json", self.test)
        self.assertNotIn("--people 100000", self.test)
        self.assertIn("TEST_NEEDS_INDEPENDENT_PRESEALED_CORPUS", self.train)
        self.assertIn("workflow_dispatch:", self.preparation)
        self.assertIn("github.actor == github.repository_owner", self.train)
        self.assertNotIn("\n  push:", self.source)
        self.assertNotIn("\n  schedule:", self.source)

    def test_training_hypotheses_and_science_unchanged(self):
        self.assertIn("324 V3-only four-name hypotheses", self.train)
        self.assertIn("v9-v3-four-name-100k-audit.py", self.audit)
        self.assertIn('v9-100k-load-frozen-ibge.sh "$root/bound/plan.json"', self.train)
        self.assertIn("v9-tf4-validation-handoff-", self.audit)
        self.assertIn("testUsed", self.validation)
        self.assertIn("v9-tf4-test-one-winner-", self.validation)
        self.assertIn("refitted", self.validation)
        self.assertIn("activated", self.validation)


if __name__ == "__main__":
    unittest.main()
