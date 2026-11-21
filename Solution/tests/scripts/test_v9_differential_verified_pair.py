"""Behavioral tests for the fail-closed two-artifact diagnostic gate."""
import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-differential-verified-pair.py"
spec = importlib.util.spec_from_file_location("v9_verified_pair", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def receipt(runtime, run_id, digest):
    return {
        "schema": "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1",
        "certified": False, "testTruthUsed": False, "runtime": runtime,
        "provenance": {"runId": run_id, "runtimeArtifactSha256": digest},
        "modelSha256": "a" * 64, "universeSha256": "b" * 64,
        "operationalEdgesSha256": "c" * 64,
        "decisions": [{"observationId": 1, "decision": "NEW"},
                      {"observationId": 2, "decision": "LINK"}],
        "groups": [[1, 2]],
    }


class VerifiedPairTests(unittest.TestCase):
    def test_distinct_verified_artifacts_equivalent_but_not_certified(self):
        with tempfile.TemporaryDirectory() as root:
            left_path, right_path = Path(root) / "science.bin", Path(root) / "production.bin"
            left_path.write_bytes(b"scientific binary")
            right_path.write_bytes(b"production binary")
            left = receipt("scientific", "science-run", hashlib.sha256(left_path.read_bytes()).hexdigest())
            right = receipt("production", "production-run", hashlib.sha256(right_path.read_bytes()).hexdigest())
            result = module.evaluate(left, left_path, right, right_path)
            self.assertTrue(result["equivalent"])
            self.assertTrue(result["artifactsVerified"])
            self.assertFalse(result["certified"])
            self.assertFalse(result["productionParityCertified"])
            right["decisions"][0]["decision"] = "LINK"
            self.assertEqual(module.evaluate(left, left_path, right, right_path)["differentFields"], ["decisions"])

    def test_tamper_same_artifact_and_same_provenance_fail_closed(self):
        with tempfile.TemporaryDirectory() as root:
            left_path, right_path = Path(root) / "science.bin", Path(root) / "production.bin"
            left_path.write_bytes(b"scientific binary")
            right_path.write_bytes(b"production binary")
            left = receipt("scientific", "science-run", hashlib.sha256(left_path.read_bytes()).hexdigest())
            right = receipt("production", "production-run", hashlib.sha256(right_path.read_bytes()).hexdigest())
            with self.assertRaises(ValueError):
                module.evaluate(left, left_path, right, left_path)
            right_path.write_bytes(b"modified")
            with self.assertRaises(ValueError):
                module.evaluate(left, left_path, right, right_path)
            right_path.write_bytes(b"production binary")
            right["provenance"]["runId"] = "science-run"
            with self.assertRaises(ValueError):
                module.evaluate(left, left_path, right, right_path)


if __name__ == "__main__":
    unittest.main()
