import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2] / "scripts"

def load(path, name):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

snapshot = load("v9-corpus-snapshot.py", "handoff_snapshot")
prepare = load("v9-corpus-prepare.py", "handoff_prepare")
verifier = load("v9-corpus-handoff-verify.py", "handoff_verify")

class CrossRunnerHandoffTests(unittest.TestCase):
    def setup_corpus(self, root):
        source = root / "generated"
        source.mkdir()
        for file in snapshot.REQUIRED:
            (source / file).write_text("synthetic-training-fixture-" + file, encoding="utf-8")
        config = {"seed": 42, "people": 100, "error_profile": "clean"}
        prepared = snapshot.publish(root / "store", config, source)
        # This fixture publishes directly; normal PREPARE creates the lock.
        (root / "store" / ".prepare.lock").touch(mode=0o600)
        dest = root / "other-runner" / "train-handoff"
        result = prepare.handoff(root / "store", config, dest)
        self.assertEqual(result["status"], "HANDOFF_VERIFIED")
        meta = dest.parent / "config.json"
        meta.write_text(json.dumps(config), encoding="utf-8")
        receipt_path = dest.parent / "prepare-receipt.json"
        receipt = {"status": "GENERATED", "fingerprint": prepared["fingerprint"],
                   "files": prepared["files"], "handoff": result}
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        return dest, meta, receipt_path, receipt

    def test_verified_transfer_is_hash_bound_with_no_test_consumption(self):
        with tempfile.TemporaryDirectory() as temp:
            dest, config, receipt_path, _ = self.setup_corpus(Path(temp))
            evidence = verifier.verify_handoff(dest, config, receipt_path)
            self.assertEqual(evidence["status"], "TRANSFER_VERIFIED_NONSCIENTIFIC")
            self.assertEqual(evidence["file_count"], 4)
            self.assertIs(evidence["scientific_test_used"], False)

    def test_mutated_contents_or_snapshot_rejected(self):
        for name in ("observacoes.csv", "gabarito.json", "snapshot.json"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temp:
                dest, config, receipt_path, _ = self.setup_corpus(Path(temp))
                (dest / name).write_text("corrupted", encoding="utf-8")
                with self.assertRaises((ValueError, json.JSONDecodeError)):
                    verifier.verify_handoff(dest, config, receipt_path)

    def test_forged_hash_mismatched_config_or_missing_receipt_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            dest, config, receipt_path, receipt = self.setup_corpus(Path(temp))
            receipt["fingerprint"] = "0" * 64
            receipt_path.write_text(json.dumps(receipt))
            with self.assertRaises(ValueError):
                verifier.verify_handoff(dest, config, receipt_path)
            receipt["fingerprint"] = receipt["handoff"]["fingerprint"]
            receipt["files"]["observacoes.csv"]["sha256"] = "b" * 64
            receipt_path.write_text(json.dumps(receipt))
            with self.assertRaises(ValueError):
                verifier.verify_handoff(dest, config, receipt_path)
            receipt_path.unlink()
            with self.assertRaises(ValueError):
                verifier.verify_handoff(dest, config, receipt_path)

    def test_reused_receipt_accepted_but_invalid_status_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            dest, config, receipt_path, receipt = self.setup_corpus(Path(temp))
            receipt["status"] = "REUSED"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            self.assertEqual(verifier.verify_handoff(dest, config, receipt_path)["file_count"], 4)
            for invalid in ("UNKNOWN", "TEST", None):
                with self.subTest(status=invalid):
                    receipt["status"] = invalid
                    receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        verifier.verify_handoff(dest, config, receipt_path)

    def test_symlink_corpus_payload_fails_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dest, config, receipt_path, _ = self.setup_corpus(root)
            payload = dest / "observacoes.csv"
            payload.unlink()
            payload.symlink_to(dest / "gabarito.json")
            with self.assertRaises(ValueError):
                verifier.verify_handoff(dest, config, receipt_path)

    def test_symlink_artifact_metadata_fails_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dest, config, receipt_path, _ = self.setup_corpus(root)
            symlink = root / "alias-config.json"
            symlink.symlink_to(config)
            with self.assertRaises(ValueError):
                verifier.verify_handoff(dest, symlink, receipt_path)

    def test_materialize_train_input_validates_and_copies_only_snapshot(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus, config, receipt, _ = self.setup_corpus(root)
            target = root / "train" / "people-100k"
            evidence = verifier.materialize_train_input(corpus, config, receipt, target)
            self.assertEqual(evidence["status"], "TRAIN_INPUT_ADMITTED_NOT_EXECUTED")
            self.assertEqual({p.name for p in target.iterdir()}, set(snapshot.REQUIRED) | {"snapshot.json"})
            self.assertEqual(snapshot.verify(target, json.loads(config.read_text()))["fingerprint"], evidence["fingerprint"])
            with self.assertRaises(ValueError):
                verifier.materialize_train_input(corpus, config, receipt, target)

    def test_materialize_train_input_rejects_corruption_without_destination(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus, config, receipt, _ = self.setup_corpus(root)
            (corpus / "observacoes.csv").write_text("tampered", encoding="utf-8")
            target = root / "train" / "people-100k"
            with self.assertRaises(ValueError):
                verifier.materialize_train_input(corpus, config, receipt, target)
            self.assertFalse(target.exists())

    def test_materialize_train_input_rejects_symlink_destination(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus, config, receipt, _ = self.setup_corpus(root)
            target = root / "train-link"
            target.symlink_to(corpus, target_is_directory=True)
            with self.assertRaises(ValueError):
                verifier.materialize_train_input(corpus, config, receipt, target)

    def test_train_admission_rejects_wrong_expected_fingerprint(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus, config, receipt, _ = self.setup_corpus(root)
            target = root / "train" / "people-100k"
            with self.assertRaisesRegex(ValueError, "fingerprint mismatch"):
                verifier.materialize_train_input(corpus, config, receipt, target, "0" * 64)
            self.assertFalse(target.exists())
            actual = snapshot.identity(json.loads(config.read_text()))
            evidence = verifier.materialize_train_input(corpus, config, receipt, target, actual)
            self.assertEqual(evidence["fingerprint"], actual)

if __name__ == "__main__":
    unittest.main()
