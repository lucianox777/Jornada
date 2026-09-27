"""Run: python -m unittest discover -s Solution/tools/tests -p 'test_dt05*.py'"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "dt05_parquet_snapshot.py"
spec = importlib.util.spec_from_file_location("dt05_parquet_snapshot", SCRIPT)
dt05 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dt05)


@unittest.skipUnless(importlib.util.find_spec("pyarrow"), "pyarrow not installed")
class SnapshotTests(unittest.TestCase):
    def test_capture_reuse_replay_and_corruption(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "input.ndjson"
            source.write_text("\n".join(json.dumps({"observation_key": str(i).zfill(4), "name": "synthetic" + str(i)}) for i in range(5)) + "\n")
            versions = {"scorer_version": "s1", "ruleset_version": "r1", "model_version": "m1", "input_snapshot_id": "fixture1"}
            first = dt05.capture(root, source, "run-1", versions, 2)
            first_manifest = json.loads(first.read_text())
            self.assertEqual(dt05.verify(root, first)["rows"], 5)
            second = dt05.capture(root, source, "run-2", versions, 2)
            second_manifest = json.loads(second.read_text())
            self.assertEqual(first_manifest["partitions"], second_manifest["partitions"])
            self.assertEqual(len(list((root / "objects").rglob("*.parquet"))), 3)
            with self.assertRaises(FileExistsError):
                dt05.capture(root, source, "run-1", versions, 2)
            changed = root / "changed.ndjson"
            lines = source.read_text().splitlines()
            lines[-1] = json.dumps({"observation_key": "0004", "name": "changed"})
            changed.write_text("\n".join(lines) + "\n")
            third = dt05.capture(root, changed, "run-3", versions, 2)
            third_manifest = json.loads(third.read_text())
            self.assertEqual(first_manifest["partitions"][:2], third_manifest["partitions"][:2])
            self.assertNotEqual(first_manifest["partitions"][2], third_manifest["partitions"][2])
            self.assertEqual(dt05.verify(root, third)["rows"], 5)
            corrupted = root / third_manifest["partitions"][2]["path"]
            corrupted.write_bytes(b"tampered")
            with self.assertRaises(ValueError):
                dt05.verify(root, third)

    def test_reject_unsorted(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "unsorted.ndjson"
            source.write_text('{"observation_key":"b"}\n{"observation_key":"a"}\n')
            with self.assertRaises(ValueError):
                dt05.capture(root, source, "run-x", {}, 2)


if __name__ == "__main__":
    unittest.main()
