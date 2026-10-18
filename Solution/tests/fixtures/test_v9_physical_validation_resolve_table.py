import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-physical-validation-resolve-table.py"
spec = importlib.util.spec_from_file_location("v9_table", SCRIPT)
table = importlib.util.module_from_spec(spec)
spec.loader.exec_module(table)

def evidence():
    return dict(seed=42, split="VALIDATION", test_used=False,
        source_kind="PHYSICAL_SQL_RUNNER_RESOLVE",
        runs=[dict(profile=p, threshold=t, tf_weight=w,
            final_rank_resolve_evaluated=True, independent_truth_verified=True,
            eligible_true=10, sql_true=8, tp=7, fp=1, conflict=1, unresolved=2,
            sql_p95_ms=120, hoeffding_upper_by_identity=.4)
            for p in ("clean", "correlated") for t in (.8, .9, .95) for w in (0, 1)])

class PhysicalResolveTableTests(unittest.TestCase):
    def test_complete_grid_and_real_denominators(self):
        rows = table.aggregate(evidence())
        self.assertEqual(len(rows), 12)
        self.assertEqual(rows[0]["blocking_recall"], .8)
        self.assertEqual(rows[0]["global_recall"], .7)
        self.assertEqual(rows[0]["fdr_after_resolve"], .125)

    def test_pair_threshold_cannot_masquerade_as_resolve(self):
        e = evidence()
        e["runs"][0]["final_rank_resolve_evaluated"] = False
        with self.assertRaises(ValueError): table.aggregate(e)

    def test_duplicate_grid_rejected(self):
        e = evidence()
        e["runs"][0] = e["runs"][1].copy()
        with self.assertRaises(ValueError): table.aggregate(e)

    def test_test_leak_rejected(self):
        e = evidence()
        e["test_used"] = True
        with self.assertRaises(ValueError): table.aggregate(e)

    def test_missing_independent_truth_rejected(self):
        e = evidence()
        e["runs"][0]["independent_truth_verified"] = False
        with self.assertRaises(ValueError): table.aggregate(e)

    def test_impossible_tp_rejected(self):
        e = evidence()
        e["runs"][0]["tp"] = 9
        with self.assertRaises(ValueError): table.aggregate(e)

    def test_no_accepted_links_has_undefined_fdr(self):
        e = evidence()
        e["runs"][0]["tp"] = e["runs"][0]["fp"] = 0
        self.assertIsNone(table.aggregate(e)[0]["fdr_after_resolve"])

if __name__ == "__main__": unittest.main()
