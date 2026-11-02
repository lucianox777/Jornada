import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-paired-report.py"
spec = importlib.util.spec_from_file_location("v9_tf_paired_report", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SHA = "a" * 64


def fixture():
    pairs = [{"DiagnosticOptionId": f"TF_SHARED_PL_ML_{i}",
              "BilateralOptionId": f"TF_TWO_PL_ML_{i}",
              "CandidatePairsSha256": SHA} for i in range(4)]
    metrics = dict(SharedSurnameCandidatePairs=20, Recall=.9, Fdr=.02,
                   FalseNewIdentityCreations=1, PendingDecisions=2)
    receipts = {
        p["DiagnosticOptionId"]: dict(
            DiagnosticOptionId=p["DiagnosticOptionId"],
            BilateralOptionId=p["BilateralOptionId"],
            DiagnosticCandidatePairsSha256=SHA,
            BilateralCandidatePairsSha256=SHA,
            Diagnostic=metrics.copy(), Bilateral=metrics.copy())
        for p in pairs}
    return {"ContractVersion": "V9_TF_SHARED_SURNAME_PAIRED_DIAGNOSTIC_V1",
            "CandidatePairsSha256": SHA, "PhysicallyExecuted": False,
            "TestUsed": False, "EligibleTfModels": 41,
            "DiagnosticModels": 4, "Pairs": pairs}, receipts


class PairedReportTests(unittest.TestCase):
    def test_complete_paired_report(self):
        manifest, receipts = fixture()
        report = module.report(manifest, receipts)
        self.assertEqual(len(report["Pairs"]), 4)
        self.assertFalse(report["PhysicallyMeasured"])
        self.assertTrue(all(all(value == 0 for value in p[
            "DeltaDiagnosticMinusBilateral"].values()) for p in report["Pairs"]))

    def test_missing_and_mismatched_receipts_fail(self):
        manifest, receipts = fixture()
        receipts.pop(next(iter(receipts)))
        with self.assertRaises(ValueError):
            module.report(manifest, receipts)
        manifest, receipts = fixture()
        next(iter(receipts.values()))["BilateralCandidatePairsSha256"] = "b" * 64
        with self.assertRaises(ValueError):
            module.report(manifest, receipts)

    def test_nonfinite_and_invalid_counts_fail(self):
        for field, value in (("Fdr", float("nan")), ("Recall", float("inf")),
                             ("PendingDecisions", -1),
                             ("FalseNewIdentityCreations", 1.2)):
            manifest, receipts = fixture()
            next(iter(receipts.values()))["Diagnostic"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                module.report(manifest, receipts)


if __name__ == "__main__":
    unittest.main()
