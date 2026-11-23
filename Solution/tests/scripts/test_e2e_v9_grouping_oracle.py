"""Offline, noncertifying grouping-oracle regressions (#1851/#1584)."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "v9_grouping_oracle", ROOT / "scripts/e2e-v9-grouping-oracle.py")
ORACLE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ORACLE)
COMMIT = "c" * 40
WINNER = "a" * 64


class GroupingOracleTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.manifest = root / "bridge-manifest.json"
        self.truth = root / "bridge-truth.jsonl"
        self.observed = root / "observed.json"
        self.source_rows = [
            ("SYNTH-0", "BASE-A"),
            ("SYNTH-1", "BASE-A"),
            ("SYNTH-2", "BASE-B"),
            ("SYNTH-3", "BASE-B"),
        ]
        self.predicted = ["G1", "G1", "G1", "G3"]
        self.write()

    def write(self):
        content = "".join(json.dumps({
            "OpaquePersonId": obs, "BasePersonId": person,
            "Status": "MATERIALIZADA", "ExclusionReason": None,
        }) + "\n" for obs, person in self.source_rows)
        self.truth.write_text(content, encoding="utf-8")
        self.manifest.write_text(json.dumps({"truthSidecar": {
            "path": "bridge-truth.jsonl",
            "sha256": hashlib.sha256(self.truth.read_bytes()).hexdigest().upper(),
            "containsSyntheticGroundTruth": True, "allowedForScoring": False,
        }}), encoding="utf-8")
        self.observed.write_text(json.dumps({
            "schema": ORACLE.SCHEMA, "commitSha": COMMIT,
            "modelWinnerSha256": WINNER,
            "bridgeManifestSha256":
                hashlib.sha256(self.manifest.read_bytes()).hexdigest().upper(),
            "rows": [
                {"opaquePersonId": obs, "groupId": group,
                 "identityState": "REFERENCIA"}
                for (obs, _), group in zip(self.source_rows, self.predicted)
            ],
        }), encoding="utf-8")

    def evaluate(self):
        return ORACLE.compare_groupings(
            self.manifest, self.truth, self.observed, COMMIT, WINNER)

    def test_false_merges_and_missed_links_not_hidden_by_identity_set(self):
        receipt = self.evaluate()
        self.assertEqual(
            (receipt["truePositivePairs"], receipt["falsePositivePairs"],
             receipt["falseNegativePairs"]), (1, 2, 1))
        self.assertEqual(receipt["pairwiseRecallPercent"], 50.0)
        self.assertEqual(receipt["status"], "MEASURED_NOT_CERTIFIED")
        self.assertFalse(receipt["E2E_NUCLEO_PASS"])
        self.assertFalse(receipt["E2E_INTEGRAL_PASS"])
        self.assertFalse(receipt["operationalParityCertified"])
        public = json.dumps(receipt)
        for private in ("SYNTH-0", "BASE-A", "G1"):
            self.assertNotIn(private, public)

    def test_correct_groups_and_unlinked_singletons(self):
        self.predicted = ["A", "A", "B", "B"]
        self.write()
        receipt = self.evaluate()
        self.assertEqual(
            (receipt["truePositivePairs"], receipt["falsePositivePairs"],
             receipt["falseNegativePairs"]), (2, 0, 0))
        self.assertEqual(receipt["observedPairwiseFdrPercent"], 0.0)
        self.predicted = [None] * 4
        self.write()
        receipt = self.evaluate()
        self.assertEqual(
            (receipt["truePositivePairs"], receipt["falsePositivePairs"],
             receipt["falseNegativePairs"]), (0, 0, 2))
        self.assertEqual(receipt["withoutGroup"], 4)
        self.assertIsNone(receipt["observedPairwiseFdrPercent"])

    def test_drift_in_truth_commit_winner_or_manifest_is_rejected(self):
        self.truth.write_text(self.truth.read_text() + "{}\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "digest mismatch"):
            self.evaluate()
        for field, wrong in (("commitSha", "d" * 40),
                             ("modelWinnerSha256", "b" * 64),
                             ("bridgeManifestSha256", "b" * 64)):
            with self.subTest(field=field):
                self.write()
                data = json.loads(self.observed.read_text())
                data[field] = wrong
                self.observed.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()

    def test_missing_duplicate_unknown_and_extra_source_rejected(self):
        for condition in ("missing", "duplicate", "unknown", "extra"):
            with self.subTest(condition=condition):
                self.write()
                data = json.loads(self.observed.read_text())
                if condition == "missing":
                    data["rows"].pop()
                elif condition == "duplicate":
                    data["rows"][1]["opaquePersonId"] = data["rows"][0]["opaquePersonId"]
                elif condition == "unknown":
                    data["rows"][1]["opaquePersonId"] = "SYNTH-UNEXPECTED"
                else:
                    data["rows"][0]["unexpected"] = "new field"
                self.observed.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()

    def test_bad_state_group_and_truth_scoring_permission_rejected(self):
        for field, bad in (("identityState", "INVALID"),
                           ("groupId", 42), ("groupId", " ")):
            with self.subTest(field=field):
                self.write()
                data = json.loads(self.observed.read_text())
                data["rows"][0][field] = bad
                self.observed.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()
        self.write()
        data = json.loads(self.manifest.read_text())
        data["truthSidecar"]["allowedForScoring"] = True
        self.manifest.write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "isolated"):
            self.evaluate()

    def test_nonfinite_duplicate_json_and_alias_fail_closed(self):
        self.observed.write_text('{"x":NaN}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "nonfinite"):
            self.evaluate()
        self.observed.write_text('{"x":1,"x":2}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON"):
            self.evaluate()
        self.write()
        with self.assertRaisesRegex(ValueError, "separate"):
            ORACLE.compare_groupings(
                self.manifest, self.truth, self.truth, COMMIT, WINNER)

    def test_without_true_positive_pairs_cannot_measure_recall(self):
        self.source_rows = [(obs, "BASE-" + str(i))
                            for i, (obs, _) in enumerate(self.source_rows)]
        self.write()
        with self.assertRaisesRegex(ValueError, "no positive pairs"):
            self.evaluate()


if __name__ == "__main__":
    unittest.main()
