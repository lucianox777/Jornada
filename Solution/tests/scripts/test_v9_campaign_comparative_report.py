import importlib.util
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-campaign-comparative-report.py"
spec = importlib.util.spec_from_file_location("v9_comparative_report", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class ComparativeReportTests(unittest.TestCase):
    def test_no_winner_does_not_invent_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = SimpleNamespace(
                sha="a" * 40, run_id="42", attempt="1", population="10000",
                prepare="success", train="success", audit_result="success",
                validation_result="skipped", test_result="skipped",
                closeout=Path(tmp) / "missing.json", audit=None,
                validation=None, independent_test=None)
            report = mod.build(args)
            self.assertIsNone(report["scientificWinnerSha256"])
            self.assertEqual(report["syntheticPopulationRequested"], "10000")
            self.assertEqual(report["productionAuthorization"], "OPERATOR_MASTER_ONLY_NOT_GRANTED")
            self.assertIn("AUDIT evidence", mod.markdown(report))

    def test_winner_is_preserved_when_test_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / "receipt.json"
            p.write_text('{"status":"E2E_VALIDATION_WINNER_PRESERVED_TEST_REPORTED","winnerSha256":"abc","validationFinalists":2}')
            args = SimpleNamespace(
                sha="a" * 40, run_id="42", attempt="1", population="10000",
                prepare="success", train="success", audit_result="success",
                validation_result="success", test_result="failure",
                closeout=p, audit=None, validation=None, independent_test=None)
            report = mod.build(args)
            self.assertEqual(report["scientificWinnerSha256"], "abc")
            self.assertEqual(report["stages"]["test_result"], "failure")


if __name__ == "__main__":
    unittest.main()
