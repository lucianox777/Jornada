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

snapshot_spec = importlib.util.spec_from_file_location("v9_snapshot", script.parent / "v9-corpus-snapshot.py")
snapshot = importlib.util.module_from_spec(snapshot_spec)
snapshot_spec.loader.exec_module(snapshot)
CONFIG_A = {"seed": 42}
CONFIG_B = {"seed": 43}
A, B = snapshot.identity(CONFIG_A), snapshot.identity(CONFIG_B)


def make_store(root):
    store = root / "store"
    store.mkdir()
    for config in (CONFIG_A, CONFIG_B):
        target = store / snapshot.identity(config)
        target.mkdir()
        for name in snapshot.REQUIRED:
            (target / name).write_text("fixture:" + name)
        (target / "snapshot.json").write_text(json.dumps(snapshot.snapshot(config, target)))
    return store


class RefPromotionTests(unittest.TestCase):
    def test_promotion_preserves_old_version_and_requires_validated_target(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            store = make_store(root)
            path = root / "index.json"
            initial = {"schema": "v9-ref-retention-index-v1", "entries": [
                {"fingerprint": A, "configuration": "seed42", "state": "CURRENT", "validated": True, "approved": True},
                {"fingerprint": B, "configuration": "seed42", "state": "HISTORICAL", "validated": True, "approved": True},
            ]}
            path.write_text(json.dumps(initial))
            self.assertEqual(module.promote(path, B, "seed42", store=store, generation=CONFIG_B)["status"], "PROMOTED_NO_DELETION")
            states = {e["fingerprint"]: e["state"] for e in json.loads(path.read_text())["entries"]}
            self.assertEqual(states, {A: "PREVIOUS", B: "CURRENT"})
            self.assertEqual(module.promote(path, B, "seed42", store=store, generation=CONFIG_B)["status"], "ALREADY_CURRENT")

    def test_production_auto_promotion_is_forbidden(self):
        with self.assertRaisesRegex(ValueError, "production requires manual PO approval"):
            module.promote(Path("/unused/index"), B, "seed42", environment="production")

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
