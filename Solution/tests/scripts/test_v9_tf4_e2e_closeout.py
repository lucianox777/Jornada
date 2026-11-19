#!/usr/bin/env python3
"""Fail closed E2E status/test science/operational file compatibility; no 100k or TEST keys."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WF = ROOT.parent / ".github/workflows/v9-standard-linear-100k.yml"
SCRIPT = ROOT / "scripts/v9-tf4-e2e-closeout.py"
spec = importlib.util.spec_from_file_location("tf4_e2e_closeout", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
W = "a" * 64
E = "b" * 64
SHA = "c" * 40


def validation_doc(winner=W):
    return {
        "status": ("VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED"
                   if winner else "NO_FINALIST_PASSES_DECLARED_SCIENTIFIC_GATES"),
        "selectedValidationSnapshotSha256": winner,
        "selectedValidationOperationalModelFileName": winner + ".operational.json" if winner else None,
        "selectedValidationOperationalModelContract": "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1" if winner else None,
        "selectionEnvelopeSha256": E,
        "validationResultSha256": "d" * 64,
        "schema": "V9_VALIDATION_E2E_STATUS_V1",
        "comparedConfigurations": 1,
        "measuredCount": 1,
        "refitted": False, "testUsed": False, "certified": False,
        "activated": False,
    }


def test_doc(full_parity=False):
    return {
        "schema": "V9_TF4_INDEPENDENT_TEST_ONE_WINNER_PHYSICAL_V1",
        "winnerSnapshotSha256": W,
        "validatedOperationalModelFileName": W + ".operational.json",
        "validatedOperationalJsonSchema": "V9_FITTED_SNAPSHOT_V3",
        "sameOperationalJsonBytesAsProductionSideSqlPayload": True,
        "operationalModelContract": "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1",
        "operationalModelByteExactRoundTrip": True,
        "operationalModelActive": False,
        "operationalModelPublished": False,
        "productionRunnerFullPipelineParityCertified": full_parity,
        "trainPlanRead": False,
        "validationResultsRead": False,
        "refitted": False, "certified": False, "activated": False,
        "testUsed": True, "configurationsEvaluated": 1,
    }


class V9Tf4E2ECloseoutTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.val = self.root / "validation-e2e-status.json"
        self.test = self.root / "independent-test.json"
        self.val.write_text(json.dumps(validation_doc()), encoding="utf-8")

    def call(self, requested=False, prep="skipped", test="skipped"):
        return mod.closeout("success", "success", "success", prep, test,
                            requested, self.val, self.test if requested else None)

    def test_actual_manual_workflow_has_always_e2e_closeout_and_no_timeouts(self):
        source = WF.read_text(encoding="utf-8")
        self.assertIn("
  e2e-closeout:
", source)
        block = source.split("
  e2e-closeout:
", 1)[1]
        self.assertIn("needs: [prepare-100k, physical-100k, audit-100k, validation-100k, test-100k]", block)
        self.assertIn("if: ${{ always() }}", block)
        self.assertIn("v9-tf4-e2e-closeout.py", block)
        self.assertIn("validation-e2e-status.json", block)
        self.assertIn("independent-test.json", block)
        self.assertIn("github.repository_owner", source)
        self.assertNotIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", source)
        self.assertNotIn("timeout-minutes:", source)
        self.assertNotIn("TEST_PRIVATE_KEY_B64", block)
        self.assertNotIn("test-corpus.cms", block)

    def test_no_winner_skips_test_but_winner_requires_test(self):
        with self.assertRaisesRegex(ValueError, "TEST_MANDATORY_IF_AND_ONLY_IF_VALIDATION_HAS_WINNER"):
            self.call()
        self.val.write_text(json.dumps(validation_doc(None)), encoding="utf-8")
        result = self.call(prep="success")
        self.assertEqual(result["status"],
                         "E2E_PHYSICAL_CORE_FINISHED_NO_WINNER_TEST_NOT_REQUESTED")
        self.assertFalse(result["certified"])
        self.assertFalse(result["productionPublication"])
        self.assertIsNone(result["winnerSha256"])

    def test_missing_or_skipped_mandatory_stage_cannot_be_success(self):
        for fields, reason in [
            (("skipped", "success", "success", "skipped", "skipped", False),
             "TRAIN_NOT_SUCCESS"),
            (("success", "failure", "success", "skipped", "skipped", False),
             "AUDIT_NOT_SUCCESS"),
            (("success", "success", "skipped", "skipped", "skipped", False),
             "VALIDATION_NOT_SUCCESS"),
            (("success", "success", "success", "failure", "skipped", False),
             "UNREQUESTED_TEST_PREPARATION"),
            (("success", "success", "success", "skipped", "failure", False),
             "UNREQUESTED_TEST_EXPOSURE"),
        ]:
            with self.subTest(fields=fields):
                with self.assertRaisesRegex(ValueError, reason):
                    mod.validate_jobs(*fields)

    def test_invalid_validation_receipts_are_denied(self):
        for field, value in [
            ("refitted", True),
            ("testUsed", True),
            ("certified", True),
            ("activated", True),
            ("comparedConfigurations", 4),
            ("selectionEnvelopeSha256", "bad"),
            ("validationResultSha256", "bad"),
            ("selectedValidationSnapshotSha256", "bad"),
            ("selectedValidationOperationalModelFileName", "wrong.json"),
        ]:
            with self.subTest(field=field):
                r = validation_doc()
                r[field] = value
                self.val.write_text(json.dumps(r), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "VALIDATION_"):
                    self.call(requested=True, prep="success", test="success")
        self.val.write_text(json.dumps(validation_doc(None)), encoding="utf-8")
        result = self.call(prep="success")
        self.assertEqual(result["status"],
                         "E2E_PHYSICAL_CORE_FINISHED_NO_WINNER_TEST_NOT_REQUESTED")
        with self.assertRaisesRegex(ValueError, "TEST_"):
            self.test.write_text(json.dumps(test_doc(full_parity=False)))
            self.call(requested=True, prep="success", test="success")

    def test_test_requires_same_sha_canonical_model_and_independent_execution(self):
        self.test.write_text(json.dumps(test_doc()), encoding="utf-8")
        result = self.call(requested=True, prep="success", test="success")
        self.assertEqual(result["status"],
                         "E2E_VALIDATION_WINNER_PRESERVED_TEST_REPORTED")
        self.assertEqual(result["independentTestReportStatus"], "VERIFIED")
        self.assertEqual(result["independentTestScientificAssessment"], "MEASURED_NOT_ADJUDICATED")
        self.assertEqual(result["productionRequirementsStatus"], "NOT_DEMONSTRATED")
        self.assertEqual(result["productionAuthorization"], "OPERATOR_MASTER_ONLY_NOT_GRANTED")
        for field, value, message in [
            ("winnerSnapshotSha256", E, "TEST_WINNER_CONTRACT_DRIFT"),
            ("operationalModelByteExactRoundTrip", False, "TEST_MODEL_"),
            ("trainPlanRead", True, "TEST_INDEPENDENCE_"),
            ("operationalModelActive", True, "TEST_MODEL_"),
            ("configurationsEvaluated", 2, "TEST_INDEPENDENCE_"),
        ]:
            with self.subTest(field=field):
                r = test_doc(full_parity=False)
                r[field] = value
                self.test.write_text(json.dumps(r), encoding="utf-8")
                result = self.call(requested=True, prep="success", test="success")
                self.assertEqual(result["independentTestReportStatus"], "INVALID_RECEIPT")
                self.assertEqual(result["winnerSha256"], W)
        self.test.write_text(json.dumps(test_doc(full_parity=False)), encoding="utf-8")
        result = self.call(requested=True, prep="success", test="success")
        self.assertEqual(result["status"],
                         "E2E_VALIDATION_WINNER_PRESERVED_TEST_REPORTED")
        self.assertFalse(result["certified"])
        invalid = test_doc(full_parity=True)
        self.test.write_text(json.dumps(invalid), encoding="utf-8")
        result = self.call(requested=True, prep="success", test="success")
        self.assertEqual(result["independentTestReportStatus"], "INVALID_RECEIPT")

    def test_cli_failure_emits_sanitized_receipt_even_after_missing_validation(self):
        out = self.root / "receipt.json"
        proc = subprocess.run(
            [sys.executable, str(SCRIPT),
             "--physical", "success", "--audit", "failure",
             "--validation", "skipped", "--preparation", "skipped",
             "--test", "skipped", "--test-requested", "false",
             "--validation-file", str(self.val), "--code-sha", SHA,
             "--run-id", "100", "--out", str(out)],
            capture_output=True, text=True, check=False,
        )
        self.assertNotEqual(proc.returncode, 0)
        data = json.loads(out.read_text(encoding="utf-8"))
        self.assertEqual(data["status"], "E2E_INCOMPLETE_FAIL_CLOSED")
        self.assertIn("AUDIT_NOT_SUCCESS", data["reason"])
        self.assertNotIn("measured", data)


if __name__ == "__main__":
    unittest.main()
