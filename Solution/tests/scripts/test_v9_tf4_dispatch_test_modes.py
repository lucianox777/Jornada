"""Legacy launcher must reject the revoked database mode before any TRAIN."""
from pathlib import Path
import unittest

WORKFLOWS = Path(__file__).resolve().parents[3] / ".github/workflows"
LAUNCHER = WORKFLOWS / "v9-tf4-execucao-testes.yml"
LEGACY = WORKFLOWS / "v9-v3-four-name-100k.yml"


class V9Tf4ManualLauncherTests(unittest.TestCase):
    def test_manual_admission_preserves_owner_sha_and_preflight(self):
        workflow = LAUNCHER.read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("pull_request:", workflow)
        for guard in ("V9_OWNER_ONLY_DISPATCH", "V9_ONE_USE_ATTEMPT_REQUIRED",
                      "V9_EXPECTED_MASTER_SHA_MISMATCH", "V9_SECURITY_PREFLIGHT_REQUIRED"):
            self.assertIn(guard, workflow)

    def test_legacy_external_selection_fails_before_reusable_workflow(self):
        workflow = LAUNCHER.read_text(encoding="utf-8")
        admission, execution = workflow.split("\n  calibrate-audit-validate:\n", 1)
        self.assertIn('if [[ "$TEST_MODE" != "sem_test" || -n "$TEST_DATABASE" ]]', admission)
        self.assertIn("V9_LEGACY_EXTERNAL_TEST_ROUTE_DISABLED_PREPARE_ONLY", admission)
        self.assertIn("exit 1", admission)
        self.assertIn("needs.admission.result == 'success'", execution)
        self.assertIn("uses: ./.github/workflows/v9-v3-four-name-100k.yml", execution)
        self.assertIn("test_required: false", execution)
        self.assertIn('test_database: ""', execution)
        self.assertNotIn("secrets: inherit", execution)
        self.assertNotIn("--v9-calibrate", workflow)

    def test_preserves_legacy_sealed_test_guards(self):
        workflow = LEGACY.read_text(encoding="utf-8")
        self.assertIn("workflow_call:", workflow)
        self.assertIn("V9_TF4_TEST_PRIVATE_KEY_B64:", workflow)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", workflow)
        self.assertNotIn("V9_EXTERNAL_TEST_READONLY_CONNECTION", workflow)


if __name__ == "__main__":
    unittest.main()
