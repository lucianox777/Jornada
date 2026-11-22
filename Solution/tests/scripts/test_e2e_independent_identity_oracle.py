"""Offline contract tests; not a substitute for physical E2E or scientific TEST."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-independent-identity-oracle.py"
spec = importlib.util.spec_from_file_location("e2e_identity_oracle", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class IdentityOracleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.truth, self.observed = root / "truth.json", root / "observed.json"
        self.write()

    def write(self, expected=None, observed=None, truth_digest="a" * 64, observed_digest=None):
        self.truth.write_text(json.dumps({"zipSha256": truth_digest, "expectedIdentities":
                                           ["A", "B"] if expected is None else expected}), encoding="utf-8")
        self.observed.write_text(json.dumps({"zipSha256": truth_digest if observed_digest is None else observed_digest,
                                              "observedIdentities": ["B", "A"] if observed is None else observed}),
                                 encoding="utf-8")

    def test_order_independent_exact_match(self):
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "PASS")

    def test_missing_and_unexpected_fail(self):
        self.write(observed=["B", "C"])
        receipt = module.compare(self.truth, self.observed)
        self.assertEqual(receipt["status"], "FAIL")
        self.assertEqual(receipt["missingIdentities"], ["A"])
        self.assertEqual(receipt["unexpectedIdentities"], ["C"])

    def test_empty_observed_fails(self):
        self.write(observed=[])
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "FAIL")

    def test_duplicate_observed_rejected(self):
        self.write(observed=["A", "A"])
        with self.assertRaisesRegex(ValueError, "duplicate"):
            module.compare(self.truth, self.observed)

    def test_duplicate_expected_rejected(self):
        self.write(expected=["A", "A"])
        with self.assertRaisesRegex(ValueError, "duplicate"):
            module.compare(self.truth, self.observed)

    def test_different_zip_rejected(self):
        self.write(observed_digest="b" * 64)
        with self.assertRaisesRegex(ValueError, "different ZIP"):
            module.compare(self.truth, self.observed)

    def test_invalid_sha_rejected(self):
        self.write(truth_digest="bad")
        with self.assertRaisesRegex(ValueError, "SHA256"):
            module.compare(self.truth, self.observed)

    def test_extra_field_rejected(self):
        self.observed.write_text(json.dumps({"zipSha256": "a" * 64, "observedIdentities": ["A", "B"],
                                             "model": "untrusted"}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "contract"):
            module.compare(self.truth, self.observed)

    def test_duplicate_truth_member_rejected(self):
        self.truth.write_text(
            '{"zipSha256":"' + "a" * 64 + '","expectedIdentities":["A"],'
            '"expectedIdentities":["B"]}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON member"):
            module.compare(self.truth, self.observed)

    def test_duplicate_observed_member_rejected(self):
        self.observed.write_text(
            '{"zipSha256":"' + "a" * 64 + '","observedIdentities":["A"],'
            '"observedIdentities":["B"]}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON member"):
            module.compare(self.truth, self.observed)

    def test_nonfinite_observed_member_rejected(self):
        self.observed.write_text(
            '{"zipSha256":"' + "a" * 64 + '","observedIdentities":["A"],'
            '"untrusted":NaN}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "nonfinite JSON"):
            module.compare(self.truth, self.observed)

    def test_same_file_rejected(self):
        with self.assertRaisesRegex(ValueError, "separate"):
            module.compare(self.truth, self.truth)


if __name__ == "__main__":
    unittest.main()
