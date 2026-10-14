import importlib.util
import pathlib
import unittest

PATH = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "v9-adaptive-frequency-planner.py"
spec = importlib.util.spec_from_file_location("v9_adaptive", PATH)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class AdaptivePlannerTests(unittest.TestCase):
    def test_train_only_and_deduplicated_truth(self):
        rows = [
            {"split":"TRAIN","truth_id":"a","person_first":"ANA","birth_date":"2000-01-01","mother_first":"LIA"},
            {"split":"TRAIN","truth_id":"a","person_first":"ANA","birth_date":"2000-01-01","mother_first":"LIA"},
            {"split":"TEST","truth_id":"b","person_first":"ANA","birth_date":"2000-01-01","mother_first":"LIA"},
        ]
        counts=module.train_counts(rows)
        self.assertEqual(counts[("person_first","birth_date")][("ANA","2000-01-01")],1)
        self.assertEqual(len(module.plan(rows[2],counts)),3)

    def test_missing_independent_fields_never_create_single_field_pass(self):
        rows=[{"split":"TRAIN","truth_id":"a","person_first":"ANA"}]
        counts=module.train_counts(rows)
        self.assertEqual(module.plan(rows[0],counts),[])

    def test_rare_joint_key_precedes_frequent(self):
        rows=[
            {"split":"TRAIN","truth_id":"a","person_first":"ANA","mother_first":"LIA","birth_date":"2000"},
            {"split":"TRAIN","truth_id":"b","person_first":"ANA","mother_first":"LIA","birth_date":"2001"},
        ]
        counts=module.train_counts(rows)
        selected=module.plan(rows[0],counts)
        self.assertEqual(selected[0]["train_identity_frequency"],1)
        self.assertEqual(selected[-1]["train_identity_frequency"],2)

if __name__ == "__main__":
    unittest.main()
