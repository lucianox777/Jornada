import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-paired-diagnostics.py"
spec = importlib.util.spec_from_file_location("v9_tf_paired_diagnostics", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SHA = "a" * 64


class TfPairedDiagnosticContractTests(unittest.TestCase):
    def test_four_diagnostics_are_excluded_from_41_eligible_models(self):
        plan = module.paired_manifest(SHA)
        self.assertEqual((plan["EligibleTfModels"], plan["DiagnosticModels"]), (41, 4))
        self.assertFalse(plan["PhysicallyExecuted"])
        self.assertFalse(plan["TestUsed"])
        self.assertEqual(len(plan["Pairs"]), 4)
        self.assertTrue(all(not p["EligibleForAutomaticSelection"] for p in plan["Pairs"]))
        self.assertTrue(all(p["BilateralOptionId"].startswith("TF_TWO_PL_ML_") for p in plan["Pairs"]))

    def test_invalid_candidate_digest_fails_closed(self):
        for sha in ("", "A" * 64, "g" * 64, "a" * 63):
            with self.subTest(sha=sha), self.assertRaises(ValueError):
                module.paired_manifest(sha)

    def test_missing_receipts_fail_closed(self):
        with self.assertRaises(ValueError):
            module.verify_receipts(module.paired_manifest(SHA), {})

    def test_different_candidate_universe_fails_closed(self):
        plan = module.paired_manifest(SHA)
        receipts = {}
        for pair in plan["Pairs"]:
            receipts[pair["DiagnosticOptionId"]] = {
                "DiagnosticOptionId": pair["DiagnosticOptionId"],
                "BilateralOptionId": pair["BilateralOptionId"],
                "DiagnosticCandidatePairsSha256": SHA,
                "BilateralCandidatePairsSha256": "b" * 64,
                **{key: 0 for key in pair["RequiredPhysicalMetrics"]},
            }
        with self.assertRaises(ValueError):
            module.verify_receipts(plan, receipts)

    def test_nonfinite_and_negative_metrics_fail_closed(self):
        plan = module.paired_manifest(SHA)
        def complete():
            return {
                pair["DiagnosticOptionId"]: {
                    "DiagnosticOptionId": pair["DiagnosticOptionId"],
                    "BilateralOptionId": pair["BilateralOptionId"],
                    "DiagnosticCandidatePairsSha256": SHA,
                    "BilateralCandidatePairsSha256": SHA,
                    "SharedSurnameCandidatePairs": 10,
                    "Recall": 0.9, "Fdr": 0.1,
                    "FalseNewIdentityCreations": 0, "PendingDecisions": 2,
                } for pair in plan["Pairs"]
            }
        self.assertTrue(module.verify_receipts(plan, complete()))
        first = plan["Pairs"][0]["DiagnosticOptionId"]
        for key, invalid in (("Recall", float("nan")), ("Fdr", float("inf")),
                             ("Recall", -0.1), ("Fdr", 1.1),
                             ("PendingDecisions", -1),
                             ("FalseNewIdentityCreations", True),
                             ("SharedSurnameCandidatePairs", 1.5)):
            with self.subTest(field=key, invalid=invalid):
                receipts = complete()
                receipts[first][key] = invalid
                with self.assertRaises(ValueError):
                    module.verify_receipts(plan, receipts)



if __name__ == "__main__":
    unittest.main()
