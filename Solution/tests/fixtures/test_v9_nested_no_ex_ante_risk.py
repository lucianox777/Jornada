"""Regression: the production 30k TRAIN plan cannot impose an ex-ante FDR/recall ceiling.

The fixture-only plan remains a plumbing test, not an operational recommendation.
"""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-nested-calibration-study.py"


class V9NestedNoExAnteRiskTests(unittest.TestCase):
    def test_production_bootstrap_search_exposes_risk_without_preselection_ceiling(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus = root / "observacoes.csv"
            corpus.write_text("synthetic fixture for plan only\n", encoding="utf-8")
            (root / "generation-manifest.json").write_text(json.dumps({
                "error_profile": "clean",
                "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
                "observation_multiplicity": {"contract_version": "V9_TEST_CAP"},
                "generator_version": "V9_TEST_GENERATOR"
            }), encoding="utf-8")
            spec = importlib.util.spec_from_file_location("v9_nested_plan", SCRIPT)
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            destination = root / "plan.json"
            module.prepare(corpus, destination)
            plan = json.loads(destination.read_text(encoding="utf-8"))
            self.assertEqual(plan["Purpose"], "BOOTSTRAP_CANDIDATE_SELECTION")
            self.assertEqual(plan["Calibration"]["Budget"]["MinimumGlobalAcceptedRecall"], 0)
            self.assertEqual(plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"], 1)
            self.assertEqual(plan["Calibration"]["SelectionPolicy"]["MinimumGlobalRecall"], 0)
            self.assertEqual(plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"], 1)
            self.assertFalse(plan.get("TestUsed", False))
            self.assertNotIn("externalValidation", plan)


if __name__ == "__main__":
    unittest.main()
