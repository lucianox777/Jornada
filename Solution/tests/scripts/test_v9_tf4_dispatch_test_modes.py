"""Contract for the new owner-only TF4 manual launcher and the canonical reusable run.

This is a static/security regression test; it does not claim a physical 100k
campaign or production RunAsync parity for scientific synthetic TEST.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
LAUNCHER = ROOT / ".github/workflows/v9-tf4-execucao-testes.yml"
CANONICAL = ROOT / ".github/workflows/v9-v3-four-name-100k.yml"


class V9Tf4ManualLauncherTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.launcher = LAUNCHER.read_text(encoding="utf-8")
        cls.canonical = CANONICAL.read_text(encoding="utf-8")

    def test_manual_only_and_executable_modes(self):
        w = self.launcher
        self.assertIn("  workflow_dispatch:", w)
        for forbidden in ("\n  push:", "\n  pull_request:", "\n  schedule:",
                          "timeout-minutes:"):
            self.assertNotIn(forbidden, w)
        self.assertIn("type: choice", w)
        self.assertIn("          - sem_test", w)
        self.assertIn("          - banco_existente_readonly", w)
        self.assertNotIn("          - teste_sintetico_cientifico", w)
        self.assertIn("TEST cientifico 100k permanece bloqueado", w)
        self.assertIn("      security_preflight_approved:", w)
        self.assertIn("      expected_sha:", w)

    def test_admission_fails_closed_before_any_physics(self):
        w = self.launcher
        admission = w.split("\n  admission:\n", 1)[1].split(
            "\n  calibrate-audit-validate:\n", 1)[0]
        for value in (
            'test "$GITHUB_EVENT_NAME" = "workflow_dispatch"',
            'test "$GITHUB_ACTOR" = "$GITHUB_REPOSITORY_OWNER"',
            'test "$GITHUB_RUN_ATTEMPT" = "1"',
            'test "$GITHUB_REF" = "refs/heads/master"',
            'test "$EXACT_SHA" = "$GITHUB_SHA"',
            'test "$APPROVED" = "true"',
            'case "$TEST_MODE" in',
            'if [[ -n "$TEST_DATABASE" ]]',
            '^[A-Za-z][A-Za-z0-9_]{0,127}$',
            'V9_UNSUPPORTED_TEST_MODE',
        ):
            self.assertIn(value, admission)
        self.assertNotIn("secrets.", admission)
        self.assertIn("needs: admission", w)

    def test_delegates_without_copying_324_scientific_jobs(self):
        w = self.launcher
        for value in (
            "uses: ./.github/workflows/v9-v3-four-name-100k.yml",
            "test_required: ${{ inputs.test_mode == 'banco_existente_readonly' }}",
            "test_database: ${{ inputs.test_mode == 'banco_existente_readonly' && inputs.test_database || '' }}",
            'expected_test_cert_sha256: ""',
            "secrets: inherit",
            "disable_python_sql_budget: ${{ inputs.disable_python_sql_budget }}",
        ):
            self.assertIn(value, w)
        for physical in ("\n  physical-100k:", "\n  audit-100k:",
                         "\n  validation-100k:", "\n  test-100k:"):
            self.assertNotIn(physical, w)

    def test_canonical_was_not_degraded_for_reuse(self):
        w = self.canonical
        self.assertIn("  workflow_dispatch:", w)
        self.assertIn("  workflow_call:", w)
        self.assertIn("V9_EXTERNAL_TEST_READONLY_CONNECTION:", w)
        self.assertIn("V9_TF4_TEST_PRIVATE_KEY_B64:", w)
        self.assertIn("github.actor == github.repository_owner", w)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", w)
        self.assertIn("Run physical V9 CSharp on 324 V3-only", w)
        self.assertIn("needs: physical-100k", w)
        self.assertIn("needs: audit-100k", w)
        self.assertIn("needs.validation-100k.result == 'success'", w)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', w)
        self.assertNotIn("timeout-minutes:", w)


if __name__ == "__main__":
    unittest.main()
