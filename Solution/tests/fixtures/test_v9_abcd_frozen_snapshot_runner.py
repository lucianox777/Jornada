import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

path = Path(__file__).resolve().parents[2] / "scripts" / "v9-abcd-frozen-snapshot-runner.py"
spec = importlib.util.spec_from_file_location("v9_runner", path)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

class FrozenRunnerTests(unittest.TestCase):
    def test_hash_mismatch_rejected_before_experiment(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "canonical_observations.csv").write_text("wrong", encoding="utf-8")
            (root / "manifest.json").write_text(json.dumps({
                "frozen": True, "test_used_for_frequency": False,
                "frozen_source_sha256": "different"}), encoding="utf-8")
            with self.assertRaises(ValueError):
                runner.execute(root, root / "result.json")

if __name__ == "__main__":
    unittest.main()
