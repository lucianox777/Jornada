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

    def test_exact_truth_bytes_still_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive_path, truth_path = root / "payload.zip", root / "truth.json"
            # ZIP digest and embedded truth bytes cannot self-reference exactly;
            # test the comparison using a separate external file with identical bytes
            # after the binding check, by replacing the digest check at the module boundary.
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("renamed.txt", b"external-truth-bytes")
            truth_path.write_bytes(b"external-truth-bytes")
            # Confirm the ZIP comparison path independently of JSON parsing using
            # a valid JSON fixture embedded in ZIP and a digest-bypass controlled by
            # mocking the digest hexdigest (only for this negative-path test).
            payload = json.dumps({"zipSha256": "a" * 64, "expectedIdentities": ["A"]}).encode()
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("renamed.txt", payload)
            truth_path.write_bytes(payload)
            with patch.object(module.hashlib, "sha256") as sha:
                sha.return_value.hexdigest.return_value = "a" * 64
                with self.assertRaisesRegex(ValueError, "embedded"):
                    module.validate(archive_path, truth_path)


if __name__ == "__main__":
    unittest.main()
