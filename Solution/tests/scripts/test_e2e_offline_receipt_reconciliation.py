"""Offline receipt reconciliation tests; no certification of operational E2E."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-offline-receipt-reconciliation.py"
spec = importlib.util.spec_from_file_location("receipt_reconciliation", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

IDENTITY = "OFFLINE_IDENTITY_SET_ONLY_NOT_SCIENTIFIC_TEST"
DECISION = "OFFLINE_DECISION_ROWS_ONLY_NOT_RUNNER_PARITY_OR_SCIENTIFIC_TEST"


class ReceiptReconciliationTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.a, self.b = root / "a.json", root / "b.json"
        self.write(self.a, IDENTITY)
        self.write(self.b, DECISION)

    def write(self, path, scope, status="PASS", digest="a" * 64):
        path.write_text(json.dumps({
            "status": status, "scope": scope, "zipSha256": digest,
        }), encoding="utf-8")

    def test_distinct_scopes_same_zip_pass(self):
        result = module.reconcile([self.a, self.b])
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["receiptCount"], 2)
        self.assertEqual(result["verifiedScopes"], sorted([IDENTITY, DECISION]))

    def test_different_zip_rejected(self):
        self.write(self.b, DECISION, digest="b" * 64)
        with self.assertRaisesRegex(ValueError, "different ZIP"):
            module.reconcile([self.a, self.b])

    def test_failed_receipt_rejected(self):
        self.write(self.b, DECISION, status="FAIL")
        with self.assertRaisesRegex(ValueError, "not PASS"):
            module.reconcile([self.a, self.b])

    def test_unknown_scope_rejected(self):
        self.write(self.b, "CERTIFIED")
        with self.assertRaisesRegex(ValueError, "unrecognized"):
            module.reconcile([self.a, self.b])

    def test_duplicate_scope_rejected(self):
        self.write(self.b, IDENTITY)
        with self.assertRaisesRegex(ValueError, "duplicate receipt scope"):
            module.reconcile([self.a, self.b])

    def test_duplicate_file_rejected(self):
        with self.assertRaisesRegex(ValueError, "duplicate receipt path"):
            module.reconcile([self.a, self.a])

    def test_empty_input_rejected(self):
        with self.assertRaisesRegex(ValueError, "at least one"):
            module.reconcile([])

    def test_invalid_digest_rejected(self):
        self.write(self.b, DECISION, digest="bad")
        with self.assertRaisesRegex(ValueError, "SHA256"):
            module.reconcile([self.a, self.b])

    def test_duplicate_json_member_rejected(self):
        self.b.write_text('{"status":"PASS","status":"FAIL"}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON"):
            module.reconcile([self.a, self.b])

    def test_nonfinite_json_rejected(self):
        self.b.write_text('{"status":"PASS","x":NaN}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "nonfinite"):
            module.reconcile([self.a, self.b])


if __name__ == "__main__":
    unittest.main()
