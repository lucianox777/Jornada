import importlib.util
import pathlib
import unittest

root = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("v9", root / "scripts/v9-batch-memory-configs.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class PartitionGuard(unittest.TestCase):
    def test_split_isolation(self):
        rows = [
            {"id":"a","truth_id":"P","split":"TRAIN","name":"ANA"},
            {"id":"b","truth_id":"P","split":"TRAIN","name":"ANA"},
            {"id":"c","truth_id":"Q","split":"TEST","name":"ANA"},
            {"id":"d","truth_id":"Q","split":"TEST","name":"ANA"}
        ]
        result = module.evaluate(rows, [["name"]], 10)
        self.assertEqual(result["eligible_m"], 2)
        self.assertEqual(result["captured_m"], 2)
        self.assertEqual(result["unique_candidates"], 2)
        self.assertEqual(result["passes"][0]["new_u"], 0)

if __name__ == "__main__":
    unittest.main()
