"""Hardlink aliases must not defeat offline E2E input/output separation."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("e2e_truth_hardlink", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class E2ETruthHardlinkTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.zip_path = self.root / "payload.zip"
        self.zip_path.write_bytes(b"not-a-zip")
        self.truth_path = self.root / "truth.json"
        self.truth_path.write_text('{"zipSha256":"x","expectedIdentities":["A"]}')

    def test_hardlinked_truth_and_zip_rejected_before_parse(self):
        alias = self.root / "truth-hardlink.json"
        os.link(self.zip_path, alias)
        with self.assertRaisesRegex(ValueError, "distinct files"):
            module.validate(self.zip_path, alias)

    def test_receipt_hardlink_to_zip_rejected_before_validation(self):
        receipt = self.root / "receipt.json"
        os.link(self.zip_path, receipt)
        with mock.patch("sys.argv", ["preflight", "--zip", str(self.zip_path),
                                     "--truth", str(self.truth_path), "--receipt", str(receipt)]):
            with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
                module.main()
        self.assertEqual(self.zip_path.read_bytes(), b"not-a-zip")

    def test_receipt_hardlink_to_truth_rejected_before_validation(self):
        receipt = self.root / "receipt.json"
        os.link(self.truth_path, receipt)
        before = self.truth_path.read_bytes()
        with mock.patch("sys.argv", ["preflight", "--zip", str(self.zip_path),
                                     "--truth", str(self.truth_path), "--receipt", str(receipt)]):
            with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
                module.main()
        self.assertEqual(self.truth_path.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
