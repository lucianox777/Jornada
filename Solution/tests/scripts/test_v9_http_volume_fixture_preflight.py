"""Negative tests for synthetic 1k fixture integrity; no SQL, HTTP or real identities."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]

def load(filename, name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

producer = load("v9-http-volume-fixture.py", "v9_volume_producer")
preflight = load("v9-http-volume-fixture-preflight.py", "v9_volume_preflight")


class SyntheticVolumeIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name) / "fixture"
        producer.generate(ROOT / "tests/fixtures/ingestao/AA01_v2", self.directory, 1000, 355)

    def test_approved_1k_passes_without_identity_receipt(self):
        receipt = preflight.verify(self.directory, 1000)
        self.assertEqual(receipt["personCount"], 1000)
        self.assertEqual(receipt["recordCount"], 1000)
        self.assertNotIn("cpf", json.dumps(receipt).lower())
        self.assertIn("NOT_LINKAGE_ORACLE", receipt["scope"])

    def test_duplicate_cpf_fails(self):
        path = self.directory / "pessoas.jsonl"
        rows = path.read_text().splitlines()
        first, second = json.loads(rows[0]), json.loads(rows[1])
        second["cpf"] = first["cpf"]
        rows[1] = json.dumps(second)
        path.write_text("\n".join(rows) + "\n")
        with self.assertRaises(ValueError):
            preflight.verify(self.directory, 1000)

    def test_orphan_record_fails(self):
        path = self.directory / "registros.jsonl"
        rows = path.read_text().splitlines()
        first = json.loads(rows[0])
        first["idPessoaEntrega"] = "PESSOA-E2E-VOL-999999"
        rows[0] = json.dumps(first)
        path.write_text("\n".join(rows) + "\n")
        with self.assertRaises(ValueError):
            preflight.verify(self.directory, 1000)

    def test_truncated_persons_fails(self):
        path = self.directory / "pessoas.jsonl"
        path.write_text("\n".join(path.read_text().splitlines()[:-1]) + "\n")
        with self.assertRaises(ValueError):
            preflight.verify(self.directory, 1000)

    def test_unapproved_count_fails(self):
        with self.assertRaises(ValueError):
            preflight.verify(self.directory, 10000)


if __name__ == "__main__":
    unittest.main()
