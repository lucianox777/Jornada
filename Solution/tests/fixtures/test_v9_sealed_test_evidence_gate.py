"""Adversarial contract tests for sealed TEST evidence (no fabricated certification)."""
import importlib.util
import math
from pathlib import Path
import unittest

MODULE = Path(__file__).resolve().parents[2] / "scripts" / "v9-sealed-test-evidence-gate.py"
spec = importlib.util.spec_from_file_location("v9_sealed_test_gate", MODULE)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def fixture():
    a, b = "a" * 64, "b" * 64
    return {
        "schema": "V9_SEALED_TEST_EVIDENCE_V1",
        "frozen": {"snapshotSha256": a, "scorerSha256": a, "tfPolicySha256": a,
                   "blockingSha256": a, "testFrameSha256": b,
                   "gitCommit": "a" * 40, "frozenBeforeTest": True},
        "partition": "TEST", "testUsedForTuning": False,
        "independentEvaluator": True, "completeTruthLabels": True,
        "evaluationMode": "RECORDED_RUNTIME", "completeCandidateUniverse": True,
        "recordedOutcomeFingerprintSha256": a, "artifactStoredOutsideRepository": True,
        "sqlPhysicalExecution": True, "artifactSha256": a, "runId": "evaluation-1",
        "blocking": {"eligibleTruePairsBeforeBlocking": 100, "recoveredTruePairsBySql": 90,
                     "physicalDistinctCandidatePairs": 120,
                     "denominatorComputedBeforeSqlBlocking": True},
        "fdr": {"predeclaredMaxAcceptedPerIdentity": 1, "capEnforcedByDesign": True,
                "independentIdentityClusters": True, "alpha": .05,
                "groups": [{"baseIdentityFingerprint": format(i, "064x"),
                            "trueAccepted": 1, "falseAccepted": 0} for i in range(1000)]},
        "physicalSqlRuns": [{"runId": str(i), "testFrameSha256": b, "blockingSha256": a,
                             "environmentSha256": a, "sqlRulesetSha256": a,
                             "createTablesMs": 1, "bulkObservationsMs": 1,
                             "bulkSignaturesMs": 1, "materializePairsMs": i + 1,
                             "readPairsMs": 1} for i in range(20)]
    }


class TestEvidenceGate(unittest.TestCase):
    def test_contract_does_not_certify(self):
        result = gate.validate(fixture())
        self.assertEqual(result["status"], "EVIDENCE_CONTRACT_VALID_NOT_CERTIFIED")
        self.assertEqual(result["blockingRecall"], .9)
        self.assertFalse(result["operationallyCertified"])
        self.assertEqual(result["materializePairsP99Ms"], 20)

    def test_rejects_posterior_replay_for_v9(self):
        data = fixture()
        data["evaluationMode"] = "POSTERIOR_REPLAY"
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_non_durable_artifact(self):
        data = fixture()
        data["artifactStoredOutsideRepository"] = False
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_post_blocking_denominator(self):
        data = fixture()
        data["blocking"]["denominatorComputedBeforeSqlBlocking"] = False
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_test_tuning(self):
        data = fixture()
        data["testUsedForTuning"] = True
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_duplicate_physical_run(self):
        data = fixture()
        data["physicalSqlRuns"][1]["runId"] = "0"
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_cross_environment_pooling(self):
        data = fixture()
        data["physicalSqlRuns"][1]["environmentSha256"] = "c" * 64
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_unbounded_fdr_denominator(self):
        data = fixture()
        data["fdr"]["groups"] = data["fdr"]["groups"][:2]
        data["fdr"]["groups"][1]["trueAccepted"] = 0
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_duplicate_identity(self):
        data = fixture()
        data["fdr"]["groups"][1]["baseIdentityFingerprint"] = "0" * 64
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_missing_physical_runs(self):
        data = fixture()
        data["physicalSqlRuns"] = data["physicalSqlRuns"][:19]
        with self.assertRaises(ValueError):
            gate.validate(data)

    def test_rejects_missing_blocking_recovery(self):
        data = fixture()
        data["blocking"]["recoveredTruePairsBySql"] = 101
        with self.assertRaises(ValueError):
            gate.validate(data)


if __name__ == "__main__":
    unittest.main()
