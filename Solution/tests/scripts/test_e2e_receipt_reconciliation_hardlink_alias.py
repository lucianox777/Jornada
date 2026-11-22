"""Reject hardlink aliases in offline E2E receipt reconciliation."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-offline-receipt-reconciliation.py"
spec = importlib.util.spec_from_file_location("e2e_reconcile_hardlinks", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReceiptHardlinkTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.zip_path = self.root / "input.zip"
        self.zip_path.write_bytes(b"synthetic ZIP bytes")
        self.receipt = self.root / "receipt.json"
        self.receipt.write_text(json.dumps({
            "status": "PASS",
            "scope": "OFFLINE_IDENTITY_SET_ONLY_NOT_SCIENTIFIC_TEST",
            "zipSha256": hashlib.sha256(self.zip_path.read_bytes()).hexdigest(),
        }))

    def test_two_receipt_hardlinks_rejected(self):
        alias = self.root / "second.json"
        os.link(self.receipt, alias)
        with self.assertRaisesRegex(ValueError, "duplicate receipt path"):
            module.reconcile([self.receipt, alias])

    def test_zip_hardlink_to_receipt_rejected(self):
        alias = self.root / "zip-alias.zip"
        os.link(self.receipt, alias)
        with self.assertRaisesRegex(ValueError, "ZIP input must be separate"):
            module.reconcile([self.receipt], zip_path=alias)

    def run_main(self, output, zip_path=None):
        argv = ["reconcile", "--receipt", str(self.receipt), "--output", str(output)]
        if zip_path is not None:
            argv += ["--zip", str(zip_path)]
        with mock.patch("sys.argv", argv):
            module.main()

    def test_output_hardlink_to_receipt_rejected_without_mutation(self):
        output = self.root / "output.json"
        os.link(self.receipt, output)
        before = self.receipt.read_bytes()
        with self.assertRaisesRegex(ValueError, "output must be separate from input receipts"):
            self.run_main(output)
        self.assertEqual(self.receipt.read_bytes(), before)

    def test_output_hardlink_to_zip_rejected_without_mutation(self):
        output = self.root / "output.json"
        os.link(self.zip_path, output)
        before = self.zip_path.read_bytes()
        with self.assertRaisesRegex(ValueError, "output must be separate from ZIP input"):
            self.run_main(output, zip_path=self.zip_path)
        self.assertEqual(self.zip_path.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
