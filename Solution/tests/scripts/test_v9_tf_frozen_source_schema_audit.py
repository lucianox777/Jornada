import importlib.util
import unittest
from pathlib import Path

source = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-frozen-source-schema-audit.py"
spec = importlib.util.spec_from_file_location("frozen_schema_audit", source)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class FrozenSourceSchemaAuditTests(unittest.TestCase):
    @staticmethod
    def fixture(schema="V9_FITTED_SNAPSHOT_V2", active=True):
        return {
            "SchemaVersion": schema,
            "BlockingPasses": [{"PassId": "P_TEST", "Fields": ["name_first"]}],
            "JointCells": [{} for _ in range(81)],
            "JointSupport": {} if schema.endswith("_V3") else None,
            "CandidateGrouping": {"CandidateGrouping": "BLIND_FS_COMPLETE_LINK_CANDIDATES"},
            "TfWeight": 0,
            "UnitTfCalibrations": [
                {"Side": 1, "Unit": 2, "State": 0 if active else 1,
                 "Calibration": {"Weight": 1, "Mode": 0} if active else {"Weight": 0}}],
        }

    def test_v2_tf_positive_is_not_v3_gold(self):
        result = audit.structure(self.fixture())
        self.assertEqual(1, result["activeUnitTfCount"])
        self.assertFalse(result["goldTfV3DiagnosticStructurallyEligible"])
        self.assertEqual("V2_WITHOUT_JOINT_SUPPORT", result["ineligibleReason"])

    def test_real_v3_active_tf_is_gold_diagnostic_structure_eligible_only(self):
        result = audit.structure(self.fixture("V9_FITTED_SNAPSHOT_V3"))
        self.assertTrue(result["goldTfV3DiagnosticStructurallyEligible"])
        self.assertEqual(81, result["jointCellCount"])
        self.assertEqual(1, result["blockingPassCount"])

    def test_v3_without_active_tf_is_rejected(self):
        result = audit.structure(self.fixture("V9_FITTED_SNAPSHOT_V3", active=False))
        self.assertEqual("NO_ACTIVE_UNIT_TF", result["ineligibleReason"])

    @staticmethod
    def support_fixture():
        return {"FrozenConfigurations": [{
            "Snapshot": {"ContentSha256": "a"*64},
            "JointSupport": {
                "SnapshotSha256": "a"*64, "SmoothingAlpha": 0.5,
                "TrainMPairs": 82, "TrainUPairs": 162,
                "IndependentIdentitySupportEstablished": False,
                "Cells": [
                    {"Person": p, "Mother": m,
                     "ObservedMPairs": 2 if p == 0 and m == 0 else 1,
                     "ObservedUPairs": 2}
                    for p in range(9) for m in range(9)
                ]
            }
        }]}

    def test_archived_train_joint_counts_have_unique_cells_and_physical_totals(self):
        v = audit.archived_train_joint_support(self.support_fixture(), "a"*64)
        self.assertTrue(v["rawObserved81Available"])
        self.assertEqual(82, v["trainMPairs"])
        self.assertEqual(162, v["trainUPairs"])
        self.assertEqual("RAW_TRAIN_81_PAIR_COUNTS_AUDITED_NOT_CERTIFIED", v["status"])
        self.assertFalse(v["newJointFitComputed"])
        self.assertFalse(v["independentIdentitySupportEstablished"])

    def test_train_support_cannot_be_invented_or_reassigned(self):
        f = self.support_fixture()
        f["FrozenConfigurations"][0]["JointSupport"] = None
        self.assertEqual("NO_TRAIN_SUPPORT_RECEIPT",
                         audit.archived_train_joint_support(f, "a"*64)["status"])
        f = self.support_fixture()
        f["FrozenConfigurations"][0]["JointSupport"]["SnapshotSha256"] = "b"*64
        with self.assertRaisesRegex(ValueError, "binding"):
            audit.archived_train_joint_support(f, "a"*64)
        f = self.support_fixture()
        f["FrozenConfigurations"][0]["JointSupport"]["Cells"][0]["ObservedMPairs"] = 500
        with self.assertRaisesRegex(ValueError, "pair totals"):
            audit.archived_train_joint_support(f, "a"*64)
        f = self.support_fixture()
        f["FrozenConfigurations"][0]["JointSupport"]["Cells"][1]["Mother"] = 0
        with self.assertRaisesRegex(ValueError, "Duplicated"):
            audit.archived_train_joint_support(f, "a"*64)

    def test_v3_without_joint_support_is_rejected(self):
        malformed = self.fixture("V9_FITTED_SNAPSHOT_V3")
        malformed["JointSupport"] = None
        with self.assertRaisesRegex(ValueError, "joint-support"):
            audit.structure(malformed)


if __name__ == "__main__":
    unittest.main()
