"""Unit tests for the fail-closed PREPARE -> TRAIN handoff."""
import importlib.util
from pathlib import Path
import tarfile
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-standard-prepare-handoff.py"
spec = importlib.util.spec_from_file_location("v9_standard_prepare_handoff", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class PrepareHandoffTests(unittest.TestCase):
    def make_root(self, root):
        for name in mod.REQUIRED:
            p = root / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text("synthetic-fixture", encoding="utf-8")

    def test_roundtrip(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "prepare"
            root.mkdir()
            self.make_root(root)
            archive = Path(temp) / "handoff.tar.gz"
            mod.pack(root, archive)
            target = Path(temp) / "train"
            target.mkdir()
            mod.unpack(target, archive)
            for name in mod.REQUIRED:
                self.assertEqual((root / name).read_bytes(), (target / name).read_bytes())

    def test_reject_path_traversal(self):
        member = tarfile.TarInfo("../outside.txt")
        member.size = 4
        with self.assertRaisesRegex(ValueError, "UNSAFE"):
            mod.validate_members([member])

    def test_reject_symlink(self):
        member = tarfile.TarInfo("people-100k/observacoes.csv")
        member.type = tarfile.SYMTYPE
        with self.assertRaisesRegex(ValueError, "UNSAFE"):
            mod.validate_members([member])

    def test_reject_missing_required(self):
        with self.assertRaisesRegex(ValueError, "MISSING_REQUIRED"):
            mod.validate_members([tarfile.TarInfo("bound/plan.json")])

    def test_reject_duplicate(self):
        entries = [tarfile.TarInfo(name) for name in mod.REQUIRED]
        entries.append(tarfile.TarInfo(mod.REQUIRED[0]))
        with self.assertRaisesRegex(ValueError, "DUPLICATE"):
            mod.validate_members(entries)

    def test_reject_symlink_source(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_root(root)
            (root / "bound" / "link").symlink_to(root / "bound" / "plan.json")
            with self.assertRaisesRegex(ValueError, "UNSAFE_SOURCE"):
                mod.pack(root, root / "handoff.tar.gz")


if __name__ == "__main__":
    unittest.main()
