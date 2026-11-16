"""REF synthetic corpus namespace must never overlap official reference data."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-ref-store-path.py"
spec = importlib.util.spec_from_file_location("v9_ref_store_path", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RefStorePathTests(unittest.TestCase):
    def test_isolated_namespace_and_negative_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            official = root / "official"
            official.mkdir()
            synthetic = root / "synthetic" / "v9"
            self.assertEqual(module.validate(root, synthetic, official)["status"],
                             "REF_NAMESPACE_ISOLATION_PASS_NOT_DURABILITY")
            for invalid in (root, official, official / "corpus", root / "outside"):
                if invalid == root / "outside":
                    invalid = root.parent / "outside"
                with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                    module.validate(root, invalid, official)
            (root / "shortcut").symlink_to(official, target_is_directory=True)
            with self.assertRaises(ValueError):
                module.validate(root, root / "shortcut" / "corpus", official)


if __name__ == "__main__":
    unittest.main()
