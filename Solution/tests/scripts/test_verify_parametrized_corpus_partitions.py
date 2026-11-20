"""Disposable fixture tests for read-only parametrized corpus verifier."""
import csv
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/verify-parametrized-corpus-partitions.py"
spec = importlib.util.spec_from_file_location("partition_verifier", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PartitionVerifierTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.people = [
            {"base_person_id": "P1", "particao": "TRAIN"},
            {"base_person_id": "P2", "particao": "TEST"},
        ]
        self.observations = [
            {"base_person_id": "P1", "particao": "TRAIN"},
            {"base_person_id": "P2", "particao": "TEST"},
        ]
        self.percentages = {"train": 50, "validation": 0, "test": 50}
        self.write_fixture()

    def write_fixture(self):
        for filename, rows in (("pessoas_verdade.csv", self.people),
                               ("observacoes.csv", self.observations)):
            with (self.root / filename).open("w", newline="", encoding="utf-8") as f:
                writer = csv.DictWriter(f, fieldnames=("base_person_id", "particao"))
                writer.writeheader()
                writer.writerows(rows)
        (self.root / "gabarito.json").write_text("{}", encoding="utf-8")
        outputs = [
            {"path": name, "sha256": hashlib.sha256((self.root / name).read_bytes()).hexdigest()}
            for name in ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json")
        ]
        (self.root / "generation-manifest.json").write_text(json.dumps({
            "synthetic_population": len(self.people),
            "partition_percentages": self.percentages,
            "outputs": outputs,
        }), encoding="utf-8")

    def test_valid_fixture(self):
        self.assertEqual(module.verify(self.root)["observations"], 2)

    def test_rejects_observation_partition_leak(self):
        self.observations[0]["particao"] = "TEST"
        self.write_fixture()
        with self.assertRaisesRegex(ValueError, "Observation partition mismatch"):
            module.verify(self.root)

    def test_rejects_orphan_observation(self):
        self.observations[0]["base_person_id"] = "P999"
        self.write_fixture()
        with self.assertRaisesRegex(ValueError, "Orphan observation"):
            module.verify(self.root)

    def test_rejects_tampered_hash(self):
        (self.root / "gabarito.json").write_text('{"tampered":true}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            module.verify(self.root)

    def test_rejects_nonempty_zero_percent_partition(self):
        self.percentages = {"train": 0, "validation": 50, "test": 50}
        self.write_fixture()
        with self.assertRaisesRegex(ValueError, "Nonempty zero-percent partition"):
            module.verify(self.root)

    def test_rejects_duplicate_person(self):
        self.people[1]["base_person_id"] = "P1"
        self.write_fixture()
        with self.assertRaisesRegex(ValueError, "Duplicate or empty person ID"):
            module.verify(self.root)


if __name__ == "__main__":
    unittest.main()
