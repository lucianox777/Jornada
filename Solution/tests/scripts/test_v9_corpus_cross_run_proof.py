import importlib.util
import unittest
import json
import tempfile
from pathlib import Path

script = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-cross-run-proof.py"
spec = importlib.util.spec_from_file_location("cross_run_proof", script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def observation(run):
    return {"schema": "v9-ref-cross-run-observation-v1",
            "repository": "lucianox777/Jornada", "run_id": run,
            "run_attempt": 1, "fingerprint": "a" * 64,
            "files": {name: {"sha256": "b" * 64, "size": 12} for name in\n                      ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json",\n                       "generation-manifest.json")},
            "store_identity": "operator-attested-ref-mount-1"}


class CrossRunProofTests(unittest.TestCase):
    def test_distinct_consistent_observations_are_not_certification(self):
        result = module.audit(observation(101), observation(102))
        self.assertTrue(result["requires_independent_mount_attestation"])
        self.assertIn("NOT_DURABILITY_CERTIFIED", result["status"])

    def test_observe_reads_verified_physical_snapshot(self):
        snapshot_script = script.with_name("v9-corpus-snapshot.py")
        snapshot_spec = importlib.util.spec_from_file_location("ref_snapshot_test", snapshot_script)
        snapshot = importlib.util.module_from_spec(snapshot_spec)
        snapshot_spec.loader.exec_module(snapshot)
        with tempfile.TemporaryDirectory() as root:
            base = Path(root)
            config = {"seed": 42, "people": 2}
            config_path = base / "config.json"
            config_path.write_text(json.dumps(config))
            corpus = base / "corpus"
            corpus.mkdir()
            for name in snapshot.REQUIRED:
                (corpus / name).write_text("test-data", encoding="utf-8")
            manifest = snapshot.snapshot(config, corpus)
            (corpus / "snapshot.json").write_text(json.dumps(manifest))
            result = module.observe(corpus, config_path, repository="lucianox777/Jornada",
                                    run_id=101, store_identity="mounted-ref")
            self.assertEqual(result["files"], manifest["files"])
            self.assertEqual(result["fingerprint"], manifest["fingerprint"])
            (corpus / "observacoes.csv").write_text("tampered", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "mismatch"):
                module.observe(corpus, config_path, repository="lucianox777/Jornada",
                               run_id=102, store_identity="mounted-ref")

    def test_observation_metadata_rejected_without_synthetic_second_run(self):
        receipt = observation(101)
        receipt["run_attempt"] = 2
        with self.assertRaisesRegex(ValueError, "Rerun"):
            module.validate_observation(receipt)

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

    def test_missing_file_metadata_rejected(self):
        second = observation(102)
        del second["files"]["gabarito.json"]
        with self.assertRaisesRegex(ValueError, "allowlist"):
            module.audit(observation(101), second)

    def test_malformed_sha_rejected(self):
        second = observation(102)
        second["files"]["observacoes.csv"]["sha256"] = "invalid"
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            module.audit(observation(101), second)

    def test_rerun_rejected(self):
        second = observation(102)
        second["run_attempt"] = 2
        with self.assertRaisesRegex(ValueError, "Rerun"):
            module.audit(observation(101), second)


if __name__ == "__main__":
    unittest.main()
