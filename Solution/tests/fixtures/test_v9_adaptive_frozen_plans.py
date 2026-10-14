import csv
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("adaptive_frozen", ROOT / "scripts" / "v9-adaptive-frozen-plans.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)

class FrozenAdaptiveTests(unittest.TestCase):
    def fixture(self, root):
        fields = ["observacao_id", "base_person_id", "particao", "nome_normalizado",
                  "nome_fonetico", "nome_mae_normalizado", "nome_mae_fonetico", "data_nascimento"]
        rows = [
            ["1", "a", "TRAIN", "ANA", "AN", "LIA", "L", "2000-01-01"],
            ["2", "a", "TRAIN", "ANA", "AN", "LIA", "L", "2000-01-01"],
            ["3", "b", "TEST", "ANA", "AN", "LIA", "L", "2000-01-01"],
        ]
        source = root / "canonical_observations.csv"
        with source.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(fields)
            writer.writerows(rows)
        hashes = {}
        for name in ("train_name_frequencies.json", "train_joint_name_frequencies.json"):
            file = root / name
            file.write_text("{}", encoding="utf-8")
            hashes[name] = hashlib.sha256(file.read_bytes()).hexdigest()
        manifest = {"frozen": True, "test_used_for_frequency": False,
                    "frozen_source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                    "frequency_sha256": hashes["train_name_frequencies.json"],
                    "joint_frequency_sha256": hashes["train_joint_name_frequencies.json"]}
        (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        return source

    def test_verified_snapshot_produces_diagnostic_plans(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            self.fixture(root)
            result = MODULE.execute(root, root / "report.json")
            self.assertEqual(len(result["plans"]), 3)
            self.assertTrue(result["diagnostic_only"])
            self.assertFalse(result["adaptive_strategy_executed"])
            self.assertEqual(result["plans"][2]["particao"], "TEST")

    def test_tampered_source_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source = self.fixture(root)
            source.write_text(source.read_text() + "\\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "hash mismatch"):
                MODULE.execute(root, root / "report.json")

    def test_test_identity_in_train_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source = self.fixture(root)
            content = source.read_text(encoding="utf-8").replace("3,b,TEST", "3,a,TEST")
            source.write_text(content, encoding="utf-8")
            manifest_path = root / "manifest.json"
            manifest = json.loads(manifest_path.read_text())
            manifest["frozen_source_sha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
            manifest_path.write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, "leaked"):
                MODULE.execute(root, root / "report.json")

    def test_output_cannot_overwrite_snapshot(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source = self.fixture(root)
            with self.assertRaisesRegex(ValueError, "overwrite frozen"):
                MODULE.execute(root, source)
            self.assertTrue(source.read_text(encoding="utf-8").startswith("observacao_id"))

if __name__ == "__main__":
    unittest.main()
