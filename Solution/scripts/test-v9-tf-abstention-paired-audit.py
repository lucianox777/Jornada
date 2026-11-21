#!/usr/bin/env python3
"""Offline regressions for the TRAIN-only paired abstention diagnostic."""
import importlib.util
import unittest
from pathlib import Path

path = Path(__file__).with_name("v9-tf-abstention-paired-audit.py")
spec = importlib.util.spec_from_file_location("paired_audit", path)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def sample():
    return {
        "schema": mod.SCHEMA, "scope": "TRAIN_ONLY_NO_TEST_NO_ACTIVATION",
        "testUsed": False, "activated": False, "candidatePairsSha256": "a" * 64,
        "clusters": [
            {"clusterId": "c1",
             "TF0": {"eligible": 5, "positive": 2, "tp": 2, "fp": 1,
                     "abstained": 0, "abstainedPositive": 0},
             "TF_ABSTAIN": {"eligible": 5, "positive": 2, "tp": 1, "fp": 0,
                            "abstained": 2, "abstainedPositive": 1}},
            {"clusterId": "c2",
             "TF0": {"eligible": 4, "positive": 1, "tp": 1, "fp": 0,
                     "abstained": 0, "abstainedPositive": 0},
             "TF_ABSTAIN": {"eligible": 4, "positive": 1, "tp": 1, "fp": 0,
                            "abstained": 0, "abstainedPositive": 0}},
        ],
    }


class PairedAuditTests(unittest.TestCase):
    def test_abstention_reduces_end_to_end_recall_not_denominator(self):
        report = mod.audit(sample(), replicates=200)
        self.assertEqual(report["arms"]["TF_ABSTAIN"]["positive"], 3)
        self.assertAlmostEqual(report["arms"]["TF_ABSTAIN"]["recallEndToEnd"], 2 / 3)
        self.assertAlmostEqual(report["deltaRecallAbstainMinusTF0"], -1 / 3)
        self.assertFalse(report["certified"])
        self.assertEqual(report["status"], "INCOMPLETE_NOT_CERTIFIED")

    def test_mismatched_frame_fails(self):
        p = sample()
        p["clusters"][0]["TF_ABSTAIN"]["eligible"] += 1
        with self.assertRaisesRegex(ValueError, "frame/gabarito"):
            mod.audit(p, 200)

    def test_abstention_cannot_disappear_from_denominator(self):
        p = sample()
        p["clusters"][0]["TF_ABSTAIN"]["positive"] = 1
        with self.assertRaisesRegex(ValueError, "inconsistent counts"):
            mod.audit(p, 200)

    def test_impossible_abstention_counts_fail(self):
        p = sample()
        p["clusters"][0]["TF_ABSTAIN"]["abstainedPositive"] = 3
        with self.assertRaisesRegex(ValueError, "inconsistent counts"):
            mod.audit(p, 200)

    def test_tf0_control_cannot_abstain(self):
        p = sample()
        p["clusters"][0]["TF0"]["abstained"] = 1
        with self.assertRaisesRegex(ValueError, "TF0 control must never abstain"):
            mod.audit(p, 200)

    def test_test_scope_is_rejected(self):
        p = sample()
        p["testUsed"] = True
        with self.assertRaisesRegex(ValueError, "unsafe scope"):
            mod.audit(p, 200)

    def test_zero_accepted_fdr_undefined(self):
        p = sample()
        for c in p["clusters"]:
            c["TF_ABSTAIN"]["tp"] = 0
            c["TF_ABSTAIN"]["fp"] = 0
        report = mod.audit(p, 200)
        self.assertIsNone(report["arms"]["TF_ABSTAIN"]["fdrObserved"])
        self.assertIsNone(report["fdrClusterBootstrapDescriptiveUpper95"]["TF_ABSTAIN"])


if __name__ == "__main__":
    unittest.main()
