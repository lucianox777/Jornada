"""Offline grouping partition contract tests, not productive V9 certification."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-independent-grouping-oracle.py"
spec = importlib.util.spec_from_file_location("grouping_oracle", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GroupingOracleTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.truth = root / "truth.json"
        self.observed = root / "observed.json"
        self.write()

    def write(self, expected=None, observed=None, digest=None):
        self.truth.write_text(json.dumps({
            "zipSha256": "a" * 64,
            "expectedGroups": [["A", "B"], ["C"]],
        } if expected is None else {
            "zipSha256": "a" * 64, "expectedGroups": expected,
        }), encoding="utf-8")
        self.observed.write_text(json.dumps({
            "zipSha256": "a" * 64 if digest is None else digest,
            "observedGroups": [["C"], ["B", "A"]] if observed is None else observed,
        }), encoding="utf-8")

    def test_order_independent_groups(self):
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "PASS")

    def test_merge_divergence_detected(self):
        self.write(observed=[["A", "B", "C"]])
        result = module.compare(self.truth, self.observed)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["missingObservations"], [])
        self.assertEqual(result["unexpectedObservations"], [])

    def test_split_divergence_detected(self):
        self.write(observed=[["A"], ["B"], ["C"]])
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "FAIL")

    def test_missing_observation_detected(self):
        self.write(observed=[["A", "B"]])
        self.assertEqual(module.compare(self.truth, self.observed)["missingObservations"], ["C"])

    def test_duplicate_observation_rejected(self):
        self.write(observed=[["A", "B"], ["B", "C"]])
        with self.assertRaisesRegex(ValueError, "duplicate observation"):
            module.compare(self.truth, self.observed)

    def test_empty_group_rejected(self):
        self.write(observed=[["A", "B"], []])
        with self.assertRaisesRegex(ValueError, "nonempty"):
            module.compare(self.truth, self.observed)

    def test_whitespace_member_rejected(self):
        self.write(observed=[["A", " B"], ["C"]])
        with self.assertRaisesRegex(ValueError, "invalid group member"):
            module.compare(self.truth, self.observed)

    def test_different_zip_rejected(self):
        self.write(digest="b" * 64)
        with self.assertRaisesRegex(ValueError, "different ZIP"):
            module.compare(self.truth, self.observed)

    def test_duplicate_json_member_rejected(self):
        self.truth.write_text('{"zipSha256":"' + "a" * 64 +
                              '","expectedGroups":[["A"]],"expectedGroups":[["B"]]}',
                              encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON"):
            module.compare(self.truth, self.observed)

    def test_nonfinite_json_rejected(self):
        self.observed.write_text('{"zipSha256":"' + "a" * 64 +
                                 '","observedGroups":[["A"]],"x":NaN}',
                                 encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "nonfinite"):
            module.compare(self.truth, self.observed)

    def test_same_file_rejected(self):
        with self.assertRaisesRegex(ValueError, "separate"):
            module.compare(self.truth, self.truth)


if __name__ == "__main__":
    unittest.main()
