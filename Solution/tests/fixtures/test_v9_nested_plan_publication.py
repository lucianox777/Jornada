"""Fail-first regression for frozen TRAIN calibration plan + TF budget publication.

No TEST data, physical 30k execution or model activation is performed here.
"""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-nested-calibration-study.py"


def load_study():
    spec = importlib.util.spec_from_file_location("v9_nested_publication", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class V9NestedPlanPublicationTests(unittest.TestCase):
    def test_tf_sidecar_failure_does_not_leave_orphan_frozen_plan(self):
        study = load_study()
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "plan.json"
            sidecar = destination.with_suffix(".tf-budget.json")
            original_open = Path.open

            def failing_open(path, mode="r", *args, **kwargs):
                if path == sidecar and mode == "x":
                    raise OSError("injected TF budget write failure")
                return original_open(path, mode, *args, **kwargs)

            with mock.patch.object(Path, "open", new=failing_open):
                with self.assertRaisesRegex(OSError, "injected TF budget"):
                    study._write_frozen_outputs(destination, {"StudyFingerprintSha256": "abc"}, {"models": 41})
            self.assertFalse(destination.exists(), "orphan plan must not be published")
            self.assertFalse(sidecar.exists())

    def test_existing_tf_budget_is_never_overwritten(self):
        study = load_study()
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "plan.json"
            sidecar = destination.with_suffix(".tf-budget.json")
            sidecar.write_text("original frozen budget", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                study._write_frozen_outputs(destination, {"StudyFingerprintSha256": "abc"}, {"models": 41})
            self.assertFalse(destination.exists())
            self.assertEqual(sidecar.read_text(encoding="utf-8"), "original frozen budget")

    def test_success_publishes_both_and_never_replaces_existing_plan(self):
        study = load_study()
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "plan.json"
            study._write_frozen_outputs(destination, {"StudyFingerprintSha256": "abc"}, {"models": 41})
            self.assertEqual(json.loads(destination.read_text(encoding="utf-8"))["StudyFingerprintSha256"], "abc")
            self.assertEqual(json.loads(destination.with_suffix(".tf-budget.json").read_text(encoding="utf-8"))["models"], 41)
            with self.assertRaises(FileExistsError):
                study._write_frozen_outputs(destination, {"StudyFingerprintSha256": "different"}, {"models": 0})


    def test_tf_budget_identifies_exact_frozen_train_study(self):
        study = load_study()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus = root / "observations.csv"
            corpus.write_text("synthetic plan fixture only\\n", encoding="utf-8")
            (root / "generation-manifest.json").write_text(json.dumps({
                "error_profile": "clean",
                "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
                "observation_multiplicity": {"contract_version": "V9_TEST_CAP"},
                "generator_version": "V9_TEST_GENERATOR"
            }), encoding="utf-8")
            pinned = root / "pinned.json"
            pinned.write_text(json.dumps({
                "PinnedIbgeReferenceVersionId": 1,
                "PinnedIbgePublishedSha256": "a" * 64,
                "PinnedIbgeMaterializedFrequencySha256": "b" * 64,
                "PinnedIbgeBootstrap": {
                    "Provenance": "SINTETICO_IBGE", "People": 30000,
                    "CorpusFingerprint": "fixture-fingerprint",
                    "GeneratorVersion": "fixture-generator", "GeneratorSeed": 42,
                    "GenerationHypothesesFingerprint": "fixture-hypotheses"
                }
            }), encoding="utf-8")
            destination = root / "plan.json"
            study.prepare(corpus, destination, tf_search=True, pinned_ibge=pinned)
            plan = json.loads(destination.read_text(encoding="utf-8"))
            budget = json.loads(destination.with_suffix(".tf-budget.json").read_text(encoding="utf-8"))
            self.assertEqual(budget["study_fingerprint_sha256"],
                             plan["Calibration"]["StudyFingerprintSha256"])
            self.assertEqual(budget["models"], 41)
            self.assertFalse(budget["test_used"])


if __name__ == "__main__":
    unittest.main()
