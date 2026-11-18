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
        self.assertNotIn("test_required:", text)
        self.assertNotIn("test_mode:", text)
        self.assertIn("test_database:", text)
        self.assertIn("V9_PREPARE_EXTERNAL_STATISTICAL_ADAPTER_NOT_IMPLEMENTED", text)
        self.assertIn("V9_PREPARE_STATISTICAL_REFERENCE=IBGE_DEFAULT", text)
        self.assertNotIn("test-database-readonly:", text)
        self.assertNotIn("test-database-preflight:", text)

    def test_external_statistical_reference_fails_closed_before_generation(self):
        prepare = self.workflow.split("  prepare-100k:\n", 1)[1].split("  physical-100k:\n", 1)[0]
        self.assertIn("TEST_DATABASE: ${{ inputs.test_database }}", prepare)
        self.assertIn("V9_PREPARE_EXTERNAL_REFERENCE_INVALID_DATABASE_NAME", prepare)
        self.assertLess(prepare.index("Admit statistical source selection"),
                        prepare.index("Build unchanged synthetic corpus generator"))
        self.assertLess(prepare.index("V9_PREPARE_EXTERNAL_STATISTICAL_ADAPTER_NOT_IMPLEMENTED"),
                        prepare.index("Generate known-seed 100k clean raw TRAIN corpus"))

    def test_linear_mandatory_jobs(self):
        text = self.workflow
        for job in ("admission", "prepare-100k", "physical-100k",
                    "audit-100k", "validation-100k", "e2e-closeout"):
            self.assertIn(f"  {job}:\n", text)
        self.assertIn("needs: admission", text)
        self.assertIn("needs: prepare-100k", text)
        self.assertIn("needs: physical-100k", text)
        self.assertIn("needs: audit-100k", text)
        self.assertIn("needs: validation-100k", text)

    def test_train_handoff_and_separate_runner(self):
        text = self.workflow
        self.assertIn("v9-standard-train-prepared-", text)
        self.assertIn("sha256sum -c SHA256SUMS", text)
        self.assertIn("train-inputs.tar.gz", text)
        self.assertIn("test -s \"$root/people-100k/observacoes.csv\"", text)
        self.assertIn("--calibrator-config config/calibration/v9-synthetic-sampling.json --seed 42", text)

    def test_prepare_creates_generation_log_parent_before_redirect(self):
        prepare = self.workflow.split("  prepare-100k:\n", 1)[1].split("  physical-100k:\n", 1)[0]
        generation = prepare.split("      - name: Generate known-seed 100k clean raw TRAIN corpus\n", 1)[1].split("      - name: ", 1)[0]
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

    def test_plan_binding_occurs_in_train_after_verified_handoff(self):
        prepare = self.workflow.split("  prepare-100k:\n", 1)[1].split("  physical-100k:\n", 1)[0]
        train = self.workflow.split("  physical-100k:\n", 1)[1].split("  audit-100k:\n", 1)[0]
        for script in ("v9-j-download-pinned-artifact.py", "v9-scale-100k-technical-plan.py",
                       "v9-v3-four-name-100k-plan.py"):
            self.assertNotIn("python3 scripts/" + script, prepare)
            self.assertIn("python3 scripts/" + script, train)
        self.assertLess(train.index("v9-standard-prepare-handoff.py unpack"),
                        train.index("python3 scripts/v9-scale-100k-technical-plan.py"))
        self.assertLess(train.index("python3 scripts/v9-v3-four-name-100k-plan.py"),
                        train.index("Start masked disposable SQL Server"))
        self.assertIn("--calibrator-config config/calibration/v9-synthetic-sampling.json --seed 42", prepare)

    def test_audit_validation_jobs_are_identical_to_legacy(self):
        legacy = (WORKFLOW.parent / "v9-v3-four-name-100k.yml").read_text(encoding="utf-8")
        def extract(text, start, end):
            first = text.index("  " + start + ":\n")
            last = text.index("  " + end + ":\n", first)
            return text[first:last]
        legacy_audit = extract(legacy, "audit-100k", "validation-100k")
        standard_audit = extract(self.workflow, "audit-100k", "validation-100k")
        # The legacy comment describes an optional TEST; the new workflow
        # deliberately supersedes that wording without changing AUDIT behavior.
        legacy_audit = legacy_audit.replace(
            "# Skip of optional TEST preparation must never suppress a successful audit.",
            "# TEST preparation is mandatory before TRAIN; audit requires successful TRAIN.")
        # PO-approved standard linear terminal outcome: no eligible finalists
        # is successful, unlike the legacy workflow.
        self.assertIn("V9_MANDATORY_VALIDATION_NO_FINALISTS", legacy_audit)
        self.assertIn("V9_AUDIT_COMPLETED_WITHOUT_ELIGIBLE_FINALISTS", standard_audit)
        self.assertNotIn("V9_MANDATORY_VALIDATION_NO_FINALISTS", standard_audit)
        legacy_prefix = legacy_audit.split("      - name: Fail incomplete campaign", 1)[0]
        standard_prefix = standard_audit.split("      - name: Record valid scientific terminal outcome", 1)[0]
        self.assertEqual(legacy_prefix, standard_prefix)
        validation = extract(self.workflow, "validation-100k", "test-100k")
        self.assertIn("has_winner: ${{ steps.operational_file.outputs.has_model }}", validation)
        self.assertIn("needs: audit-100k", validation)
        self.assertIn("selectedValidationOperationalModelFileName", validation)

    def test_legacy_workflow_remains_present(self):
        self.assertTrue((WORKFLOW.parent / "v9-v3-four-name-100k.yml").is_file())

    def test_test_mandatory_on_validated_winner(self):
        text = self.workflow
        self.assertNotIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", text)
        self.assertNotIn("inputs.test_required", text)
        self.assertIn("needs.validation-100k.outputs.has_winner == 'true'", text)
        self.assertIn("V9_VALIDATION_WINNER_OUTPUT_MISSING", text)


if __name__ == "__main__":
    unittest.main()
