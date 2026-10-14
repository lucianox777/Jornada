import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("v9batch", ROOT / "scripts" / "v9-batch-memory-configs.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class MemoryBatchFixture(unittest.TestCase):
    def test_marginal_and_dedup(self):
        rows = mod.load(ROOT / "tests/fixtures/v9-memory-small.jsonl")
        first = mod.evaluate(rows, [["person_first"]], 3)
        self.assertEqual(first["eligible_m"], 1)
        self.assertEqual(first["captured_m"], 1)
        self.assertEqual(first["unique_candidates"], 3)
        self.assertEqual(first["passes"][0]["new_u"], 2)
        two = mod.evaluate(rows, [["person_first", "mother_first"], ["person_first", "birth_year"]], 3)
        self.assertEqual(two["passes"][0]["new_pairs"], 1)
        self.assertEqual(two["passes"][1]["new_pairs"], 2)
        self.assertEqual(two["passes"][1]["new_m"], 0)
        self.assertEqual(two["captured_m"], 1)
    def test_fanout_cap_suppresses_cell(self):
        rows = mod.load(ROOT / "tests/fixtures/v9-memory-small.jsonl")
        result = mod.evaluate(rows, [["person_first"]], 2)
        self.assertEqual(result["unique_candidates"], 0)
        self.assertEqual(result["passes"][0]["suppressed_cells"], 1)

if __name__ == "__main__":
    unittest.main()
