#!/usr/bin/env python3
"""Regression checks over public aggregate-only TF-001 observations recovered from a read-only Actions log.

Not a new physical run, statistical certification, release gate or Pareto selection.
"""
import csv
import math
import unittest
from pathlib import Path

PATH = Path(__file__).resolve().parents[2] / "docs/evidence/V9_TF_001_135_Validation_Aggregates_From_ReadOnly_Log_20261104.csv"
MARGINS = (0.5, 2.0, 4.0)
BASE = "T0.95_TF0"
LEAD = "TF_TWO_PF_MF_W1_M0_A0.5_T0.95"
MOTHER = "TF_ONE_MF_W1_M0_A0.5_T0.95"


def load():
    with PATH.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


class HistoricalTf001ReadOnlyLogTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows = load()
        cls.by_key = {(r["option_id"], float(r["margin"])): r for r in cls.rows}

    def test_family_cardinality_and_diagnostics(self):
        self.assertEqual(len(self.rows), 135)
        self.assertEqual(len(self.by_key), 135)
        arms = {x["option_id"] for x in self.rows}
        self.assertEqual(len(arms), 45)
        self.assertEqual(len([x for x in arms if x.startswith("TF_SHARED_PL_ML_")]), 4)
        self.assertEqual({float(x["margin"]) for x in self.rows}, set(MARGINS))
        self.assertEqual({sum(float(x["margin"]) == m for x in self.rows) for m in MARGINS}, {45})

    def test_cost_not_imputed_or_certified(self):
        for row in self.rows:
            self.assertEqual(row["end_to_end_p95_ms"], "NOT_MEASURED")
            self.assertEqual(row["per_option_cost"], "NOT_MEASURED")
            self.assertEqual(row["cluster_iid_attested"], "false")
            self.assertGreaterEqual(float(row["cluster_conditional_cp_fdr_upper"]), 0)
            self.assertLessEqual(float(row["cluster_conditional_cp_fdr_upper"]), 1)

    def test_fdr_recall_reconcile_with_physical_counts(self):
        for row in self.rows:
            tp, fp = int(row["tp_accepted"]), int(row["fp_accepted"])
            self.assertTrue(0 <= tp <= 7841 and fp >= 0)
            self.assertTrue(0 <= int(row["conflicts"]) and 0 <= int(row["unresolved"]))
            self.assertEqual(tp + fp + int(row["conflicts"]) + int(row["unresolved"]), 10296)
            self.assertTrue(math.isclose(float(row["recall_global"]), tp / 7841, abs_tol=1e-12))
            self.assertTrue(math.isclose(float(row["fdr_accepted"]), fp / (tp + fp), abs_tol=1e-12))
            self.assertTrue(math.isclose(float(row["blocking_recall"]), 7727 / 7841, abs_tol=1e-12))

    def test_historical_tf0_mother_and_lead(self):
        for option, tp, fp, recall in [
            (BASE, 7505, 0, .9571483229179951),
            (MOTHER, 7533, 0, .9607192960081622),
            (LEAD, 7566, 1, .9649279428644305),
        ]:
            row = self.by_key[(option, .5)]
            self.assertEqual((int(row["tp_accepted"]), int(row["fp_accepted"])), (tp, fp))
            self.assertAlmostEqual(float(row["recall_global"]), recall, places=12)

    def test_margins_and_nondominated_quality_observations(self):
        for margin, expected_tp, count95 in [(0.5, 7566, 41), (2., 7490, 8), (4., 7319, 0)]:
            rows = [r for r in self.rows if float(r["margin"]) == margin]
            leader = self.by_key[(LEAD, margin)]
            self.assertEqual(int(leader["tp_accepted"]), expected_tp)
            self.assertEqual(max(int(x["tp_accepted"]) for x in rows), expected_tp)
            self.assertEqual(sum(float(x["recall_global"]) >= .95
                                 for x in rows if not x["option_id"].startswith("TF_SHARED_")), count95)
        # There is no measured relative-cost ordering: nondominance is descriptive
        # in quality alone, never a claim of a new Pareto V4.
        q = [r for r in self.rows if float(r["margin"]) == .5 and
             not r["option_id"].startswith("TF_SHARED_")]
        front = [r for r in q if not any(
            float(p["recall_global"]) >= float(r["recall_global"]) and
            float(p["fdr_accepted"]) <= float(r["fdr_accepted"]) and (
                float(p["recall_global"]) > float(r["recall_global"]) or
                float(p["fdr_accepted"]) < float(r["fdr_accepted"]))
            for p in q)]
        self.assertEqual(len(front), 4)
        self.assertIn(LEAD, {x["option_id"] for x in front})
        self.assertIn(MOTHER, {x["option_id"] for x in front})


if __name__ == "__main__":
    unittest.main()
