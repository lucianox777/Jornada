import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-freeze-canonical-name-frequency.py"
spec = importlib.util.spec_from_file_location("v9_freeze", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class FrozenCanonicalFrequencyTests(unittest.TestCase):
    def test_e0_triple_frequency_counts_distinct_train_identities(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "source.csv"
            columns = ["observacao_id", "base_person_id", "particao",
                       "nome_normalizado", "nome_fonetico", "nome_mae_normalizado",
                       "nome_mae_fonetico", "data_nascimento"]
            rows = [
                ["1", "P1", "TRAIN", "ANA", "AN", "MARIA", "MR", "1980-01-01"],
                ["2", "P1", "TRAIN", "ANA", "AN", "MARIA", "MR", "1980-01-01"],
                ["3", "P2", "TRAIN", "ANA", "AN", "MARIA", "MR", "1980-01-01"],
                ["4", "P3", "TEST", "ANA", "AN", "MARIA", "MR", "1980-01-01"],
            ]
            with source.open("w", encoding="utf-8", newline="") as handle:
                writer = csv.writer(handle)
                writer.writerow(columns)
                writer.writerows(rows)
            target = root / "snapshot"
            result = module.freeze(source, target, "BLOCKING_PROJECTION_KEY_PROJECTOR_V4")
            self.assertEqual(result["observations"], 4)
            self.assertEqual((target / "canonical_observations.csv").read_bytes(), source.read_bytes())
            self.assertEqual(result["source_sha256"], result["frozen_source_sha256"])
            joint = json.loads((target / "train_joint_name_frequencies.json").read_text(encoding="utf-8"))
            key = "nome_normalizado|nome_mae_normalizado|data_nascimento"
            self.assertEqual(joint[key], [{"key": ["ANA", "MARIA", "1980-01-01"], "identities": 2}])
            with self.assertRaises(FileExistsError):
                module.freeze(source, target, "BLOCKING_PROJECTION_KEY_PROJECTOR_V4")

    def test_partition_leakage_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "source.csv"
            columns = ["observacao_id", "base_person_id", "particao",
                       "nome_normalizado", "nome_fonetico", "nome_mae_normalizado",
                       "nome_mae_fonetico", "data_nascimento"]
            with source.open("w", encoding="utf-8", newline="") as handle:
                writer = csv.writer(handle)
                writer.writerow(columns)
                writer.writerows([
                    ["1", "P1", "TRAIN", "ANA", "AN", "MARIA", "MR", "1980-01-01"],
                    ["2", "P1", "TEST", "ANA", "AN", "MARIA", "MR", "1980-01-01"],
                ])
            with self.assertRaises(ValueError):
                module.freeze(source, root / "snapshot", "BLOCKING_PROJECTION_KEY_PROJECTOR_V4")
            self.assertFalse((root / "snapshot").exists())


if __name__ == "__main__":
    unittest.main()
