import importlib.util
from pathlib import Path
import tempfile
import types
import unittest

spec = importlib.util.spec_from_file_location("receipt", Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-input-receipt.py")
receipt = importlib.util.module_from_spec(spec)
spec.loader.exec_module(receipt)

class InputReceiptTests(unittest.TestCase):
    def test_reference_and_generator_change_invalidate(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for name in ("src", "ibge", "family"):
                (root / name).mkdir()
                (root / name / "x").write_text("v1")
            birth = root / "birth.json"
            birth.write_text("{}")
            args = types.SimpleNamespace(generator_source=root/"src", ibge_names=root/"ibge",
                birth_daily=birth, family_ref=root/"family", people=100000, seed=42,
                population_profile="demographic-primary", error_profile="clean")
            initial = receipt.make(args)
            self.assertEqual(initial, receipt.make(args))
            (root/"src"/"x").write_text("v2")
            self.assertNotEqual(initial, receipt.make(args))
            (root/"src"/"x").write_text("v1")
            (root/"family"/"x").write_text("v2")
            self.assertNotEqual(initial, receipt.make(args))

    def test_birth_reference_missing_or_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "src").mkdir()
            (root / "ibge").mkdir()
            (root / "src" / "x").write_text("source")
            (root / "ibge" / "x").write_text("reference")
            args = types.SimpleNamespace(generator_source=root / "src",
                ibge_names=root / "ibge", birth_daily=root / "missing.json",
                family_ref=None, people=100000, seed=42,
                population_profile="demographic-primary", error_profile="clean")
            with self.assertRaises(ValueError):
                receipt.make(args)
            (root / "actual.json").write_text("{}")
            (root / "linked.json").symlink_to(root / "actual.json")
            args.birth_daily = root / "linked.json"
            with self.assertRaises(ValueError):
                receipt.make(args)

    def test_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root/"original").write_text("x")
            (root/"linked").symlink_to(root/"original")
            with self.assertRaises(ValueError):
                receipt.tree(root)

if __name__ == "__main__":
    unittest.main()
