"""Atomic REF index promotion does not remove old scientific corpus versions."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

script = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-ref-promote.py"
spec = importlib.util.spec_from_file_location("ref_promote", script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

A, B = "a" * 64, "b" * 64


class RefPromotionTests(unittest.TestCase):
    def test_promotion_preserves_old_version_and_requires_approval(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "index.json"
            initial = {"schema": "v9-ref-retention-index-v1", "entries": [
                {"fingerprint": A, "configuration": "seed42", "state": "CURRENT", "validated": True, "approved": True},
                {"fingerprint": B, "configuration": "seed42", "state": "HISTORICAL", "validated": True, "approved": True},
            ]}
            path.write_text(json.dumps(initial))
            with self.assertRaisesRegex(ValueError, "approval"):
                module.promote(path, B, "seed42", approved=False)
            self.assertEqual(json.loads(path.read_text()), initial)
            self.assertEqual(module.promote(path, B, "seed42", approved=True)["status"], "PROMOTED_NO_DELETION")
            states = {e["fingerprint"]: e["state"] for e in json.loads(path.read_text())["entries"]}
            self.assertEqual(states, {A: "PREVIOUS", B: "CURRENT"})
            self.assertEqual(module.promote(path, B, "seed42", approved=True)["status"], "ALREADY_CURRENT")

    def test_unvalidated_target_rejected_without_mutation(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "index.json"
            initial = {"schema": "v9-ref-retention-index-v1", "entries": [
                {"fingerprint": A, "configuration": "seed42", "state": "CURRENT", "validated": True, "approved": True},
                {"fingerprint": B, "configuration": "seed42", "state": "HISTORICAL", "validated": False, "approved": False},
            ]}
            path.write_text(json.dumps(initial))
            with self.assertRaisesRegex(ValueError, "validated and approved"):
                module.promote(path, B, "seed42", approved=True)
            self.assertEqual(json.loads(path.read_text()), initial)


if __name__ == "__main__":
    unittest.main()
