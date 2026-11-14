#!/usr/bin/env python3
"""Fast integration contract: mandatory TF4 TRAIN->AUDIT->VALIDATION, TEST optional.

A real small GitHub workflow separately proves scheduling and artifact travel.
No 100k, no SQL, no independent TEST data or secrets are ever opened here.
"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
SMOKE_WF = ROOT.parent / ".github/workflows/v9-tf4-chain-integration-smoke.yml"
SCRIPT = ROOT / "scripts/v9-tf4-chain-integration-smoke.py"
SHA = "a" * 40
RUN_ID = 43600000001

spec = importlib.util.spec_from_file_location("v9_tf4_chain_smoke", SCRIPT)
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


class MandatoryChainIntegrationContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = WF.read_text(encoding="utf-8")
        first, tail = cls.source.split("\n  physical-100k:\n", 1)
        train, tail = tail.split("\n  audit-100k:\n", 1)
        audit, tail = tail.split("\n  validation-100k:\n", 1)
        validation, test = tail.split("\n  test-100k:\n", 1)
        test = test.split("\n  e2e-closeout:\n", 1)[0]
        cls.prepare, cls.train, cls.audit, cls.validation, cls.test = (
            first, train, audit, validation, test,
        )

    def test_mandatory_chain_never_has_optional_test_gate(self):
        self.assertIn("needs: prepare-test-100k", self.train)
        self.assertIn("always()", self.train)
        self.assertIn("inputs.test_required != true", self.train)
        self.assertIn("needs: physical-100k", self.audit)
        self.assertIn("always() && !cancelled()", self.audit)
        self.assertIn("needs.physical-100k.result == 'success'", self.audit)
        self.assertIn("needs: audit-100k", self.validation)
        self.assertIn("always() && !cancelled()", self.validation)
        self.assertIn("needs.audit-100k.result == 'success'", self.validation)
        self.assertIn("needs.audit-100k.outputs.has_finalists == 'true'",
                      self.validation)
        self.assertIn("v9-tf4-calibration-preaudit-", self.train)
        self.assertIn("v9-tf4-calibration-preaudit-", self.audit)
        self.assertIn("v9-tf4-validation-handoff-", self.audit)
        self.assertIn("v9-tf4-validation-handoff-", self.validation)
        self.assertIn("V9_MANDATORY_VALIDATION_NO_FINALISTS", self.audit)
        self.assertIn("steps.finalists.outputs.has_finalists != 'true'", self.audit)
        for job in (self.train, self.audit, self.validation):
            header = job.split("\n    steps:", 1)[0]
            self.assertNotIn("if: ${{ inputs.test_required == true", header)
        self.assertNotIn("timeout-minutes:", self.source)

    def test_independent_test_is_optional_but_unconditionally_blocked_until_parity(self):
        self.assertIn("needs: [validation-100k, prepare-test-100k]", self.test)
        self.assertIn("inputs.test_required == true", self.test)
        self.assertNotIn("prepare_sealed_test", self.source)
        self.assertIn("needs.validation-100k.result == 'success'", self.test)
        self.assertIn("needs.prepare-test-100k.result == 'success'", self.test)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",
                      self.prepare)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",
                      self.train)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', self.test)
        self.assertIn("refs/tags/v9-tf4-test-seed-", self.test)
        self.assertIn("--v9-independent-test", self.test)
        self.assertIn("github.actor == github.repository_owner", self.test)
        self.assertNotIn("TEST_PRIVATE_KEY_B64", self.train + self.audit
                         + self.validation)
        self.assertNotIn("JORNADA_V9_TEST_CORPUS_DIR", self.train
                         + self.audit + self.validation)

    def test_cross_job_smoke_uses_skippable_mock_test_and_no_science_or_secrets(self):
        content = SMOKE_WF.read_text(encoding="utf-8")
        self.assertIn("pull_request:", content)
        self.assertIn("prepare-test-optional:", content)
        self.assertIn("train-fixture:", content)
        self.assertIn("audit-fixture:", content)
        self.assertIn("validation-fixture:", content)
        self.assertIn("test-fixture-optional:", content)
        self.assertIn("needs.prepare-test-optional.result == 'success'", content)
        self.assertIn("needs.train-fixture.result == 'success'", content)
        self.assertIn("needs.audit-fixture.result == 'success'", content)
        self.assertIn("needs.validation-fixture.result == 'success'", content)
        self.assertIn("always() && !cancelled()", content)
        self.assertIn("actions/upload-artifact@", content)
        self.assertIn("actions/download-artifact@", content)
        self.assertIn("synthetic_test_requested == true", content)
        self.assertNotIn("dotnet run", content)
        self.assertNotIn("test_required", content)
        self.assertNotIn("${{ secrets.", content)
        self.assertNotIn("timeout-minutes:", content)

    def test_synthetic_train_audit_validation_roundtrip_and_rejected_corruption(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            source = base / "source"
            handoff = base / "preaudit"
            audit = base / "audit"
            smoke.train(source, handoff, SHA, RUN_ID)
            smoke.audit(handoff, audit, SHA, RUN_ID)
            smoke.validation(handoff, audit, SHA, RUN_ID)
            with self.assertRaisesRegex(ValueError, "immutable run|manifest mismatch"):
                smoke.validation(handoff, audit, SHA, RUN_ID + 1)
            forged = json.loads((audit / smoke.AUDIT_FILENAME).read_text())
            forged["scientificAuditExecuted"] = True
            (audit / smoke.AUDIT_FILENAME).write_text(json.dumps(forged))
            with self.assertRaisesRegex(ValueError, "AUDIT artifact identity"):
                smoke.validation(handoff, audit, SHA, RUN_ID)

    def test_synthetic_smoke_rejects_tampered_preaudit_and_extra_members(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            source, handoff, audit = base / "source", base / "handoff", base / "audit"
            smoke.train(source, handoff, SHA, RUN_ID)
            (handoff / "observacoes.csv").write_text("leak")
            with self.assertRaisesRegex(ValueError, "unexpected or absent"):
                smoke.audit(handoff, audit, SHA, RUN_ID)
            (handoff / "observacoes.csv").unlink()
            (handoff / "runtime-diagnostics.json").write_text('{"fake":true}')
            with self.assertRaisesRegex(ValueError, "manifest mismatch"):
                smoke.audit(handoff, audit, SHA, RUN_ID)


if __name__ == "__main__":
    unittest.main()
