import importlib.util
import pathlib
import unittest

script = pathlib.Path(__file__).resolve().parents[2] / "scripts/v9-birth-tf-train.py"
spec = importlib.util.spec_from_file_location("v9_birth_tf", script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class BirthFrequencyAuditTests(unittest.TestCase):
    def test_deduplicates_base_person_and_excludes_test(self):
        rows=[
            {"split":"TRAIN","truth_id":"p1","birth_date":"1990-01-01","birth_year":"1990"},
            {"split":"TRAIN","truth_id":"p1","birth_date":"1990-01-01","birth_year":"1990"},
            {"split":"TRAIN","truth_id":"p2","birth_date":"1990-01-01","birth_year":"1990"},
            {"split":"TEST","truth_id":"p3","birth_date":"1990-01-01","birth_year":"1990"}]
        report=module.audit(rows)
        self.assertEqual(report["train_base_people"],2)
        self.assertEqual(report["units"]["birth_date"]["largest_value_count"],2)
        self.assertEqual(report["units"]["birth_year"]["present_base_people"],2)
    def test_conflicting_base_birth_fails_closed(self):
        rows=[{"split":"TRAIN","truth_id":"p1","birth_date":"1990-01-01"},
              {"split":"TRAIN","truth_id":"p1","birth_date":"1991-01-01"}]
        with self.assertRaises(ValueError):
            module.audit(rows)

if __name__=="__main__":
    unittest.main()
