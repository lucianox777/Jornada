"""Regression: the official TF-001 30k plan must actually enable the V1 risk preference.

No physical corpus, SQL, reference data or TEST is consumed by these tests.
"""
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "v9_adaptive_risk_bootstrap", ROOT / "scripts" / "v9-adaptive-30k-study.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V9TfRiskBootstrapPlanTests(unittest.TestCase):
    def fixture(self, directory):
        root = pathlib.Path(directory)
        observations = root / "observacoes.csv"
        observations.write_text("synthetic fixture only\n", encoding="utf-8")
        manifest = {
            "seed": 42,
            "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
            "error_profile": "clean",
            "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            "observation_multiplicity": {
                "contract_version": "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1",
                "maximum_observations_per_base_identity": 4,
                "scope": "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES"
            }
        }
        (root / "generation-manifest.json").write_text(json.dumps(manifest))
        pin = {
            "PinnedIbgeReferenceVersionId": 1,
            "PinnedIbgePublishedSha256": "a" * 64,
            "PinnedIbgeMaterializedFrequencySha256": "b" * 64,
            "PinnedIbgeBootstrap": {
                "Provenance": "SINTETICO_IBGE",
                "People": 30000,
                "CorpusFingerprint": "c" * 64,
                "GeneratorVersion": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
                "GeneratorSeed": 42,
                "GenerationHypothesesFingerprint": "d" * 64
            }
        }
        pin_file = root / "pinned-ibge.json"
        pin_file.write_text(json.dumps(pin))
        return observations, pin_file

    def test_tf_physical_plan_bootstraps_risk_preference_without_changing_45_arms(self):
        with tempfile.TemporaryDirectory() as tmp:
            observations, pin_file = self.fixture(tmp)
            plan_file = pathlib.Path(tmp) / "plan-tf.json"
            MODULE.prepare(observations, plan_file, cap_four=True,
                           tf_search=True, pinned_ibge=pin_file)
            plan = json.loads(plan_file.read_text())
            self.assertEqual(plan["ExploratoryRiskPreference"], {
                "FdrUpperTolerancePp": 0.05
            })
            self.assertEqual(len(plan["ModelOptions"]), 45)
            self.assertEqual(sum(x["EligibleForSelection"] for x in plan["ModelOptions"]), 41)
            self.assertEqual(plan["MaximumComparedConfigurations"], 3861)
            self.assertEqual(plan["PreviouslyComparedValidationConfigurations"], 3726)

    def test_operator_can_predeclare_zero_or_different_tolerance(self):
        with tempfile.TemporaryDirectory() as tmp:
            observations, pin_file = self.fixture(tmp)
            for index, value in enumerate([0.0, 0.2]):
                plan_file = pathlib.Path(tmp) / f"other-{index}.json"
                MODULE.prepare(observations, plan_file, cap_four=True,
                               tf_search=True, pinned_ibge=pin_file,
                               exploratory_risk_tolerance_pp=value)
                plan = json.loads(plan_file.read_text())
                self.assertEqual(plan["ExploratoryRiskPreference"]["FdrUpperTolerancePp"], value)

    def test_non_tf_legacy_plan_has_no_risk_preference_and_rejects_new_parameter(self):
        with tempfile.TemporaryDirectory() as tmp:
            observations, _ = self.fixture(tmp)
            plan_file = pathlib.Path(tmp) / "historical.json"
            MODULE.prepare(observations, plan_file)
            self.assertNotIn("ExploratoryRiskPreference", json.loads(plan_file.read_text()))
            with self.assertRaisesRegex(ValueError, "requires TF search"):
                MODULE.prepare(observations, pathlib.Path(tmp) / "reject.json",
                               exploratory_risk_tolerance_pp=0.05)

    def test_invalid_tolerance_fails_before_writing_plan(self):
        with tempfile.TemporaryDirectory() as tmp:
            observations, pin_file = self.fixture(tmp)
            for i, value in enumerate([-0.01, 100.0001, float("nan"),
                                        float("inf"), True]):
                path = pathlib.Path(tmp) / f"invalid-{i}.json"
                with self.assertRaisesRegex(ValueError, "tolerance must be finite"):
                    MODULE.prepare(observations, path, cap_four=True,
                                   tf_search=True, pinned_ibge=pin_file,
                                   exploratory_risk_tolerance_pp=value)
                self.assertFalse(path.exists())


if __name__ == "__main__":
    unittest.main()
