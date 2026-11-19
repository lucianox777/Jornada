"""Regression: independent TEST cannot veto the scientific VALIDATION winner."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import json

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf4-e2e-closeout.py"
spec = importlib.util.spec_from_file_location("v9_closeout", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class IndependentTestReportOnly(unittest.TestCase):
    def test_test_failure_does_not_revoke_winner(self):
        sha = "a" * 64
        with tempfile.TemporaryDirectory() as tmp:
            receipt = Path(tmp) / "validation.json"
            receipt.write_text(json.dumps({
                "schema": "V9_VALIDATION_E2E_STATUS_V1",
                "testUsed": False, "refitted": False, "certified": False, "activated": False,
                "selectionEnvelopeSha256": "b"*64, "validationResultSha256": "c"*64,
                "comparedConfigurations": 1, "measuredCount": 1,
                "status": "VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED",
                "selectedValidationSnapshotSha256": sha,
                "selectedValidationOperationalModelFileName": sha + ".operational.json",
                "selectedValidationOperationalModelContract": "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1"
            }))
            for test_state in ("failure", "cancelled", "skipped"):
                with self.subTest(test_state=test_state):
                    result = module.closeout("success", "success", "success",
                        "success", test_state, True, receipt, None)
                    self.assertEqual(result["winnerSha256"], sha)
                    self.assertEqual(result["independentTestReportStatus"], "INCOMPLETE")
                    self.assertFalse(result["activated"])
                    self.assertEqual(result["independentTestScientificAssessment"], "INCONCLUSIVE")
                    self.assertEqual(result["productionRequirementsStatus"], "NOT_DEMONSTRATED")
                    self.assertEqual(result["productionAuthorization"], "OPERATOR_MASTER_ONLY_NOT_GRANTED")
            result = module.closeout("success", "success", "success",
                "success", "success", True, receipt, None)
            self.assertEqual(result["independentTestReportStatus"], "INCOMPLETE")

if __name__ == "__main__":
    unittest.main()
