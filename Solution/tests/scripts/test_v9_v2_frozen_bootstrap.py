"""Regression tests: a frozen V2 plan is INPUT, never generated defaults."""
import importlib.util
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

P = Path(__file__).resolve().parents[2] / "scripts" / "v9-v2-frozen-bootstrap.py"
spec = importlib.util.spec_from_file_location("v2_replay", P)
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)


def write(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2) + "\n", encoding="utf-8")
    return hashlib.sha256(path.read_bytes()).hexdigest()


class FrozenV2BootstrapTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.d = Path(self.tmp.name)
        self.planfile = self.d / "plan-v2.json"
        self.ref = self.d / "refs.json"
        self.corpus = self.d / "observacoes.csv"
        self.corpus.write_bytes(b"frozen source observations, for testing only\n")
        self.generator = self.d / "generation-manifest.json"
        write(self.generator, {"seed": 42})
        self.pinfile = self.d / "pinned-ibge.json"
        self.pin = {
            "PinnedIbgeReferenceVersionId": 1,
            "PinnedIbgePublishedSha256": "A" * 64,
            "PinnedIbgeMaterializedFrequencySha256": "B" * 64,
            "PinnedIbgeBootstrap": {"GeneratorVersion": "TEST", "GeneratorSeed": 42},
        }
        write(self.pinfile, self.pin)
        self.v2 = {
            "StudyFingerprintSha256": "1" * 64,
            "ModelOptions": [
                {"OptionId": "ONE_MF", "DecisionThreshold": .95, "SmoothingAlpha": .5,
                 "TfWeight": 0, "EligibleForSelection": True, "UnitTfOptions": [
                     {"Side": 1, "Unit": 2, "Weight": 1, "Mode": 0}]},
            ],
            "ConflictMargins": [.5], "BaselineRuleSets": [[{"PassId": "P1", "Fields": ["x"]}]],
            "Budget": {"MinimumGlobalAcceptedRecall": .95},
            "SelectionPolicy": {"MinimumGlobalRecall": .95},
            "Discovery": {"SearchOptions": {"MinimumTrueMatchRecall": .95}},
            "Execution": {"FsBlindGroupingOnly": True},
            "ObservationCapDeclaration": {
                "ObservationsSha256": tool.digest(self.corpus),
                "ManifestSha256": tool.digest(self.generator),
                "MaximumObservationsPerBaseIdentity": 4,
            },
            "MaximumComparedConfigurations": 50,
            "PreviouslyComparedValidationConfigurations": 20,
            "MaximumAcceptedPerBaseIdentity": 4,
            **self.pin,
        }
        self.snap = "8" * 64
        self.rebuild()

    def rebuild(self):
        plan_sha = write(self.planfile, self.v2)
        self.references = {
            "schema_version": "V9_FROZEN_ARTIFACT_REFERENCES_V1",
            "models": {tool.MODEL_KEY: self.snap},
            "artifacts": {"seed42-source": {
                "run_id": 111, "head_sha": "a" * 40, "name": "historical-readonly",
                "files": [
                    {"path": "plan-tf.json", "sha256": plan_sha},
                    {"path": "study-tf/study.json", "sha256": "7" * 64},
                    {"path": "study-tf/" + self.snap + ".fitted.json",
                     "sha256": self.snap},
                ]}}}
        write(self.ref, self.references)

    def do_prepare(self):
        return tool.prepare(self.planfile, self.ref, self.corpus, self.generator,
                            self.pinfile, self.d / "selected-plan.json",
                            self.d / "receipt.json")

    def test_exact_bytes_and_every_calibration_value_from_source(self):
        doc = self.do_prepare()
        self.assertEqual(self.planfile.read_bytes(),
                         (self.d / "selected-plan.json").read_bytes())
        self.assertEqual("GIT_VERSIONED_FROZEN_V2_PLAN_EXACT_BYTES",
                         doc["ConfigurationSource"])
        self.assertEqual(self.v2["Budget"],
                         json.loads((self.d / "selected-plan.json").read_text())["Budget"])
        self.assertEqual("8" * 64, doc["OriginalFittedSnapshotSha256"])
        self.assertFalse(doc["Calibrated"])

    def test_mismatched_corpus_fail_closed(self):
        self.corpus.write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "corpus does not match"):
            self.do_prepare()
        self.assertFalse((self.d / "selected-plan.json").exists())

    def test_mismatched_generator_manifest_fail_closed(self):
        write(self.generator, {"seed": 99})
        with self.assertRaisesRegex(ValueError, "generation-manifest differs"):
            self.do_prepare()

    def test_original_plan_mutation_requires_new_source_identity(self):
        self.v2["SelectionPolicy"]["MinimumGlobalRecall"] = .92
        write(self.planfile, self.v2)
        with self.assertRaisesRegex(ValueError, "parameters differ"):
            self.do_prepare()

    def test_mismatched_ibge_reference_fail_closed(self):
        self.pin["PinnedIbgePublishedSha256"] = "C" * 64
        write(self.pinfile, self.pin)
        with self.assertRaisesRegex(ValueError, "IBGE digest differs"):
            self.do_prepare()

    def test_uppercase_hex_pins_permitted_when_equal(self):
        self.pin["PinnedIbgePublishedSha256"] = "a" * 64
        self.pin["PinnedIbgeMaterializedFrequencySha256"] = "b" * 64
        write(self.pinfile, self.pin)
        self.do_prepare()

    def test_no_overwrite_of_plan_or_receipt(self):
        self.do_prepare()
        with self.assertRaisesRegex(ValueError, "overwrite"):
            self.do_prepare()

    def test_replay_statistical_regression_comparison(self):
        receipt = self.do_prepare()
        source = self.d / "original-study.json"
        target = self.d / "replay-study.json"
        rows = [{"OptionId": "a" * 64 + ":ONE_MF", "ConflictMargin": .5,
                 "EligibleSourceObservationsBeforeBlocking": 100,
                 "TrueAcceptedSourceObservations": 96,
                 "FalseAcceptedSourceObservations": 0,
                 "GlobalSourceRecall": .96,
                 "FdrAfterResolve": 0, "CandidateGrouping": "BLIND"}]
        study = {
            "Version": "V9_EXAMPLE",
            "PlanSha256": "b" * 64,
            "TrainInputSha256": "c" * 64,
            "ValidationInputSha256": "d" * 64,
            "PredeclaredComparedConfigurations": 50,
            "SourceSelection": {"TestUsed": False},
            "SourceMeasurements": rows,
            "FrozenConfigurations": [
                {"OptionId": rows[0]["OptionId"], "ConflictMargin": .5,
                 "Snapshot": {"ContentSha256": self.snap}}],
        }
        new_sha = write(source, study)
        self.references["artifacts"]["seed42-source"]["files"][1]["sha256"] = new_sha
        write(self.ref, self.references)
        receipt["SourceStudySha256"] = new_sha
        write(self.d / "receipt.json", receipt)
        write(target, study)
        output = self.d / "regression.json"
        result = tool.compare(self.ref, source, target, self.d / "receipt.json", output)
        self.assertEqual("REPRODUCED_EXACTLY", result["Status"])
        self.assertTrue(result["SelectedSnapshotExactMatch"])
        study["SourceMeasurements"][0]["GlobalSourceRecall"] = .95
        study["SourceMeasurements"][0]["TrueAcceptedSourceObservations"] = 95
        write(target, study)
        newer = tool.compare(self.ref, source, target, self.d / "receipt.json",
                             self.d / "different.json")
        self.assertEqual("REGRESSION_DETECTED_REVIEW_REQUIRED", newer["Status"])
        self.assertEqual(1, newer["DifferentMetricRows"])

    def test_original_corpus_identity_changes_forbid_a_comparison(self):
        self.do_prepare()
        old = self.d / "old.json"
        new = self.d / "new.json"
        s = {"Version": "V", "PlanSha256": "1" * 64, "TrainInputSha256": "2" * 64,
             "ValidationInputSha256": "3" * 64, "PredeclaredComparedConfigurations": 1,
             "SourceMeasurements": [{"OptionId": "a:X", "ConflictMargin": .5}],
             "FrozenConfigurations": [],
             "SourceSelection": {"TestUsed": False}}
        ref_sha = write(old, s)
        self.references["artifacts"]["seed42-source"]["files"][1]["sha256"] = ref_sha
        write(self.ref, self.references)
        receipt = json.loads((self.d / "receipt.json").read_text())
        receipt["SourceStudySha256"] = ref_sha
        write(self.d / "receipt.json", receipt)
        s["TrainInputSha256"] = "9" * 64
        write(new, s)
        with self.assertRaisesRegex(ValueError, "Regressed experiment identity"):
            tool.compare(self.ref, old, new, self.d / "receipt.json",
                         self.d / "out.json")


if __name__ == "__main__":
    unittest.main()
