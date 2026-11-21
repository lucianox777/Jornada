"""Differential receipt comparator contract tests (no scientific certification)."""
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-differential-receipt-compare.py"
spec = importlib.util.spec_from_file_location("v9_differential", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def receipt():
    return {"schema": "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1",
            "certified": False, "testTruthUsed": False, "runtime": "scientific",
            "provenance": {"runId": "scientific-run-001", "runtimeArtifactSha256": "d" * 64},
            "modelSha256": "a" * 64, "universeSha256": "b" * 64,
            "operationalEdgesSha256": "c" * 64,
            "decisions": [{"observationId": 1, "decision": "NEW"},
                          {"observationId": 2, "decision": "LINK"},
                          {"observationId": 3, "decision": "NEW"}],
            "groups": [[1, 2], [3]]}


def pair():
    a, b = receipt(), receipt()
    b["runtime"] = "production"
    b["provenance"] = {"runId": "production-run-001", "runtimeArtifactSha256": "e" * 64}
    return a, b


class DifferentialReceiptTests(unittest.TestCase):
    def test_order_and_group_labels_are_ignored(self):
        a, b = pair()
        b["decisions"].reverse()
        b["groups"] = [[3], [2, 1]]
        result = module.compare(a, b)
        self.assertTrue(result["equivalent"])
        self.assertFalse(result["certified"])
        self.assertFalse(result["productionParityCertified"])

    def test_decision_mismatch_fails(self):
        a, b = pair()
        b["decisions"][1]["decision"] = "NEW"
        self.assertEqual(module.compare(a, b)["differentFields"], ["decisions"])

    def test_group_mismatch_fails(self):
        a, b = pair()
        b["groups"] = [[1], [2, 3]]
        self.assertEqual(module.compare(a, b)["differentFields"], ["groups"])

    def test_graph_or_model_mismatch_fails(self):
        a, b = pair()
        b["operationalEdgesSha256"] = "d" * 64
        b["modelSha256"] = "e" * 64
        self.assertEqual(set(module.compare(a, b)["differentFields"]),
                         {"operationalEdgesSha256", "modelSha256"})

    def test_duplicate_or_missing_observations_rejected(self):
        for mutate in (
            lambda r: r["decisions"].append(dict(r["decisions"][0])),
            lambda r: r["groups"].pop(),
            lambda r: r["groups"].append([1]),
        ):
            r = receipt()
            mutate(r)
            with self.subTest(r=r), self.assertRaises(ValueError):
                module.compare(pair()[0], {**r, "runtime": "production", "provenance": pair()[1]["provenance"]})

    def test_cannot_claim_certification_or_use_test_truth(self):
        for field in ("certified", "testTruthUsed"):
            r = receipt()
            r[field] = True
            with self.subTest(field=field), self.assertRaises(ValueError):
                module.compare(pair()[0], {**r, "runtime": "production"})

    def test_missing_or_shared_provenance_rejected(self):
        a, b = pair()
        for mutation in (
            lambda r: r.pop("provenance"),
            lambda r: r["provenance"].update(runId=a["provenance"]["runId"]),
            lambda r: r["provenance"].update(runtimeArtifactSha256=a["provenance"]["runtimeArtifactSha256"]),
            lambda r: r["provenance"].update(runtimeArtifactSha256="not-a-sha"),
        ):
            candidate = {**b, "provenance": dict(b["provenance"])}
            mutation(candidate)
            with self.subTest(candidate=candidate), self.assertRaises(ValueError):
                module.compare(a, candidate)

    def test_cli_exit_codes_and_self_comparison(self):
        a, b = pair()
        with tempfile.TemporaryDirectory() as directory:
            left = Path(directory) / "scientific.json"
            right = Path(directory) / "production.json"
            left.write_text(json.dumps(a), encoding="utf-8")
            right.write_text(json.dumps(b), encoding="utf-8")
            command = [sys.executable, str(SCRIPT), str(left), str(right)]
            ok = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(ok.returncode, 0, ok.stderr)
            self.assertTrue(json.loads(ok.stdout)["equivalent"])
            b["decisions"][0]["decision"] = "LINK"
            right.write_text(json.dumps(b), encoding="utf-8")
            mismatch = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertNotEqual(mismatch.returncode, 0)
            self.assertFalse(json.loads(mismatch.stdout)["equivalent"])
            self_compare = subprocess.run(
                [sys.executable, str(SCRIPT), str(left), str(left)],
                capture_output=True, text=True, check=False)
            self.assertNotEqual(self_compare.returncode, 0)

    def test_same_runtime_or_swapped_receipts_rejected(self):
        a, b = pair()
        for left, right in ((a, a), (b, a), (b, b)):
            with self.subTest(left=left["runtime"], right=right["runtime"]):
                with self.assertRaises(ValueError):
                    module.compare(left, right)


if __name__ == "__main__":
    unittest.main()
