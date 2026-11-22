"""Contract-only offline tests; not proof of operational V9 parity."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-independent-decision-oracle.py"
spec = importlib.util.spec_from_file_location("decision_oracle", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DecisionOracleTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.truth = root / "truth.json"
        self.observed = root / "observed.json"
        self.expected = [
            {"observationId": "a", "decision": "MATCH", "identityId": "person-1"},
            {"observationId": "b", "decision": "ABSTAIN", "identityId": None},
            {"observationId": "c", "decision": "NO_MATCH", "identityId": None},
        ]
        self.write()

    def write(self, expected=None, observed=None, observed_digest=None):
        digest = "a" * 64
        self.truth.write_text(json.dumps({
            "zipSha256": digest,
            "expectedDecisions": self.expected if expected is None else expected,
        }), encoding="utf-8")
        self.observed.write_text(json.dumps({
            "zipSha256": digest if observed_digest is None else observed_digest,
            "observedDecisions": list(reversed(self.expected)) if observed is None else observed,
        }), encoding="utf-8")

    def test_order_independent_match_abstain_no_match(self):
        self.assertEqual(module.compare(self.truth, self.observed)["status"], "PASS")

    def test_divergent_identity_detected(self):
        rows = [dict(row) for row in self.expected]
        rows[0]["identityId"] = "person-2"
        self.write(observed=rows)
        self.assertEqual(module.compare(self.truth, self.observed)["divergentObservations"], ["a"])

    def test_divergent_abstention_detected(self):
        rows = [dict(row) for row in self.expected]
        rows[1] = {"observationId": "b", "decision": "NO_MATCH", "identityId": None}
        self.write(observed=rows)
        self.assertEqual(module.compare(self.truth, self.observed)["divergentObservations"], ["b"])

    def test_missing_and_unexpected_observations(self):
        rows = [dict(self.expected[0]), dict(self.expected[1]),
                {"observationId": "d", "decision": "NO_MATCH", "identityId": None}]
        self.write(observed=rows)
        receipt = module.compare(self.truth, self.observed)
        self.assertEqual(receipt["missingObservations"], ["c"])
        self.assertEqual(receipt["unexpectedObservations"], ["d"])

    def test_duplicate_observation_rejected(self):
        self.write(observed=[self.expected[0], self.expected[0]])
        with self.assertRaisesRegex(ValueError, "duplicate observationId"):
            module.compare(self.truth, self.observed)

    def test_identity_on_abstain_rejected(self):
        rows = [dict(row) for row in self.expected]
        rows[1]["identityId"] = "person-1"
        self.write(observed=rows)
        with self.assertRaisesRegex(ValueError, "non-MATCH"):
            module.compare(self.truth, self.observed)

    def test_missing_identity_on_match_rejected(self):
        rows = [dict(row) for row in self.expected]
        rows[0]["identityId"] = None
        self.write(observed=rows)
        with self.assertRaisesRegex(ValueError, "MATCH requires"):
            module.compare(self.truth, self.observed)

    def test_different_zip_rejected(self):
        self.write(observed_digest="b" * 64)
        with self.assertRaisesRegex(ValueError, "different ZIP"):
            module.compare(self.truth, self.observed)

    def test_duplicate_json_member_rejected(self):
        self.observed.write_text(
            '{"zipSha256":"' + "a" * 64 +
            '","observedDecisions":[],"observedDecisions":[]}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON"):
            module.compare(self.truth, self.observed)

    def test_nonfinite_json_rejected(self):
        self.truth.write_text('{"zipSha256":"' + "a" * 64 +
                              '","expectedDecisions":[],"x":NaN}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "nonfinite"):
            module.compare(self.truth, self.observed)

    def test_same_file_rejected(self):
        with self.assertRaisesRegex(ValueError, "separate"):
            module.compare(self.truth, self.truth)


if __name__ == "__main__":
    unittest.main()
