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

    def test_candidate_state_contract_is_content_addressed_and_contains_no_pii_in_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "candidates.ndjson"
            rows = [
                {"candidate_uuid": "00000000-0000-0000-0000-000000000001", "nome_completo": "Pessoa Um", "data_nascimento": "1980-01-02", "nome_mae": "Mae Um", "estado_identidade": "REFERENCIA"},
                {"candidate_uuid": "00000000-0000-0000-0000-000000000002", "nome_completo": "Pessoa Dois", "data_nascimento": None, "nome_mae": None, "estado_identidade": "REFERENCIA"},
            ]
            source.write_text("\n".join(json.dumps(row) for row in rows) + "\n", encoding="utf-8")
            versions = {"scorer_version": "s1", "ruleset_version": "r1", "model_version": "m1", "input_snapshot_id": "fixture-candidates"}
            manifest_path = dt05.capture(root, source, "run-candidates", versions, 100, "candidate-state")
            manifest_text = manifest_path.read_text(encoding="utf-8")
            manifest = json.loads(manifest_text)
            self.assertEqual(manifest["schema_version"], 2)
            self.assertEqual(manifest["snapshot_kind"], "candidate-state")
            self.assertEqual(manifest["key_field"], "candidate_uuid")
            self.assertEqual(manifest["row_count"], 2)
            self.assertNotIn("Pessoa Um", manifest_text)
            self.assertNotIn("Mae Um", manifest_text)
            self.assertEqual(dt05.verify(root, manifest_path)["rows"], 2)

    def test_candidate_state_rejects_non_reference_and_contract_drift(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "invalid.ndjson"
            source.write_text(json.dumps({
                "candidate_uuid": "00000000-0000-0000-0000-000000000001",
                "nome_completo": "Pessoa", "data_nascimento": None, "nome_mae": None,
                "estado_identidade": "MESCLADA"
            }) + "\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                dt05.capture(root, source, "run-invalid", {}, 10, "candidate-state")

    def test_reject_unsorted(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "unsorted.ndjson"
            source.write_text('{"observation_key":"b"}\n{"observation_key":"a"}\n')
            with self.assertRaises(ValueError):
                dt05.capture(root, source, "run-x", {}, 2)


if __name__ == "__main__":
    unittest.main()
