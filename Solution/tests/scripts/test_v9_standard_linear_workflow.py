"""Structural guard for the standalone V9 standard scientific workflow.

This test intentionally does not execute the 100k scientific campaign.
"""
from pathlib import Path
import unittest

WORKFLOW = Path(__file__).resolve().parents[2] / ".github/workflows/v9-standard-linear-100k.yml"


class StandardLinearWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")

    def test_only_scientific_test_option(self):
        text = self.workflow
        self.assertIn("test_required:", text)
        self.assertNotIn("test_mode:", text)
        self.assertNotIn("test_database:", text)
        self.assertNotIn("test-database-readonly:", text)
        self.assertNotIn("test-database-preflight:", text)

    def test_linear_mandatory_jobs(self):
        text = self.workflow
        for job in ("admission", "prepare-100k", "physical-100k",
                    "audit-100k", "validation-100k", "e2e-closeout"):
            self.assertIn(f"  {job}:\n", text)
        self.assertIn("needs: admission", text)
        self.assertIn("needs: [prepare-100k]", text)
        self.assertIn("needs: physical-100k", text)
        self.assertIn("needs: audit-100k", text)
        self.assertIn("needs: [validation-100k]", text)

    def test_train_handoff_and_separate_runner(self):
        text = self.workflow
        self.assertIn("v9-standard-train-prepared-", text)
        self.assertIn("sha256sum -c SHA256SUMS", text)
        self.assertIn("train-inputs.tar.gz", text)
        self.assertIn("test -s \"$root/people-100k/observacoes.csv\"", text)
        self.assertIn("--people 100000 --seed 42", text)

    def test_test_remains_fail_closed(self):
        text = self.workflow
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", text)
        self.assertIn('if [[ "${{ inputs.test_required }}" == "true" ]]; then', text)


if __name__ == "__main__":
    unittest.main()
