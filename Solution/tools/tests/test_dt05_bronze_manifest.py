import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / "dt05_bronze_manifest.py"
spec = importlib.util.spec_from_file_location("dt05_bronze_manifest", MODULE)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class BronzeManifestTests(unittest.TestCase):
    def test_references_without_copy_and_detects_corruption(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            content = b"synthetic ZIP bytes"
            sha = hashlib.sha256(content).hexdigest()
            key = f"sha256/{sha[:2]}/{sha[2:4]}/{sha}.zip"
            obj = root / key
            obj.parent.mkdir(parents=True)
            obj.write_bytes(content)
            refs = root / "refs.json"
            refs.write_text(json.dumps([{"objeto_chave": key, "payload_sha256": sha}]))
            manifest = root / "linkage-snapshots" / "v1" / "manifests" / "run1.json"
            versions = {"scorer_version": "1", "ruleset_version": "1", "model_version": "1", "input_snapshot_id": "fixture"}
            self.assertEqual(mod.create(root, refs, manifest, "run1", versions), 1)
            self.assertEqual(mod.verify(root, manifest), 1)
            self.assertEqual(len(list(root.rglob("*.zip"))), 1)
            with self.assertRaises(FileExistsError):
                mod.create(root, refs, manifest, "run1", versions)
            obj.write_bytes(b"tampered")
            with self.assertRaises(ValueError):
                mod.verify(root, manifest)

    def test_rejects_noncanonical_key(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                mod.check_object(Path(directory), {"objeto_chave": "../escape.zip", "payload_sha256": "a" * 64})


if __name__ == "__main__":
    unittest.main()
