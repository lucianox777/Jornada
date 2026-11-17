"""Structural guard for the standalone V9 standard scientific workflow.

This test intentionally does not execute the 100k scientific campaign.
"""
from pathlib import Path
import unittest

WORKFLOW = Path(__file__).resolve().parents[3] / ".github/workflows/v9-standard-linear-100k.yml"


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

    def test_prepare_creates_generation_log_parent_before_redirect(self):
        prepare = self.workflow.split("  prepare-100k:\n", 1)[1].split("  physical-100k:\n", 1)[0]
        generation = prepare.split("      - name: Generate known-seed 100k clean TRAIN and bind new V3-only TF4 plan\n", 1)[1].split("      - name: ", 1)[0]
        self.assertIn('mkdir -p "$root"', generation)
        self.assertLess(generation.index('mkdir -p "$root"'), generation.index('> "$root/generation.log"'))

    def test_prepare_evidence_retained_with_closeout_without_duplicate_corpus(self):
        prepare = self.workflow.split("  prepare-100k:\n", 1)[1].split("  physical-100k:\n", 1)[0]
        closeout = self.workflow.split("  e2e-closeout:\n", 1)[1]
        self.assertIn("name: v9-standard-train-prepared-", prepare)
        self.assertIn("retention-days: 30", prepare)
        self.assertIn("prepare-evidence-reference.json", closeout)
        self.assertIn("SHA256SUMS", closeout)
        self.assertIn("retentionDays", closeout)
        self.assertIn("no duplicate corpus stored in closeout", closeout)
        self.assertNotIn("train-inputs.tar.gz\n          path:", closeout)

    def test_only_train_executes_physical_hypotheses_after_handoff(self):
        text = self.workflow
        prepare = text.split("  prepare-100k:\n", 1)[1].split("  physical-100k:\n", 1)[0]
        train = text.split("  physical-100k:\n", 1)[1].split("  audit-100k:\n", 1)[0]
        self.assertIn("v9-standard-prepare-handoff.py pack", prepare)
        self.assertIn("actions/upload-artifact@", prepare)
        self.assertNotIn("--v9-calibrate", prepare)
        self.assertNotIn("Start masked disposable SQL Server", prepare)
        self.assertNotIn("v9-100k-physical-memory-evidence.py", prepare)
        self.assertIn("actions/download-artifact@", train)
        self.assertIn("sha256sum -c SHA256SUMS", train)
        self.assertIn("v9-standard-prepare-handoff.py unpack", train)
        self.assertIn("--v9-calibrate", train)
        self.assertLess(train.index("sha256sum -c SHA256SUMS"), train.index("Start masked disposable SQL Server"))
        self.assertLess(train.index("v9-standard-prepare-handoff.py unpack"), train.index("--v9-calibrate"))

    def test_scientific_generation_is_byte_identical_to_legacy(self):
        legacy = (WORKFLOW.parent / "v9-v3-four-name-100k.yml").read_text(encoding="utf-8")
        marker = "      - name: Generate known-seed 100k clean TRAIN and bind new V3-only TF4 plan"
        ending = "      - name: "
        def extract(text):
            start = text.index(marker)
            end = text.index(ending, start + len(marker))
            return text[start:end]
        # The only intentional orchestration difference is creating the log directory.
        standard = extract(self.workflow).replace('          mkdir -p "$root"\n', '')
        self.assertEqual(extract(legacy), standard)

    def test_audit_validation_jobs_are_identical_to_legacy(self):
        legacy = (WORKFLOW.parent / "v9-v3-four-name-100k.yml").read_text(encoding="utf-8")
        def extract(text, start, end):
            first = text.index("  " + start + ":\n")
            last = text.index("  " + end + ":\n", first)
            return text[first:last]
        self.assertEqual(
            extract(legacy, "audit-100k", "validation-100k"),
            extract(self.workflow, "audit-100k", "validation-100k"))
        self.assertEqual(
            extract(legacy, "validation-100k", "test-100k"),
            extract(self.workflow, "validation-100k", "test-100k"))

    def test_legacy_workflow_remains_present(self):
        self.assertTrue((WORKFLOW.parent / "v9-v3-four-name-100k.yml").is_file())

    def test_test_remains_fail_closed(self):
        text = self.workflow
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", text)
        self.assertIn('if [[ "${{ inputs.test_required }}" == "true" ]]; then', text)


if __name__ == "__main__":
    unittest.main()
