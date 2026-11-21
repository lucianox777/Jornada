"""Behavioral tests for byte-level V9 diagnostic artifact verification."""
import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-differential-verify-artifacts.py"
spec = importlib.util.spec_from_file_location("v9_artifact_verify", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ArtifactVerificationTests(unittest.TestCase):
    def test_actual_bytes_and_tampering(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "runtime.bin"
            path.write_bytes(b"runtime-one")
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            receipt = {"schema": "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1",
                       "runtime": "scientific",
                       "provenance": {"runtimeArtifactSha256": digest}}
            result = module.verify(receipt, path)
            self.assertTrue(result["verified"])
            self.assertFalse(result["certified"])
            path.write_bytes(b"runtime-two")
            with self.assertRaises(ValueError):
                module.verify(receipt, path)

    def test_missing_artifact_and_invalid_claims(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "missing.bin"
            receipt = {"schema": "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1",
                       "provenance": {"runtimeArtifactSha256": "a" * 64}}
            with self.assertRaises(ValueError):
                module.verify(receipt, path)
            path.write_bytes(b"x")
            receipt["provenance"]["runtimeArtifactSha256"] = "INVALID"
            with self.assertRaises(ValueError):
                module.verify(receipt, path)


if __name__ == "__main__":
    unittest.main()
