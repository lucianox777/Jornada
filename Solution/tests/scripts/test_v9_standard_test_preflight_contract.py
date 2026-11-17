"""Fail-closed static preflight for the V9 standard TEST scientific isolation."""
from pathlib import Path
import hashlib
import json
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-standard-linear-100k.yml"
LOCK = ROOT / "config/calibration/v9-tf4-100k-test-preparation-lock.json"


class StandardTestPreflightContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.lock_bytes = LOCK.read_bytes()
        cls.lock = json.loads(cls.lock_bytes)

    def job(self, name):
        return re.split(r"(?m)^  [a-z][a-z0-9-]*:\s*$", self.workflow.split("\n  " + name + ":\n", 1)[1], maxsplit=1)[0]

    def test_three_distinct_preregistered_seeds_and_locked_hash(self):
        lock = self.lock
        self.assertEqual(lock["status"], "LOCKED")
        self.assertEqual(lock["people"], 100000)
        self.assertEqual(len({lock["trainSeed"], lock["validationSeed"], lock["testSeed"]}), 3)
        self.assertEqual(lock["sealing"], "OPENSSL_CMS_AES_256_GCM_X509_RSA")
        self.assertIn("IDENTITY_DISJOINTNESS_NOT_ATTESTED", lock["declaredIndependence"])
        self.assertIn(hashlib.sha256(self.lock_bytes).hexdigest(), self.workflow)

    def test_test_preparation_blocks_train_and_requires_cert(self):
        prep = self.job("prepare-test-sealed")
        train = self.job("physical-100k")
        self.assertIn("needs: admission", prep)
        self.assertIn("V9_TF4_TEST_PUBLIC_CERT_B64", prep)
        self.assertIn("V9_TF4_TEST_PUBLIC_CERT_SHA256", prep)
        self.assertIn("prepare-test-sealed", train)
        self.assertNotIn("V9_TF4_TEST_PRIVATE_KEY_B64", train)

    def test_test_only_after_winner_and_one_use_before_unseal(self):
        test = self.job("test-100k")
        validation = self.job("validation-100k")
        self.assertIn("needs: [validation-100k, prepare-test-sealed]", test)
        self.assertIn("needs.validation-100k.outputs.has_winner == 'true'", test)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', test)
        self.assertIn("refs/tags/v9-tf4-test-seed-20261113001-exposed", test)
        self.assertLess(test.index('gh api "repos/$GITHUB_REPOSITORY/git/refs"'), test.index(" unseal \\"))
        self.assertIn("V9_TF4_TEST_PRIVATE_KEY_B64", test)
        self.assertNotIn("V9_TF4_TEST_PRIVATE_KEY_B64", validation)
        self.assertNotIn("contents: write", validation)
        self.assertIn("contents: write", test)

    def test_test_evaluates_only_frozen_winner(self):
        test = self.job("test-100k")
        self.assertIn("Apply one frozen winner to TEST, no refit and no winner selection", test)
        self.assertIn("*.operational.json", test)
        self.assertNotIn("finalist-validation.json", test)


if __name__ == "__main__":
    unittest.main()
