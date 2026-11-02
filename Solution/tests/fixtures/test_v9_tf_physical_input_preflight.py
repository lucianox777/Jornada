import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-physical-input-preflight.py"
spec = importlib.util.spec_from_file_location("v9_tf_physical_input", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PhysicalInputTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.sources = Path(self.tmp.name) / "sources.csv"
        self.sources.write_text("StableId\n" + "".join(f"id{i}\n" for i in range(29997)) + "a\nb\nc\n", encoding="utf-8")
        self.csv = Path(self.tmp.name) / "pairs.csv"
        self.csv.write_text("LeftStableId,RightStableId,SharedSurname\na,b,1\na,c,0\n",
                            encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def test_eight_evaluations_on_same_candidate_sha(self):
        result = module.preflight(self.csv, source_count=30000, source_ids_csv=self.sources)
        self.assertEqual(result["RequiredModelEvaluations"], 8)
        self.assertFalse(result["PhysicallyExecuted"])
        self.assertEqual(len(result["Pairs"]), 4)
        self.assertEqual({p["SharedSurnameCandidatePairs"] for p in result["Pairs"]}, {1})
        self.assertEqual(len({p["CandidatePairsSha256"] for p in result["Pairs"]}), 1)

    def test_candidate_outside_source_census_fails(self):
        self.csv.write_text("LeftStableId,RightStableId,SharedSurname\\na,unknown,1\\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            module.preflight(self.csv, source_count=30000, source_ids_csv=self.sources)

    def test_non_30k_fails(self):
        with self.assertRaises(ValueError):
            module.preflight(self.csv, source_count=29999, source_ids_csv=self.sources)

    def test_duplicate_unordered_pair_fails(self):
        with self.csv.open("a", encoding="utf-8") as stream:
            stream.write("b,a,1\n")
        with self.assertRaises(ValueError):
            module.preflight(self.csv, source_count=30000)

    def test_invalid_shared_flag_fails(self):
        self.csv.write_text("LeftStableId,RightStableId,SharedSurname\na,b,unknown\n",
                            encoding="utf-8")
        with self.assertRaises(ValueError):
            module.preflight(self.csv, source_count=30000)


if __name__ == "__main__":
    unittest.main()
