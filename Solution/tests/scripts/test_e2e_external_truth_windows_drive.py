"""Regression: external-truth ZIP boundary must reject Windows drive paths."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("external_truth_boundary_drive", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ZipDrivePathTests(unittest.TestCase):
    def check_path(self, name, reject):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive_path = root / "payload.zip"
            truth_path = root / "truth.json"
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr(name, b"synthetic-payload")
            truth_path.write_text(json.dumps({
                "zipSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
                "expectedIdentities": ["synthetic-A"],
            }), encoding="utf-8")
            if reject:
                with self.assertRaisesRegex(ValueError, "unsafe entry path"):
                    module.validate(archive_path, truth_path)
            else:
                self.assertEqual(module.validate(archive_path, truth_path)["status"], "PASS")

    def test_drive_letter_absolute_path_rejected(self):
        for name in ("C:/payload.txt", "z:/data/payload.txt", "D:relative.txt"):
            with self.subTest(name=name):
                self.check_path(name, True)

    def test_regular_relative_path_remains_accepted(self):
        self.check_path("synthetic/payload.txt", False)

    def test_colon_not_in_drive_prefix_remains_accepted(self):
        self.check_path("folder/file:part.txt", False)


if __name__ == "__main__":
    unittest.main()
