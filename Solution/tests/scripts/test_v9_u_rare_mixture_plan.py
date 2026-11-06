"""Offline tests for explicit TF0 / ONE_MF V2 + U rare-mixture TRAIN plan."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


subject = load("v9_u_rare_mixture_offline", SCRIPTS / "v9-u-rare-mixture-plan.py")
control = load("v9_tf0_control_rare", SCRIPTS / "v9-mu-tf0-controlled-plan.py")
fixture = load("v9_historical_fixture_rare", Path(__file__).with_name("test_v9_mu_historical_plan.py"))


class RareUPlanTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.source = Path(temp.name) / "source.json"
        self.output = Path(temp.name) / "rare-plan.json"
        self.base = fixture.baseline()
        self.base["Calibration"]["ModelOptions"].insert(
            0, control.mu._helper().option_grid(.5, .95)[0])
        self.source.write_text(json.dumps(self.base), encoding="utf-8")

    def test_frozen_20_arm_pair_stays_on_same_source_train_and_zero_is_only_witness(self):
        receipt = subject.freeze(self.source, self.output)
        plan = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual(receipt["comparisons"], 20)
        self.assertEqual(receipt["uJointWeights"], [0, .25, .5, 1])
        self.assertEqual(plan["MaximumSearchConfigurations"], 20)
        self.assertEqual(plan["Calibration"]["MaximumComparedConfigurations"], 20)
        self.assertEqual(plan["MaximumBatches"], 1)
        self.assertEqual(plan["Calibration"]["ConflictMargins"], [.5])
        self.assertEqual(plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"], .01)
        self.assertEqual(plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"], .01)
        self.assertEqual(len(plan["Calibration"]["BaselineRuleSets"]), 1)
        self.assertEqual([x["OptionId"] for x in plan["Calibration"]["ModelOptions"]],
                         [control.TF0, control.ONE_MF])
        self.assertEqual([m["Method"] for m in plan["MuEstimationMethods"]],
                         ["DEFAULT", subject.FAMILY])
        self.assertEqual(plan["MuEstimationMethods"][1]["Parameters"]["UBackoffJointMixtureWeights"],
                         [0, .25, .5, 1])
        self.assertEqual(json.loads(self.source.read_text()), self.base)
        self.assertFalse(receipt["physicalExecuted"])
        self.assertFalse(receipt["certified"])
        self.assertFalse(receipt["activated"])
        self.assertFalse(receipt["testUsed"])
        with self.assertRaises(FileExistsError):
            subject.freeze(self.source, self.output)

    def test_mutated_source_or_tf_baseline_refuses_without_output(self):
        mutations = [
            lambda p: p["Calibration"]["ModelOptions"][0].update(TfWeight=1),
            lambda p: p["Calibration"]["ModelOptions"][1].update(DecisionThreshold=.9),
            lambda p: p["PinnedIbgeBootstrap"].update(GeneratorSeed=43),
            lambda p: p["Calibration"].update(BaselineRuleSets=[]),
            lambda p: p["Calibration"].update(ConflictMargins=[0]),
        ]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                self.base = fixture.baseline()
                self.base["Calibration"]["ModelOptions"].insert(
                    0, control.mu._helper().option_grid(.5, .95)[0])
                mutation(self.base)
                self.source.write_text(json.dumps(self.base), encoding="utf-8")
                with self.assertRaises((ValueError, KeyError, AssertionError)):
                    subject.freeze(self.source, self.output)
                self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
