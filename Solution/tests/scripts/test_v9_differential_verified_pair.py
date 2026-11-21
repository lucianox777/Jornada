"""Behavioral tests for the fail-closed two-artifact diagnostic gate."""
import hashlib
import importlib.util
import json
import subprocess
import sys
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

    def test_cli_reports_mismatch_and_exits_nonzero(self):
        with tempfile.TemporaryDirectory() as root:
            folder = Path(root)
            science_artifact = folder / "science.bin"
            production_artifact = folder / "production.bin"
            science_artifact.write_bytes(b"science")
            production_artifact.write_bytes(b"production")
            left = receipt("scientific", "science-run", hashlib.sha256(science_artifact.read_bytes()).hexdigest())
            right = receipt("production", "production-run", hashlib.sha256(production_artifact.read_bytes()).hexdigest())
            science_receipt = folder / "science.json"
            production_receipt = folder / "production.json"
            science_receipt.write_text(json.dumps(left), encoding="utf-8")
            production_receipt.write_text(json.dumps(right), encoding="utf-8")
            command = [sys.executable, str(SCRIPT), str(science_receipt),
                       str(science_artifact), str(production_receipt), str(production_artifact)]
            ok = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(ok.returncode, 0, ok.stderr)
            self.assertTrue(json.loads(ok.stdout)["artifactsVerified"])
            right["groups"] = [[1], [2]]
            production_receipt.write_text(json.dumps(right), encoding="utf-8")
            mismatch = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertNotEqual(mismatch.returncode, 0)
            self.assertEqual(json.loads(mismatch.stdout)["differentFields"], ["groups"])

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
