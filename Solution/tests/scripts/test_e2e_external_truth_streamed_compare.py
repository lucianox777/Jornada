"""Regression: large ZIP entries must not be decompressed into memory for truth comparison."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("e2e_external_truth_streaming", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ExternalTruthStreamingTests(unittest.TestCase):
    def test_large_entry_not_read_when_size_differs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive_path, truth_path = root / "payload.zip", root / "truth.json"
            with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("large.txt", b"X" * (4 * 1024 * 1024))
            truth_path.write_text(json.dumps({
                "zipSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
                "expectedIdentities": ["synthetic-A"],
            }), encoding="utf-8")
            with patch.object(zipfile.ZipFile, "read", side_effect=AssertionError("unbounded read")):
                result = module.validate(archive_path, truth_path)
            self.assertEqual(result["status"], "PASS")

    def test_equal_sized_nonmatching_entry_uses_bounded_stream(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive_path, truth_path = root / "payload.zip", root / "truth.json"
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("payload.txt", b"X" * 1024)
            truth = {
                "zipSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
                "expectedIdentities": ["A"],
            }
            # Size matching is exercised using a valid JSON document padded
            # with trailing whitespace (JSON permits it).
            initial = json.dumps(truth).encode()
            truth_path.write_bytes(initial + b" " * (1024 - len(initial)))
            with patch.object(zipfile.ZipFile, "read", side_effect=AssertionError("unbounded read")):
                self.assertEqual(module.validate(archive_path, truth_path)["status"], "PASS")

    def test_embedded_truth_after_nonmatching_equal_sized_entry_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive_path, truth_path = root / "payload.zip", root / "truth.json"
            payload = json.dumps({"zipSha256": "a" * 64, "expectedIdentities": ["A"]}).encode()
            with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("first.txt", b"X" * len(payload))
                archive.writestr("renamed-truth.txt", payload)
            truth_path.write_bytes(payload)
            # The SHA is intentionally mocked: exact self-referential ZIP digest
            # cannot be embedded in the same ZIP; the byte-comparison is isolated.
            with patch.object(module.hashlib, "sha256") as sha, \\
                    patch.object(zipfile.ZipFile, "read", side_effect=AssertionError("unbounded read")):
                sha.return_value.hexdigest.return_value = "a" * 64
                with self.assertRaisesRegex(ValueError, "embedded"):
                    module.validate(archive_path, truth_path)

    def test_exact_truth_bytes_still_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive_path, truth_path = root / "payload.zip", root / "truth.json"
            # A self-referential ZIP digest is impossible; isolate the byte guard
            # by mocking only the digest, not the ZIP reading logic.
            payload = json.dumps({"zipSha256": "a" * 64, "expectedIdentities": ["A"]}).encode()
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("renamed.txt", payload)
            truth_path.write_bytes(payload)
            with patch.object(module.hashlib, "sha256") as sha, \
                    patch.object(zipfile.ZipFile, "read", side_effect=AssertionError("unbounded read")):
                sha.return_value.hexdigest.return_value = "a" * 64
                with self.assertRaisesRegex(ValueError, "embedded"):
                    module.validate(archive_path, truth_path)


if __name__ == "__main__":
    unittest.main()
