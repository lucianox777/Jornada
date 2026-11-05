"""Offline tests: calibration evidence bundle never runs physical calibration."""
import importlib.util
import json
import hashlib
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-calibration-evidence-bundle.py"
spec = importlib.util.spec_from_file_location("v9_calibration_evidence_bundle", SCRIPT)
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


def write_json(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(content, indent=2) + "\n", encoding="utf-8")
    return hashlib.sha256(path.read_bytes()).hexdigest()


class V9CalibrationEvidenceBundleTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "archive"
        self.source.mkdir()
        self.refs = self.root / "refs.json"
        self.fitted = {
            "SchemaVersion": "V9_FITTED_SNAPSHOT_V2",
            "NominalContractVersion": "COMP_V9",
            "ProjectionSchemaVersion": "PROJ_V1",
            "ProjectionFingerprintSha256": "1" * 64,
            "FitFingerprintSha256": "2" * 64,
            "BlockingFrameFingerprintSha256": "2" * 64,
            "TrainCorpusSha256": "3" * 64,
            "ValidationCorpusSha256": "4" * 64,
            "TrainIdentityGroupsSha256": "5" * 64,
            "ValidationIdentityGroupsSha256": "6" * 64,
            "AdapterVersion": "ADAPT_V1",
            "OptionId": "TF_ONE_MF_W1_M0_A0.5_T0.95",
            "Threshold": 0.95, "ConflictMargin": 0.5,
            "BlockingPasses": [{"PassId": "P_NAME_BIRTH", "Fields": ["first", "birth"]}],
            "JointCells": [{"Person": i, "Mother": j, "M": 1/81, "U": 1/81}
                           for i in range(9) for j in range(9)],
            "Parameters": {"m.birth": .8, "u.birth": .01},
            "UnitTfCalibrations": [{"Side": 1, "Unit": 2, "State": 0,
                                    "Calibration": {"Weight": 1, "Mode": 0}}],
        }
        self.plan = {
            "StudyFingerprintSha256": "0" * 64,
            "ModelOptions": [{
                "OptionId": self.fitted["OptionId"], "SmoothingAlpha": .5,
                "DecisionThreshold": .95, "TfWeight": 0,
                "UnitTfOptions": [
                    {"Side": side, "Unit": unit,
                     "Weight": 1 if (side, unit) == (1, 2) else 0, "Mode": 0}
                    for side in (0, 1) for unit in (2, 3)
                ],
            }],
            "ConflictMargins": [0.5],
            "BaselineRuleSets": [self.fitted["BlockingPasses"]],
            "Discovery": {"SearchOptions": {"MinimumTrueMatchRecall": 0.95}},
            "SelectionPolicy": {"MinimumGlobalRecall": .95,
                                "MaximumAcceptedFdrUpper": .01,
                                "MinimumIndependentIdentityGroups": 1000,
                                "MaximumP95Milliseconds": 60000},
            "Budget": {"MinimumGlobalAcceptedRecall": .95,
                       "MaximumObservedAcceptedFdrPoint": .01,
                       "MinimumValidationIdentityGroups": 1000},
            "Execution": {"EndToEndRepetitions": 0, "FsBlindGroupingOnly": True},
            "MaximumComparedConfigurations": 3861,
            "PreviouslyComparedValidationConfigurations": 3726,
            "MaximumAcceptedPerBaseIdentity": 4,
            "ObservationCapDeclaration": {"MaximumObservationsPerBaseIdentity": 4,
                                          "ManifestSha256": "7" * 64,
                                          "ObservationsSha256": "8" * 64},
            "PinnedIbgeReferenceVersionId": 10,
            "PinnedIbgePublishedSha256": "9" * 64,
            "PinnedIbgeMaterializedFrequencySha256": "a" * 64,
            "PinnedIbgeBootstrap": {"GeneratorSeed": 42, "GeneratorVersion": "V1",
                                    "CorpusFingerprint": "b" * 64,
                                    "GenerationHypothesesFingerprint": "c" * 64},
        }
        self.row = {
            "OptionId": self.fitted["OptionId"], "ConflictMargin": .5,
            "TrueAcceptedSourceObservations": 95,
            "FalseAcceptedSourceObservations": 1,
            "EligibleSourceObservationsBeforeBlocking": 100,
            "GlobalSourceRecall": .95,
            "FdrAfterResolve": 1 / 96,
            "CandidateGrouping": "BLIND_FS_COMPLETE_LINK_CANDIDATES",
            "SyntheticGoldOracleGrouping": False,
            "ClusterIidIndependentlyValidated": False,
            "ConditionalClusterCpFdrUpper": .11,
            "HoeffdingUpperBySourceBaseIdentity": .2,
        }
        self.study = {
            "Version": bundle.STUDY_VERSION,
            "PlanSha256": "d" * 64,
            "TrainInputSha256": "e" * 64,
            "ValidationInputSha256": "f" * 64,
            "SourceSelection": {
                "TestUsed": False,
                "SourceReport": {"TestUsed": False, "OperationalModelCreated": False,
                                 "AutomaticActivationAllowed": False},
            },
            "SourceMeasurements": [self.row],
            "FrozenConfigurations": [],
        }
        self.refresh_sources()

    def refresh_sources(self):
        plan_sha = write_json(self.source / "plan-tf.json", self.plan)
        fits = self.source / "study-tf"
        for path in fits.glob("*.fitted.json"):
            path.unlink()
        scratch = self.root / "snap.json"
        fitted_sha = write_json(scratch, self.fitted)
        fitted_path = fits / (fitted_sha + ".fitted.json")
        fitted_path.parent.mkdir(parents=True, exist_ok=True)
        fitted_path.write_bytes(scratch.read_bytes())
        self.study["FrozenConfigurations"] = [{
            "OptionId": self.row["OptionId"], "ConflictMargin": self.row["ConflictMargin"],
            "Snapshot": {"ContentSha256": fitted_sha}
        }]
        study_sha = write_json(fits / "study.json", self.study)
        refs = {
            "schema_version": "V9_FROZEN_ARTIFACT_REFERENCES_V1",
            "models": {"V2_ONE_MF_original": fitted_sha},
            "artifacts": {"seed42-source": {
                "run_id": 41900357031, "head_sha": "a" * 40,
                "name": "fixture-source-run",
                "files": [
                    {"path": "plan-tf.json", "sha256": plan_sha},
                    {"path": "study-tf/study.json", "sha256": study_sha},
                    {"path": "study-tf/" + fitted_sha + ".fitted.json",
                     "sha256": fitted_sha},
                ]
            }},
        }
        write_json(self.refs, refs)

    def make(self, name="bundle"):
        out = self.root / name
        bundle.build(self.source, self.refs, out, "V2_ONE_MF_original")
        return out

    def test_bundle_build_verify_and_trace_all_four_bytes(self):
        out = self.make()
        m, report = bundle.verify(out)
        self.assertEqual(bundle.VERSION, m["SchemaVersion"])
        self.assertEqual({"plan", "result", "fitted_model", "report"},
                         set(m["Components"]))
        self.assertEqual("41900357031", report["CalibrationRunId"])
        self.assertEqual("a" * 40, report["SourceCommitSha"])
        self.assertFalse(report["Status"]["OperationallyCertified"])
        self.assertFalse(report["Status"]["TestEvaluated"])
        self.assertNotEqual(m["Components"]["plan"]["sha256"],
                            m["CrossLinks"]["NormalizedCSharpStudyPlanSha256"])
        self.assertEqual(.95, report["SourceMetrics"]["GlobalSourceRecall"])

    def test_no_overwrite_of_frozen_bundle(self):
        self.make()
        with self.assertRaisesRegex(ValueError, "overwrite"):
            self.make()

    def test_missing_plan_parameter_is_fail_closed(self):
        self.plan.pop("SelectionPolicy")
        self.refresh_sources()
        with self.assertRaisesRegex(ValueError, "Missing calibration-plan fields"):
            self.make()

    def test_missing_fitted_attribute_is_fail_closed(self):
        self.fitted.pop("TrainIdentityGroupsSha256")
        self.refresh_sources()
        with self.assertRaisesRegex(ValueError, "fitted-model fields"):
            self.make()

    def test_tf_slots_must_be_declared(self):
        self.plan["ModelOptions"][0]["UnitTfOptions"].pop()
        self.refresh_sources()
        with self.assertRaisesRegex(ValueError, "four declared unit slots"):
            self.make()

    def test_mismatch_of_run_receipt_is_fail_closed(self):
        self.study["FrozenConfigurations"][0]["Snapshot"]["ContentSha256"] = "0" * 64
        # refresh_sources rebinds frozen config so tamper the file and re-pin
        self.refresh_sources()
        study_path = self.source / "study-tf" / "study.json"
        corrupted = json.loads(study_path.read_text())
        corrupted["FrozenConfigurations"][0]["Snapshot"]["ContentSha256"] = "0" * 64
        new_study_sha = write_json(study_path, corrupted)
        refs = json.loads(self.refs.read_text())
        refs["artifacts"]["seed42-source"]["files"][1]["sha256"] = new_study_sha
        write_json(self.refs, refs)
        with self.assertRaisesRegex(ValueError, "physical fit receipt"):
            self.make()

    def test_modified_report_with_recomputed_checksum_still_fails_binding(self):
        out = self.make()
        report_path = out / "report.json"
        obj = json.loads(report_path.read_text())
        obj["SourceSnapshotSha256"] = "0" * 64
        new_hash = write_json(report_path, obj)
        manifest_path = out / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        manifest["Components"]["report"]["sha256"] = new_hash
        manifest["Components"]["report"]["bytes"] = report_path.stat().st_size
        write_json(manifest_path, manifest)
        with self.assertRaisesRegex(ValueError, "Report not bound"):
            bundle.verify(out)

    def test_comparison_is_descriptive_and_fails_closed_on_corpus_mismatch(self):
        left = self.make("left")
        right = self.root / "right"
        bundle.build(self.source, self.refs, right, "V2_ONE_MF_original")
        first = bundle.compare(left, right)
        self.assertTrue(first["ControlledSourceComparison"])
        self.assertEqual(0.0, first["RecallDelta"])
        self.assertFalse(first["RegressionCertified"])
        result_path = right / "result.json"
        study = json.loads(result_path.read_text())
        study["ValidationInputSha256"] = "b" * 64
        new_hash = write_json(result_path, study)
        report_path = right / "report.json"
        report = json.loads(report_path.read_text())
        report["SourceStudySha256"] = new_hash
        new_report_hash = write_json(report_path, report)
        manifest_path = right / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        manifest["Components"]["result"]["sha256"] = new_hash
        manifest["Components"]["result"]["bytes"] = result_path.stat().st_size
        manifest["Components"]["report"]["sha256"] = new_report_hash
        manifest["Components"]["report"]["bytes"] = report_path.stat().st_size
        manifest["CrossLinks"]["ValidationInputSha256"] = "b" * 64
        write_json(manifest_path, manifest)
        result = bundle.compare(left, right)
        self.assertFalse(result["ControlledSourceComparison"])
        self.assertIsNone(result["RecallDelta"])


if __name__ == "__main__":
    unittest.main()
