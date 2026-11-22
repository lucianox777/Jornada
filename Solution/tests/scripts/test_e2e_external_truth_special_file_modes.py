"""Reject nonregular Unix ZIP entries in the external truth boundary."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("e2e_truth_special_modes", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class E2ETruthSpecialModesTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.zip_path = self.root / "input.zip"
        self.truth_path = self.root / "truth.json"

    def make_zip(self, name, mode):
        info = zipfile.ZipInfo(name)
        info.create_system = 3
        info.external_attr = (mode << 16)
        with zipfile.ZipFile(self.zip_path, "w") as archive:
            archive.writestr(info, b"payload")
        self.truth_path.write_text(json.dumps({
            "zipSha256": hashlib.sha256(self.zip_path.read_bytes()).hexdigest(),
            "expectedIdentities": ["synthetic-A"],
        }))

    def test_reject_special_unix_modes(self):
        for mode in (0o010644, 0o020644, 0o060644, 0o140644):
            with self.subTest(mode=oct(mode)):
                self.make_zip("data.bin", mode)
                with self.assertRaisesRegex(ValueError, "nonregular Unix entry"):
                    module.validate(self.zip_path, self.truth_path)

    def test_reject_directory_mode_on_regular_name(self):
        self.make_zip("data.bin", 0o040755)
        with self.assertRaisesRegex(ValueError, "directory mode"):
            module.validate(self.zip_path, self.truth_path)

    def test_allow_regular_unix_file(self):
        self.make_zip("data.bin", 0o100644)
        self.assertEqual(module.validate(self.zip_path, self.truth_path)["status"], "PASS")

    def test_allow_untyped_zip_entry(self):
        self.make_zip("data.bin", 0o000644)
        self.assertEqual(module.validate(self.zip_path, self.truth_path)["status"], "PASS")


if __name__ == "__main__":
    unittest.main()
