"""Offline edge-set contract tests; not evidence of productive Runner parity."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-independent-candidate-edge-oracle.py"
spec = importlib.util.spec_from_file_location("edge_oracle", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CandidateEdgeOracleTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.truth = root / "truth.json"
        self.observed = root / "observed.json"
        self.write()

    def write(self, expected=None, observed=None, observed_digest=None):
        self.truth.write_text(json.dumps({
            "zipSha256": "a" * 64,
            "expectedEdges": [["A", "B"], ["B", "C"]] if expected is None else expected,
        }), encoding="utf-8")
        self.observed.write_text(json.dumps({
            "zipSha256": "a" * 64 if observed_digest is None else observed_digest,
            "observedEdges": [["C", "B"], ["B", "A"]] if observed is None else observed,
        }), encoding="utf-8")

    def test_order_and_orientation_independent(self):
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "PASS")

    def test_missing_and_unexpected_edges(self):
        self.write(observed=[["B", "A"], ["C", "D"]])
        result = module.compare(self.truth, self.observed)
        self.assertEqual(result["missingEdges"], [["B", "C"]])
        self.assertEqual(result["unexpectedEdges"], [["C", "D"]])
        self.assertEqual(result["status"], "FAIL")

    def test_empty_edge_sets_are_valid(self):
        self.write(expected=[], observed=[])
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "PASS")

    def test_duplicate_reverse_edge_rejected(self):
        self.write(observed=[["A", "B"], ["B", "A"]])
        with self.assertRaisesRegex(ValueError, "duplicate undirected"):
            module.compare(self.truth, self.observed)

    def test_self_edge_rejected(self):
        self.write(observed=[["A", "A"]])
        with self.assertRaisesRegex(ValueError, "self-edge"):
            module.compare(self.truth, self.observed)

    def test_invalid_endpoint_rejected(self):
        self.write(observed=[["A", " B"]])
        with self.assertRaisesRegex(ValueError, "endpoint"):
            module.compare(self.truth, self.observed)

    def test_invalid_edge_shape_rejected(self):
        self.write(observed=[["A", "B", "C"]])
        with self.assertRaisesRegex(ValueError, "pair"):
            module.compare(self.truth, self.observed)

    def test_different_zip_rejected(self):
        self.write(observed_digest="b" * 64)
        with self.assertRaisesRegex(ValueError, "different ZIP"):
            module.compare(self.truth, self.observed)

    def test_duplicate_json_member_rejected(self):
        self.truth.write_text('{"zipSha256":"' + "a" * 64 +
                              '","expectedEdges":[],"expectedEdges":[]}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON"):
            module.compare(self.truth, self.observed)

    def test_nonfinite_json_rejected(self):
        self.observed.write_text('{"zipSha256":"' + "a" * 64 +
                                 '","observedEdges":[],"x":NaN}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "nonfinite"):
            module.compare(self.truth, self.observed)

    def test_same_file_rejected(self):
        with self.assertRaisesRegex(ValueError, "separate"):
            module.compare(self.truth, self.truth)


if __name__ == "__main__":
    unittest.main()
