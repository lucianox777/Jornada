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

    def test_symlink_artifact_metadata_fails_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dest, config, receipt_path, _ = self.setup_corpus(root)
            symlink = root / "alias-config.json"
            symlink.symlink_to(config)
            with self.assertRaises(ValueError):
                verifier.verify_handoff(dest, symlink, receipt_path)

if __name__ == "__main__":
    unittest.main()
