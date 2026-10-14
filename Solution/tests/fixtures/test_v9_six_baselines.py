import importlib.util
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("v9batch", ROOT / "scripts/v9-batch-memory-configs.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class SixBaselineRuns(unittest.TestCase):
    def test_all_six_on_real_runner(self):
        rows = mod.load(ROOT / "tests/fixtures/v9-memory-small.jsonl")
        configs = json.loads((ROOT / "tests/fixtures/v9-blocking-baselines.json").read_text())
        self.assertEqual(len(configs), 6)
        for config in configs:
            with self.subTest(name=config["name"]):
                result = mod.evaluate(rows, config["passes"], config["fanout_cap"])
                self.assertEqual(result["eligible_m"], 1)
                self.assertEqual(result["captured_m"], 1)
                self.assertGreaterEqual(result["unique_candidates"], 1)
                self.assertEqual(sum(p["new_m"] + p["new_u"] for p in result["passes"]), result["unique_candidates"])
                self.assertEqual(sum(p["new_m"] for p in result["passes"]), 1)

if __name__ == "__main__":
    unittest.main()
