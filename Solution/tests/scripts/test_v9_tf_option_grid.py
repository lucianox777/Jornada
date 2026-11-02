#!/usr/bin/env python3
"""Plan-level TF contract tests. They do NOT claim any physical 30k evidence."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"


def load(filename, name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class V9UnitTfDeclaredGridTests(unittest.TestCase):
    def test_tf0_singles_pairs_and_shared_surname_are_finite_and_distinct(self):
        helper = load("v9-tf-option-grid.py", "tf_grid")
        models = helper.option_grid()
        self.assertEqual(len(models), 45)
        self.assertEqual(len({x["OptionId"] for x in models}), 45)
        self.assertEqual(sum(x.get("SharedSurnameMode", 0) == 1 for x in models), 4)
        self.assertEqual(sum(x.get("UnitTfOptions") is None for x in models), 1)
        for model in models:
            self.assertEqual(model["TfWeight"], 0)
            if "UnitTfOptions" not in model:
                self.assertTrue(model["OptionId"].endswith("_TF0"))
                continue
            self.assertEqual(len(model["UnitTfOptions"]), 4)
            self.assertEqual({(x["Side"], x["Unit"]) for x in model["UnitTfOptions"]},
                             {(0, 2), (0, 3), (1, 2), (1, 3)})
            self.assertTrue(all(x["Weight"] in (0, 0.5, 1)
                                and x["Mode"] in (0, 1) for x in model["UnitTfOptions"]))
            if model.get("SharedSurnameMode") == 1:
                self.assertTrue(all(any(x["Side"] == side and x["Unit"] == 3
                                        and x["Weight"] > 0 for x in model["UnitTfOptions"])
                                    for side in (0, 1)))

    def test_both_physical_plans_predeclare_exact_cost_and_require_pinned_reference(self):
        helper = load("v9-tf-option-grid.py", "tf_grid_cost")
        nested = load("v9-nested-calibration-study.py", "nested_tf")
        adaptive = load("v9-adaptive-30k-study.py", "adaptive_tf")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus = root / "observacoes.csv"
            corpus.write_bytes(b"deterministic plan only; not an empirical 30k corpus\n")
            source_sha = hashlib.sha256(corpus.read_bytes()).hexdigest()
            manifest = {
                "seed": 42, "error_profile": "clean",
                "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
                "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
                "observation_multiplicity": {
                    "contract_version": "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1",
                    "maximum_observations_per_base_identity": 4,
                    "scope": "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES"
                }
            }
            (root / "generation-manifest.json").write_text(json.dumps(manifest))
            pin = {
                "PinnedIbgeReferenceVersionId": 7,
                "PinnedIbgePublishedSha256": "a" * 64,
                "PinnedIbgeMaterializedFrequencySha256": "b" * 64,
                "PinnedIbgeBootstrap": {
                    "Provenance": "SINTETICO_IBGE", "People": 30000,
                    "CorpusFingerprint": source_sha,
                    "GeneratorVersion": "fixture", "GeneratorSeed": 42,
                    "GenerationHypothesesFingerprint": "test only"
                }
            }
            pin_path = root / "pinned-ibge.json"
            pin_path.write_text(json.dumps(pin))
            nested_path = root / "nested-plan.json"
            with self.assertRaises(ValueError):
                nested.prepare(corpus, nested_path, tf_search=True)
            nested.prepare(corpus, nested_path, tf_search=True, pinned_ibge=pin_path)
            n = json.loads(nested_path.read_text())
            self.assertEqual(len(n["Calibration"]["ModelOptions"]), 41)
            self.assertFalse(any("SHARED_PL_ML_" in x["OptionId"] for x in n["Calibration"]["ModelOptions"]))
            self.assertEqual(n["MaximumSearchConfigurations"], 512)
            self.assertEqual(n["PinnedIbgeMaterializedFrequencySha256"], "b" * 64)
            nb = json.loads(nested_path.with_suffix(".tf-budget.json").read_text())
            self.assertEqual(nb["predeclared_comparisons"], 164)
            self.assertEqual(nb["diagnostic_models_excluded_from_selection"], 4)
            self.assertEqual(len(nb["diagnostic_comparison_only"]), 4)
            for pair in nb["diagnostic_comparison_only"]:
                self.assertFalse(pair["EligibleForAutomaticSelection"])
                self.assertTrue(pair["RequiresSameCandidatePairs"])
                self.assertIn(pair["BilateralOptionId"], {x["OptionId"] for x in n["Calibration"]["ModelOptions"]})
            self.assertFalse(nb["physically_executed"])
            plan_path = root / "adaptive-plan.json"
            adaptive.prepare(corpus, plan_path, cap_four=True, tf_search=True, pinned_ibge=pin_path)
            a = json.loads(plan_path.read_text())
            self.assertEqual(len(a["ModelOptions"]), 41)
            self.assertFalse(any("SHARED_PL_ML_" in x["OptionId"] for x in a["ModelOptions"]))
            self.assertEqual(a["MaximumPhysicalRuleSets"], 1)
            self.assertEqual(a["PreviouslyComparedValidationConfigurations"], 3726)
            self.assertEqual(a["MaximumComparedConfigurations"], 3849)
            self.assertEqual(a["PinnedIbgeReferenceVersionId"], 7)
            self.assertEqual(a["ObservationCapDeclaration"]["ObservationsSha256"], source_sha)
            ab = json.loads(plan_path.with_suffix(".tf-budget.json").read_text())
            self.assertEqual(ab["predeclared_comparisons"], 3849)
            self.assertEqual(ab["diagnostic_models_excluded_from_selection"], 4)
            self.assertEqual(len(ab["diagnostic_comparison_only"]), 4)
            for pair in ab["diagnostic_comparison_only"]:
                self.assertFalse(pair["EligibleForAutomaticSelection"])
                self.assertTrue(pair["RequiresSameCandidatePairs"])
                self.assertIn(pair["BilateralOptionId"], {x["OptionId"] for x in a["ModelOptions"]})
            self.assertFalse(ab["test_used"])

    def test_pinned_bootstrap_rejects_noncanonical_provenance_and_invalid_types(self):
        # The C# runtime requires the exact SINTETICO_IBGE provenance and a
        # numeric generator seed. Python plan preparation must reject invalid
        # declarations before any TRAIN configuration is frozen.
        helper = load("v9-tf-option-grid.py", "tf_grid_bootstrap_guard")
        reference = {
            "PinnedIbgeReferenceVersionId": 7,
            "PinnedIbgePublishedSha256": "a" * 64,
            "PinnedIbgeMaterializedFrequencySha256": "b" * 64,
            "PinnedIbgeBootstrap": {
                "Provenance": "SINTETICO_IBGE",
                "People": 30000,
                "CorpusFingerprint": "c" * 64,
                "GeneratorVersion": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
                "GeneratorSeed": 42,
                "GenerationHypothesesFingerprint": "d" * 64,
            },
        }
        self.assertEqual(helper.pinned_reference(reference), reference)
        bad_values = (
            ("Provenance", "UNIT_TEST_ONLY_NOT_PUBLISHED"),
            ("Provenance", ["SINTETICO_IBGE"]),
            ("GeneratorSeed", True),
            ("GeneratorVersion", ["generator"]),
            ("CorpusFingerprint", ["not-a-fingerprint"]),
            ("GenerationHypothesesFingerprint", {"value": "untrusted"}),
        )
        for field, value in bad_values:
            with self.subTest(field=field, value=value):
                mutated = json.loads(json.dumps(reference))
                mutated["PinnedIbgeBootstrap"][field] = value
                with self.assertRaises(ValueError):
                    helper.pinned_reference(mutated)

    def test_adaptive_train_plan_rejects_bad_manifest_and_ignored_pin(self):
        adaptive = load("v9-adaptive-30k-study.py", "adaptive_manifest_guard")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus = root / "observations.csv"
            corpus.write_text("plan-only fixture\\n", encoding="utf-8")
            manifest_path = root / "generation-manifest.json"
            valid = {"seed": 42, "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
                     "error_profile": "clean"}
            manifest_path.write_text(json.dumps({**valid, "seed": 43}), encoding="utf-8")
            destination = root / "invalid-plan.json"
            with self.assertRaisesRegex(ValueError, "manifest"):
                adaptive.prepare(corpus, destination)
            self.assertFalse(destination.exists())
            manifest_path.write_text(json.dumps(valid), encoding="utf-8")
            destination = root / "ignored-pin.json"
            with self.assertRaisesRegex(ValueError, "requires TF search"):
                adaptive.prepare(corpus, destination, pinned_ibge=root / "pinned.json")
            self.assertFalse(destination.exists())

    def test_bad_pinned_reference_fails_closed(self):
        helper = load("v9-tf-option-grid.py", "tf_grid_guard")
        for malformed in (None, [], ["PinnedIbgeReferenceVersionId",
                                    "PinnedIbgePublishedSha256",
                                    "PinnedIbgeMaterializedFrequencySha256",
                                    "PinnedIbgeBootstrap"]):
            with self.subTest(malformed=malformed):
                with self.assertRaisesRegex(ValueError, "JSON object"):
                    helper.pinned_reference(malformed)
        with self.assertRaises(ValueError):
            helper.pinned_reference({})
        with self.assertRaises(ValueError):
            helper.pinned_reference({"PinnedIbgeReferenceVersionId": 0,
                                     "PinnedIbgePublishedSha256": "invalid"})


if __name__ == "__main__":
    unittest.main()
