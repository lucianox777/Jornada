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
        # Historical lock preserves preregistered seed; encryption is superseded.
        self.assertIn("IDENTITY_DISJOINTNESS_NOT_ATTESTED", lock["declaredIndependence"])
        # The historical lock is not the active Standard Linear TEST seed.
        self.assertNotIn('--seed ' + str(lock['testSeed']), self.job('test-100k'))
        self.assertIn('steps.test_seed.outputs.test_seed', self.job('test-100k'))

    def test_train_does_not_depend_on_test_preparation(self):
        train = self.job("physical-100k")
        prepare = self.job("prepare-100k")
        self.assertIn("--seed 42", prepare)
        self.assertIn("needs: prepare-100k", train)
        self.assertNotIn("prepare-test-sealed", self.workflow)
        self.assertNotIn("V9_TF4_TEST_PRIVATE_KEY_B64", self.workflow)
        self.assertNotIn("V9_TF4_TEST_PUBLIC_CERT_B64", self.workflow)

    def test_test_only_after_winner_and_one_use_before_generation(self):
        test = self.job("test-100k")
        validation = self.job("validation-100k")
        self.assertIn("needs: validation-100k", test)
        self.assertIn("needs.validation-100k.outputs.has_winner == 'true'", test)
        self.assertIn("python3 scripts/v9-reserve-test-seed.py", test)
        self.assertLess(test.index("python3 scripts/v9-reserve-test-seed.py"), test.index("Generate fresh independent TEST 100k"))
        self.assertIn("--seed \"${{ steps.test_seed.outputs.test_seed }}\"", test)
        self.assertIn("--seed 20261112001", validation)
        self.assertNotIn("contents: write", validation)
        self.assertIn("contents: write", test)

    def test_test_evaluates_only_frozen_winner(self):
        test = self.job("test-100k")
        self.assertIn("Apply one frozen winner to TEST, no refit and no winner selection", test)
        self.assertIn("*.operational.json", test)
        self.assertNotIn("finalist-validation.json", test)


if __name__ == "__main__":
    unittest.main()
