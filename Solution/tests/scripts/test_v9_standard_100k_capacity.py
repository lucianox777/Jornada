"""Scope, capacity and science constraints for the standard 100k runner gates."""
import importlib.util
import unittest
from pathlib import Path

SOLUTION = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("v9_std_capacity", SOLUTION / "scripts/v9-standard-100k-capacity.py")
capacity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capacity)
G = capacity.GIB


class Standard100kResourceTests(unittest.TestCase):
    def test_standard_host_admission_is_not_physical_certification(self):
        before = capacity.decide("before-sql", 15 * G, 12 * G, 9 * G)
        self.assertEqual(before["status"], "PASS_ADMISSION_ONLY_NOT_PHYSICAL_PROOF")
        self.assertFalse(before["testUsed"])
        self.assertFalse(before["certified"])
        self.assertFalse(before["activated"])
        remaining = capacity.decide("before-calibration", 15 * G, 5 * G, 4 * G)
        self.assertTrue(remaining["status"].startswith("PASS"))

    def test_insufficient_remaining_memory_and_disk_fail_closed(self):
        for values in (("before-sql", 11 * G, 10 * G, 9 * G),
                       ("before-sql", 15 * G, 10 * G, 5 * G),
                       ("before-calibration", 15 * G, 3 * G, 5 * G),
                       ("before-calibration", 15 * G, 5 * G, 2 * G)):
            with self.subTest(values=values):
                self.assertEqual(capacity.decide(*values)["status"],
                                 "FAIL_CLOSED_INSUFFICIENT_RESOURCES")

    def test_invalid_samples_and_manual_security_sha_guards(self):
        with self.assertRaises(ValueError):
            capacity.decide("preflight", 20, 20, 20)
        with self.assertRaises(ValueError):
            capacity.decide("before-sql", -1, 10, 10)
        workflow = (SOLUTION.parent / ".github/workflows/v9-multiparameter-100k-technical.yml").read_text()
        self.assertIn("runs-on: ubuntu-24.04", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertIn("security_preflight_approved", workflow)
        self.assertIn("github.actor == github.repository_owner", workflow)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', workflow)
        self.assertIn("--v9-calibrate", workflow)
        self.assertIn("v9-scale-100k-technical-audit.py", workflow)
        bootstrap = (SOLUTION / "scripts/v9-tf-start-disposable-sql.sh").read_text()
        self.assertIn("v9-100k-execution-resource-budget.py", bootstrap)
        self.assertIn("--stage plan-sql", bootstrap)


if __name__ == "__main__":
    unittest.main()
