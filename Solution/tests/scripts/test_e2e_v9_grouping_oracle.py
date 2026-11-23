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

    def write_linkage_only_staging(self):
        # Staging has only runtime groups, NOT a fabricated Gold state.
        self.source_rows = [
            (f"SYNTH-{i:032X}", person)
            for i, (_, person) in enumerate(self.source_rows, 1)
        ]
        self.write()
        raw = json.loads(self.observed.read_text(encoding="utf-8"))
        group_a = "00000000-0000-0000-0000-000000000001"
        group_b = "00000000-0000-0000-0000-000000000003"
        staging = {
            "schema": ORACLE.STAGING_SCHEMA,
            "status": "STAGING_NOT_CERTIFIED",
            "runId": "11111111-1111-1111-1111-111111111111",
            "commitSha": COMMIT,
            "modelWinnerSha256": WINNER,
            "observationCount": 4,
            "identityStatesAvailable": False,
            "certified": False,
            "E2E_NUCLEO_PASS": False,
            "E2E_INTEGRAL_PASS": False,
            "rows": [
                {"opaquePersonId": row["opaquePersonId"],
                 "groupId": (group_a if i < 3 else group_b)}
                for i, row in enumerate(raw["rows"])
            ]
        }
        self.observed.write_text(json.dumps(staging), encoding="utf-8")
        return staging

    def test_sql_staging_groups_can_be_compared_without_gold_inference(self):
        self.write_linkage_only_staging()
        receipt = self.evaluate()
        self.assertEqual(
            (receipt["truePositivePairs"], receipt["falsePositivePairs"],
             receipt["falseNegativePairs"]), (1, 2, 1))
        self.assertEqual(receipt["sourceSchema"], ORACLE.STAGING_SCHEMA)
        self.assertEqual(receipt["status"], "MEASURED_NOT_CERTIFIED")
        self.assertFalse(receipt["identityStatesAvailable"])
        self.assertIsNone(receipt["identityStates"])
        self.assertFalse(receipt["producerBridgeBinding"])
        self.assertFalse(receipt["scientificValidation"])
        self.assertFalse(receipt["operationalParityCertified"])
        self.assertFalse(receipt["E2E_NUCLEO_PASS"])
        self.assertFalse(receipt["E2E_INTEGRAL_PASS"])
        self.assertNotIn("SYNTH-", json.dumps(receipt))

    def test_staging_rejects_gold_states_certification_and_bad_uuid(self):
        for key, bad in (
            ("identityStatesAvailable", True),
            ("certified", True),
            ("E2E_NUCLEO_PASS", True),
            ("E2E_INTEGRAL_PASS", True),
            ("status", "PASS"),
            ("runId", "not-a-run"),
            ("observationCount", True),
            ("observationCount", 3),
            ("modelWinnerSha256", "f" * 64),
        ):
            with self.subTest(key=key, bad=bad):
                data = self.write_linkage_only_staging()
                data[key] = bad
                self.observed.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()
        for modification in ("identity-state", "bad-group", "missing-group",
                             "group-null", "unknown-source", "extra-metadata"):
            with self.subTest(modification=modification):
                data = self.write_linkage_only_staging()
                if modification == "identity-state":
                    data["rows"][0]["identityState"] = "REFERENCIA"
                elif modification == "bad-group":
                    data["rows"][0]["groupId"] = "not-a-guid"
                elif modification == "missing-group":
                    data["rows"][0].pop("groupId")
                elif modification == "group-null":
                    data["rows"][0]["groupId"] = None
                elif modification == "unknown-source":
                    data["rows"][0]["opaquePersonId"] = "SYNTH-UNEXPECTED"
                else:
                    data["bridgeManifestSha256"] = "0" * 64
                self.observed.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()

    def test_v2_staging_verifies_sql_source_digest_independently(self):
        stage = self.write_linkage_only_staging()
        stage["schema"] = ORACLE.STAGING_SCHEMA_V2
        stage["sourceSetDigestMethod"] = ORACLE.SOURCE_SET_DIGEST_METHOD
        stage["sourceSetSha256Utf16le"] = hashlib.sha256(
            "".join(source + "\n" for source in sorted(
                obs for obs, _ in self.source_rows)).encode("utf-16le")
        ).hexdigest().upper()
        self.observed.write_text(json.dumps(stage), encoding="utf-8")
        receipt = self.evaluate()
        self.assertTrue(receipt["sourceSetDigestValidated"])
        self.assertEqual(receipt["sourceSchema"], ORACLE.STAGING_SCHEMA_V2)
        self.assertIsNone(receipt["identityStates"])
        self.assertFalse(receipt["producerBridgeBinding"])
        for condition in ("modified_digest", "wrong_method", "missing_digest",
                          "bad_digest_case", "extra_digest_in_v1"):
            with self.subTest(condition=condition):
                changed = dict(stage)
                if condition == "modified_digest":
                    changed["sourceSetSha256Utf16le"] = "0" * 64
                elif condition == "wrong_method":
                    changed["sourceSetDigestMethod"] = "UNKNOWN"
                elif condition == "missing_digest":
                    changed.pop("sourceSetSha256Utf16le")
                elif condition == "bad_digest_case":
                    changed["sourceSetSha256Utf16le"] = stage["sourceSetSha256Utf16le"].lower()
                else:
                    changed["schema"] = ORACLE.STAGING_SCHEMA
                self.observed.write_text(json.dumps(changed), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()

    def test_without_true_positive_pairs_cannot_measure_recall(self):
        self.source_rows = [(obs, "BASE-" + str(i))
                            for i, (obs, _) in enumerate(self.source_rows)]
        self.write()
        with self.assertRaisesRegex(ValueError, "no positive pairs"):
            self.evaluate()


if __name__ == "__main__":
    unittest.main()
