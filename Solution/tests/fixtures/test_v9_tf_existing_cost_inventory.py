#!/usr/bin/env python3
"""Synthetic-only regression of read-only, version-preserving TF-001 cost inventory."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-tf-existing-cost-inventory.py"
spec = importlib.util.spec_from_file_location("tf_cost_inventory", SCRIPT)
cost = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cost)
ZERO, ONE, TWO, THREE, FOUR = ("0"*64, "1"*64, "2"*64, "3"*64, "4"*64)


def fixture():
    arms = ["T0.95_TF0"] + [f"TF_CONFIG_{i:02}" for i in range(40)] + [f"TF_SHARED_PL_ML_{i:02}" for i in range(4)]
    rows, frozen, paired = [], [], {}
    for name in arms:
        oid = f"{ZERO}:{name}"
        paired[oid] = 10
        for margin in (0.5, 2, 4):
            rows.append({"OptionId": oid, "ConflictMargin": margin,
                         "CandidateGrouping": "BLIND_FS_COMPLETE_LINK_CANDIDATES",
                         "SyntheticGoldOracleGrouping": False,
                         "TrueAcceptedSourceObservations": 50,
                         "FalseAcceptedSourceObservations": 1,
                         "EligibleSourceObservationsBeforeBlocking": 100,
                         "GlobalSourceRecall": .5, "FdrAfterResolve": 1/51})
            frozen.append({"OptionId": oid, "ConflictMargin": margin,
                           "Snapshot": {"ContentSha256": ONE}})
    return {"Version": cost.STUDY_VERSION, "PlanSha256": ONE,
            "TrainInputSha256": TWO, "ValidationInputSha256": THREE,
            "SourceSelection": {"TestUsed": False, "SourceReport": {"TestUsed": False,
             "OperationalModelCreated": False, "AutomaticActivationAllowed": False}},
            "SourceMeasurements": rows, "FrozenConfigurations": frozen,
            "CandidatePairsSha256ByRuleset": {ZERO: FOUR},
            "SharedSurnameCandidatePairsByOption": paired,
            "EndToEndMeasurements": []}


def measured(oid, margin, scale=1):
    samples = [float(i*scale) for i in range(1, 21)]
    return {"OptionId": oid, "ConflictMargin": margin,
            "CandidateGrouping": "BLIND_FS_COMPLETE_LINK_CANDIDATES",
            "ProtocolSha256": ONE, "EnvironmentSha256": TWO,
            "OutcomeSha256": THREE, "SamplesMilliseconds": samples,
            "Quantiles": {"SampleCount": 20, "P95Milliseconds": 19*scale,
                          "P99Milliseconds": 20*scale, "MaximumMilliseconds": 20*scale}}


class TestExistingCostInventory(unittest.TestCase):
    def generate(self, study):
        return cost.build(study, code_sha="f"*40, run_id=41681714081, study_sha=ZERO)

    def test_tf_001_no_latency_is_not_free(self):
        manifest, rows = self.generate(fixture())
        self.assertEqual(manifest["source_rows"], 135)
        self.assertEqual(manifest["timed_configurations"], 0)
        self.assertEqual(manifest["status"], "NO_PER_CONFIGURATION_PHYSICAL_COST")
        self.assertEqual(sum(x["end_to_end_p95_ms"] is None for x in rows), 135)
        self.assertEqual(sum(x["relative_p95_vs_tf0_pct"] is None for x in rows), 135)
        self.assertIn("NÃO MEDIDO", cost.render(manifest, rows))

    def test_comparable_physical_latency_only_when_measured(self):
        f = fixture()
        f["EndToEndMeasurements"] = [measured(ZERO + ":T0.95_TF0", .5),
                                     measured(ZERO + ":TF_CONFIG_00", .5, 2)]
        manifest, rows = self.generate(f)
        self.assertEqual(manifest["timed_configurations"], 2)
        item = next(x for x in rows if x["option"] == "TF_CONFIG_00" and x["margin"] == .5)
        self.assertAlmostEqual(item["relative_p95_vs_tf0_pct"], 100)
        self.assertIsNone(next(x for x in rows if x["option"] == "TF_CONFIG_00" and x["margin"] == 2)["relative_p95_vs_tf0_pct"])

    def test_mixed_environment_or_inconsistent_p95_rejected(self):
        f = fixture()
        a, b = measured(ZERO + ":T0.95_TF0", .5), measured(ZERO + ":TF_CONFIG_00", .5)
        b["EnvironmentSha256"] = FOUR
        f["EndToEndMeasurements"] = [a, b]
        with self.assertRaisesRegex(ValueError, "environments"):
            self.generate(f)
        b["EnvironmentSha256"] = TWO
        b["Quantiles"]["P95Milliseconds"] = 0
        with self.assertRaisesRegex(ValueError, "quantiles"):
            self.generate(f)

    def test_duplicate_evidence_is_rejected(self):
        f = fixture()
        f["SourceMeasurements"][1]["OptionId"] = f["SourceMeasurements"][0]["OptionId"]
        f["SourceMeasurements"][1]["ConflictMargin"] = .5
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.generate(f)

    def test_output_aggregate_and_source_immutable(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            source = root / "study.json"
            source.write_text(json.dumps(fixture()), encoding="utf-8")
            original = source.read_bytes()
            dest = root / "inventory"
            manifest = cost.run(source, dest, "f"*40, 41681714081)
            self.assertEqual(manifest["source_rows"], 135)
            self.assertEqual(source.read_bytes(), original)
            content = (dest / "V9-TF-001-cost-inventory.csv").read_text()
            self.assertEqual(len(content.splitlines()), 136)
            self.assertNotIn("BasePersonId", content)
            with self.assertRaisesRegex(ValueError, "new"):
                cost.run(source, dest, "f"*40, 41681714081)


if __name__ == "__main__":
    unittest.main()
