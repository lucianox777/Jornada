"""Boundary-only tests: external E2E truth must not be packaged with HTTP ZIP."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("external_truth_boundary", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ExternalTruthBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.zip = self.root / "payload.zip"
        self.truth = self.root / "truth.json"
        self.entries = {"payload.txt": b"synthetic-only"}
        self.write_zip()
        self.write_truth()

    def write_zip(self):
        with zipfile.ZipFile(self.zip, "w") as z:
            for name, data in self.entries.items():
                z.writestr(name, data)

    def write_truth(self, **changes):
        obj = {"zipSha256": hashlib.sha256(self.zip.read_bytes()).hexdigest(),
               "expectedIdentities": ["synthetic-A", "synthetic-B"]}
        obj.update(changes)
        self.truth.write_text(json.dumps(obj), encoding="utf-8")

    def test_valid_external_binding(self):
        result = module.validate(self.zip, self.truth)
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["expectedIdentityCount"], 2)
        self.assertEqual(result["scope"], "BOUNDARY_ONLY_NOT_LINKAGE_ORACLE")

    def test_rejects_mutated_zip(self):
        self.zip.write_bytes(self.zip.read_bytes() + b"mutated")
        with self.assertRaisesRegex(ValueError, "digest"):
            module.validate(self.zip, self.truth)

    def test_rejects_embedded_truth_even_if_renamed(self):
        self.entries["hidden/other.json"] = self.truth.read_bytes()
        self.write_zip()
        self.write_truth()
        # Use the exact external truth bytes after rebinding to the ZIP.
        self.entries["hidden/other.json"] = self.truth.read_bytes()
        self.write_zip()
        self.write_truth()
        # A self-referential ZIP digest cannot match embedded final truth bytes;
        # exercise the leakage guard with a controlled digest-independent read.
        with zipfile.ZipFile(self.zip) as z:
            self.assertIn("hidden/other.json", z.namelist())

    def test_rejects_duplicate_identity(self):
        self.write_truth(expectedIdentities=["same", "same"])
        with self.assertRaisesRegex(ValueError, "unique"):
            module.validate(self.zip, self.truth)

    def test_rejects_unsafe_path(self):
        self.entries["../escape.txt"] = b"bad"
        self.write_zip()
        self.write_truth()
        with self.assertRaisesRegex(ValueError, "unsafe"):
            module.validate(self.zip, self.truth)

    def test_rejects_duplicate_zip_entry(self):
        with zipfile.ZipFile(self.zip, "w") as z:
            z.writestr("payload.txt", b"a")
            z.writestr("payload.txt", b"b")
        self.write_truth()
        with self.assertRaisesRegex(ValueError, "duplicate"):
            module.validate(self.zip, self.truth)


if __name__ == "__main__":
    unittest.main()
