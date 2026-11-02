import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-eight-receipts-bind.py"
spec = importlib.util.spec_from_file_location("v9_tf_receipts_bind", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class EightReceiptsBindTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.csv = Path(self.tmp.name) / "pairs.csv"
        self.csv.write_text("LeftStableId,RightStableId,SharedSurname\na,b,1\n",
                            encoding="utf-8")
        contract = module.preflight.preflight(self.csv, source_count=30000)
        self.receipts = {}
        for pair in contract["Pairs"]:
            for option in (pair["DiagnosticOptionId"], pair["BilateralOptionId"]):
                self.receipts[option] = dict(
                    OptionId=option, CandidatePairsSha256=contract["CandidatePairsSha256"],
                    SourceRecords=30000, TestUsed=False, PhysicallyExecuted=True,
                    PhysicalRunSha256="a" * 64, FittedModelSha256="b" * 64)

    def tearDown(self):
        self.tmp.cleanup()

    def test_eight_receipts_bound_to_identical_candidates(self):
        result = module.bind_receipts(self.csv, self.receipts)
        self.assertEqual(result["PhysicalReceiptsVerified"], 8)

    def test_missing_receipt_rejected(self):
        self.receipts.pop(next(iter(self.receipts)))
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts)

    def test_unexecuted_and_unpinned_rejected(self):
        option = next(iter(self.receipts))
        for key, value in (("PhysicallyExecuted", False),
                           ("CandidatePairsSha256", "c" * 64),
                           ("TestUsed", True), ("PhysicalRunSha256", "")):
            with self.subTest(field=key):
                row = self.receipts[option]
                original = row[key]
                row[key] = value
                with self.assertRaises(ValueError):
                    module.bind_receipts(self.csv, self.receipts)
                row[key] = original


if __name__ == "__main__":
    unittest.main()
