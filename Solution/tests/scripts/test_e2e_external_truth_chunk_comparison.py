"""Bounded comparison of external ground truth against ZIP members."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("truth_chunk_comparison", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ExternalTruthChunkComparisonTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.zip_path = self.root / "payload.zip"
        self.truth_path = self.root / "external-truth.json"

    def write_fixture(self, member_bytes, pad=0):
        with zipfile.ZipFile(self.zip_path, "w") as archive:
            archive.writestr("renamed.bin", member_bytes)
        truth = {"zipSha256": hashlib.sha256(self.zip_path.read_bytes()).hexdigest(),
                 "expectedIdentities": ["synthetic-A"]}
        self.truth_path.write_text(json.dumps(truth) + " " * pad, encoding="utf-8")

    def test_large_nonmatching_member_is_read_in_bounded_chunks(self):
        self.write_fixture(b"x" * (3 * 1024 * 1024))
        original = zipfile.ZipExtFile.read
        reads = []

        def bounded(stream, size=-1):
            reads.append(size)
            self.assertGreater(size, 0)
            self.assertLessEqual(size, 64 * 1024)
            return original(stream, size)

        with mock.patch.object(zipfile.ZipExtFile, "read", bounded):
            self.assertEqual(module.validate(self.zip_path, self.truth_path)["status"], "PASS")
        # Unequal-size members should be skipped without decompression.
        self.assertEqual(reads, [])

    def test_equal_size_member_with_late_difference_is_allowed(self):
        self.write_fixture(b"placeholder", pad=200000)
        truth_bytes = self.truth_path.read_bytes()
        different = truth_bytes[:-1] + (b"X" if truth_bytes[-1:] != b"X" else b"Y")
        with zipfile.ZipFile(self.zip_path, "w") as archive:
            archive.writestr("different.bin", different)
        # The fixture has a circular digest dependency only for this boundary
        # comparison test; isolate digest verification without changing bytes.
        with mock.patch.object(module.hashlib, "sha256") as sha:
            sha.return_value.hexdigest.return_value = json.loads(
                self.truth_path.read_text())["zipSha256"]
            self.assertEqual(module.validate(self.zip_path, self.truth_path)["status"], "PASS")

    def test_later_member_matching_truth_is_rejected(self):
        self.write_fixture(b"placeholder", pad=130000)
        truth_bytes = self.truth_path.read_bytes()
        with zipfile.ZipFile(self.zip_path, "w") as archive:
            archive.writestr("first.bin", b"Z" * len(truth_bytes))
            archive.writestr("second-renamed.bin", truth_bytes)
        with mock.patch.object(module.hashlib, "sha256") as sha:
            sha.return_value.hexdigest.return_value = json.loads(
                self.truth_path.read_text())["zipSha256"]
            with self.assertRaisesRegex(ValueError, "embedded"):
                module.validate(self.zip_path, self.truth_path)

    def test_matching_large_renamed_truth_is_rejected_with_bounded_reads(self):
        # The ZIP SHA cannot self-reference its own truth; mock the digest only
        # to isolate the member-byte boundary and exercise all comparison chunks.
        self.write_fixture(b"placeholder", pad=200000)
        truth_bytes = self.truth_path.read_bytes()
        with zipfile.ZipFile(self.zip_path, "w") as archive:
            archive.writestr("renamed.bin", truth_bytes)
        actual_digest = hashlib.sha256(self.zip_path.read_bytes()).hexdigest()
        self.truth_path.write_text(
            json.dumps({"zipSha256": actual_digest, "expectedIdentities": ["synthetic-A"]})
            + " " * 200000, encoding="utf-8")
        # Repackage the final truth bytes and bypass only the circular ZIP digest.
        with zipfile.ZipFile(self.zip_path, "w") as archive:
            archive.writestr("renamed.bin", self.truth_path.read_bytes())
        original = zipfile.ZipExtFile.read
        reads = []

        def bounded(stream, size=-1):
            reads.append(size)
            self.assertGreater(size, 0)
            self.assertLessEqual(size, 64 * 1024)
            return original(stream, size)

        with mock.patch.object(zipfile.ZipExtFile, "read", bounded), mock.patch.object(
            module.hashlib, "sha256"
        ) as sha:
            sha.return_value.hexdigest.return_value = json.loads(self.truth_path.read_text())["zipSha256"]
            with self.assertRaisesRegex(ValueError, "embedded"):
                module.validate(self.zip_path, self.truth_path)
        self.assertGreater(len(reads), 1)
        self.assertLessEqual(max(reads), 64 * 1024)


if __name__ == "__main__":
    unittest.main()
