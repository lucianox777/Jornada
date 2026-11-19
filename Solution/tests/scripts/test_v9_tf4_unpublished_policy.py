"""Regression: TF4 never turns missing IBGE frequencies into invented scores."""
import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-v3-four-name-100k-plan.py"
spec = importlib.util.spec_from_file_location("tf4_plan", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Tf4UnpublishedPolicyTests(unittest.TestCase):
    def test_active_tf_abstains_while_tf0_remains_historical(self):
        tf0 = {"OptionId": module.TF0, "TfWeight": 0,
               "UnpublishedTermPolicy": 0, "UnitTfOptions": None}
        policy = {"contextPopulation": 11911337,
                  "unpublishedOccurrenceCount": 20,
                  "unpublishedTermPolicy": 0}
        options = module.grid(tf0, policy)
        self.assertEqual(len(options), 81)
        self.assertEqual(options[0]["UnpublishedTermPolicy"], 0)
        self.assertTrue(all(o["UnpublishedTermPolicy"] == 1 for o in options[1:]))
        self.assertTrue(all(o["ContextPopulation"] == 11911337 for o in options[1:]))
        self.assertTrue(all(o["UnpublishedOccurrenceCount"] == 20 for o in options[1:]))

    def test_invalid_historical_control_fails_closed(self):
        with self.assertRaises(ValueError):
            module.grid({"OptionId": module.TF0, "TfWeight": 0,
                         "UnpublishedTermPolicy": 1, "UnitTfOptions": None})


if __name__ == "__main__":
    unittest.main()
