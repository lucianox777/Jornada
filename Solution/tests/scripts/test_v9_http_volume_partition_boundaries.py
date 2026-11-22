"""Offline partition contracts for the approved 1k -> 10k synthetic E2E staging.

No SQL, HTTP, linkage, or scientific TRAIN/TEST execution.
"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("volume_fixture", ROOT / "scripts/v9-http-volume-fixture.py")
producer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(producer)
fixture = ROOT / "tests/fixtures/ingestao/AA01_v2"


class VolumePartitionBoundaryTests(unittest.TestCase):
    def test_ten_nonoverlapping_1k_windows_cover_exactly_10k(self):
        windows = [(1 + i * 1000, (i + 1) * 1000) for i in range(10)]
        self.assertEqual(windows[0], (1, 1000))
        self.assertEqual(windows[-1], (9001, 10000))
        self.assertEqual(len({n for start, end in windows for n in range(start, end + 1)}), 10000)
        with tempfile.TemporaryDirectory() as temp:
            for index, (start, end) in enumerate(windows):
                # Small actual generated slices exercise every partition's edges.
                for point in (start, end):
                    output = Path(temp) / f"part-{index}-{point}"
                    receipt = producer.generate(fixture, output, 1, 355, point)
                    person = json.loads((output / "pessoas.jsonl").read_text(encoding="utf-8"))
                    record = json.loads((output / "registros.jsonl").read_text(encoding="utf-8"))
                    self.assertEqual(receipt["startIndex"], point)
                    self.assertEqual(person["idPessoaEntrega"], f"PESSOA-E2E-VOL-{point:06d}")
                    self.assertEqual(person["cpf"], producer.make_cpf(point, 355))
                    self.assertEqual(record["idPessoaEntrega"], person["idPessoaEntrega"])
                    self.assertEqual(record["codigoRegistroOrigem"], f"E2E-AA01-VOL-2026-{point:06d}")

    def test_overflow_and_unapproved_seed_fail_before_output(self):
        with tempfile.TemporaryDirectory() as temp:
            for count, seed, start in ((1001, 355, 1), (2, 355, 10000),
                                       (1, 355, 10001), (1, 356, 1),
                                       (0, 355, 1), (1, 355, 0)):
                output = Path(temp) / f"{count}-{seed}-{start}"
                with self.subTest(count=count, seed=seed, start=start):
                    with self.assertRaises(ValueError):
                        producer.generate(fixture, output, count, seed, start)
                    self.assertFalse(output.exists())

    def test_existing_nonempty_output_cannot_be_overwritten(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "already-generated"
            producer.generate(fixture, output, 1, 355, 1)
            before = (output / "pessoas.jsonl").read_bytes()
            with self.assertRaises(FileExistsError):
                producer.generate(fixture, output, 1, 355, 2)
            self.assertEqual((output / "pessoas.jsonl").read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
