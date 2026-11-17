#!/usr/bin/env python3
"""Regression for the scientifically valid zero-finalist E2E terminal outcome."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("v9_closeout", ROOT / "scripts/v9-tf4-e2e-closeout.py")
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class ZeroFinalistsCloseoutTests(unittest.TestCase):
    def call(self, **changes):
        with tempfile.TemporaryDirectory() as folder:
            kwargs = dict(physical="success", audit="success",
                          validation="skipped", preparation="skipped",
                          test="skipped", test_requested=False,
                          validation_file=Path(folder) / "nonexistent.json",
                          test_file=None)
            kwargs.update(changes)
            return module.closeout(**kwargs)

    def test_zero_finalists_success_without_test(self):
        result = self.call()
        self.assertEqual(result["status"], "COMPLETED_WITHOUT_ELIGIBLE_FINALISTS")
        self.assertEqual(result["validationFinalists"], 0)
        self.assertIsNone(result["winnerSha256"])
        self.assertFalse(result["certified"])

    def test_audit_failure_is_not_scientific_success(self):
        with self.assertRaises(ValueError):
            self.call(audit="failure")

    def test_test_exposure_is_not_allowed(self):
        with self.assertRaises(ValueError):
            self.call(test="success")

    def test_unexpected_validation_receipt_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            receipt = Path(folder) / "validation.json"
            receipt.write_text("{}")
            with self.assertRaises(ValueError):
                self.call(validation_file=receipt)


if __name__ == "__main__":
    unittest.main()
