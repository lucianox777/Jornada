import importlib.util
import unittest
from pathlib import Path

script = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-cross-run-proof.py"
spec = importlib.util.spec_from_file_location("cross_run_proof", script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def observation(run):
    return {"schema": "v9-ref-cross-run-observation-v1",
            "repository": "lucianox777/Jornada", "run_id": run,
            "run_attempt": 1, "fingerprint": "a" * 64,
            "files": {"observacoes.csv": {"sha256": "b" * 64, "size": 12}},
            "store_identity": "operator-attested-ref-mount-1"}


class CrossRunProofTests(unittest.TestCase):
    def test_distinct_consistent_observations_are_not_certification(self):
        result = module.audit(observation(101), observation(102))
        self.assertTrue(result["requires_independent_mount_attestation"])
        self.assertIn("NOT_DURABILITY_CERTIFIED", result["status"])

    def test_same_run_rejected(self):
        with self.assertRaisesRegex(ValueError, "distinct"):
            module.audit(observation(101), observation(101))

    def test_changed_physical_hash_rejected(self):
        second = observation(102)
        second["files"]["observacoes.csv"]["sha256"] = "c" * 64
        with self.assertRaisesRegex(ValueError, "files"):
            module.audit(observation(101), second)

    def test_changed_store_rejected(self):
        second = observation(102)
        second["store_identity"] = "other"
        with self.assertRaisesRegex(ValueError, "store_identity"):
            module.audit(observation(101), second)

    def test_rerun_rejected(self):
        second = observation(102)
        second["run_attempt"] = 2
        with self.assertRaisesRegex(ValueError, "Rerun"):
            module.audit(observation(101), second)


if __name__ == "__main__":
    unittest.main()
