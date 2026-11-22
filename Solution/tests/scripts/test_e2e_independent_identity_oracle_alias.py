"""Fail-closed hardlink and output-alias regressions for the offline identity oracle.

Not physical E2E, production authorization, or scientific certification.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-independent-identity-oracle.py"
spec = importlib.util.spec_from_file_location("e2e_identity_oracle_alias", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class IdentityOracleAliasTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.zip = self.root / "fixture.zip"
        self.zip.write_bytes(b"synthetic-fixture")
        digest = hashlib.sha256(self.zip.read_bytes()).hexdigest()
        self.truth = self.root / "truth.json"
        self.truth.write_text(json.dumps({
            "zipSha256": digest, "expectedIdentities": ["A"]
        }), encoding="utf-8")
        self.observed = self.root / "observed.json"
        self.observed.write_text(json.dumps({
            "zipSha256": digest, "observedIdentities": ["A"]
        }), encoding="utf-8")

    def run_main(self, output):
        argv = ["oracle", "--truth", str(self.truth), "--observed",
                str(self.observed), "--zip", str(self.zip),
                "--receipt", str(output)]
        with mock.patch("sys.argv", argv):
            module.main()

    def test_truth_and_observed_hardlink_rejected_before_parse(self):
        alias = self.root / "observed-alias.json"
        os.link(self.truth, alias)
        with self.assertRaisesRegex(ValueError, "separate files"):
            module.compare(self.truth, alias)

    def test_zip_hardlink_to_observed_is_rejected(self):
        alias = self.root / "zip-alias.zip"
        os.link(self.observed, alias)
        with self.assertRaisesRegex(ValueError, "physical ZIP must be separate"):
            module.compare(self.truth, self.observed, alias)

    def test_receipt_hardlink_to_truth_rejected_without_mutation(self):
        output = self.root / "truth-output.json"
        os.link(self.truth, output)
        before = self.truth.read_bytes()
        with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
            self.run_main(output)
        self.assertEqual(self.truth.read_bytes(), before)

    def test_receipt_hardlink_to_observed_rejected_without_mutation(self):
        output = self.root / "observed-output.json"
        os.link(self.observed, output)
        before = self.observed.read_bytes()
        with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
            self.run_main(output)
        self.assertEqual(self.observed.read_bytes(), before)

    def test_receipt_hardlink_to_zip_rejected_without_mutation(self):
        output = self.root / "zip-output.json"
        os.link(self.zip, output)
        before = self.zip.read_bytes()
        with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
            self.run_main(output)
        self.assertEqual(self.zip.read_bytes(), before)

    def test_receipt_symlink_to_truth_rejected(self):
        output = self.root / "truth-symlink.json"
        output.symlink_to(self.truth)
        with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
            self.run_main(output)

    def test_receipt_symlink_to_unrelated_file_rejected_without_mutation(self):
        target = self.root / "unrelated-target.txt"
        target.write_text("do-not-overwrite", encoding="utf-8")
        output = self.root / "unrelated-link.json"
        output.symlink_to(target)
        with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
            self.run_main(output)
        self.assertEqual(target.read_text(encoding="utf-8"), "do-not-overwrite")

    def test_receipt_dangling_symlink_rejected_without_creating_target(self):
        target = self.root / "not-yet-created.json"
        output = self.root / "dangling-link.json"
        output.symlink_to(target)
        with self.assertRaisesRegex(ValueError, "receipt output must be separate"):
            self.run_main(output)
        self.assertFalse(target.exists())

    def test_distinct_receipt_preserves_inputs(self):
        output = self.root / "receipt.json"
        before = (self.truth.read_bytes(), self.observed.read_bytes(), self.zip.read_bytes())
        self.run_main(output)
        self.assertEqual(json.loads(output.read_text())["status"], "PASS")
        self.assertEqual(before, (self.truth.read_bytes(), self.observed.read_bytes(),
                                  self.zip.read_bytes()))


if __name__ == "__main__":
    unittest.main()
