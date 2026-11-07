"""Offline tests for V2 + historical V3 + U rare-mixture TRAIN plan."""
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

    def test_frozen_24_arm_three_method_plan_keeps_historical_v3_and_zero_witness(self):
        receipt = subject.freeze(self.source, self.output)
        plan = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual(receipt["comparisons"], 24)
        self.assertEqual(receipt["uJointWeights"], [0, .25, .5, 1])
        self.assertEqual(plan["MaximumSearchConfigurations"], 24)
        self.assertEqual(plan["Calibration"]["MaximumComparedConfigurations"], 24)
        self.assertEqual(plan["MaximumBatches"], 1)
        self.assertEqual(plan["Calibration"]["ConflictMargins"], [.5])
        self.assertEqual(plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"], .01)
        self.assertEqual(plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"], .01)
        self.assertEqual(len(plan["Calibration"]["BaselineRuleSets"]), 1)
        self.assertEqual([x["OptionId"] for x in plan["Calibration"]["ModelOptions"]],
                         [control.TF0, control.ONE_MF])
        self.assertEqual([m["Method"] for m in plan["MuEstimationMethods"]],
                         ["DEFAULT", subject.FAMILY, subject.MIXED_FAMILY])
        self.assertNotIn("UBackoffJointMixtureWeights",
                         plan["MuEstimationMethods"][1]["Parameters"])
        self.assertEqual(plan["MuEstimationMethods"][2]["Parameters"]["UBackoffJointMixtureWeights"],
                         [0, .25, .5, 1])
        self.assertEqual(receipt["estimatorMethods"], 3)
        self.assertEqual(receipt["estimatedVariants"], 6)
        self.assertTrue(receipt["historicalV3Preserved"])
        self.assertEqual(json.loads(self.source.read_text()), self.base)
        self.assertFalse(receipt["physicalExecuted"])
        self.assertFalse(receipt["certified"])
        self.assertFalse(receipt["activated"])
        self.assertFalse(receipt["testUsed"])
        with self.assertRaises(FileExistsError):
            subject.freeze(self.source, self.output)

    def test_predeclared_quarter_margin_is_isolated_and_no_hidden_relaxation(self):
        alternate = Path(self.output).with_name("sensitivity.json")
        primary = subject.freeze(self.source, self.output)
        sensitivity = subject.freeze(self.source, alternate, margin=.25)
        first = json.loads(self.output.read_bytes())
        second = json.loads(alternate.read_bytes())
        self.assertEqual(first["Calibration"]["ConflictMargins"], [.5])
        self.assertEqual(second["Calibration"]["ConflictMargins"], [.25])
        self.assertEqual(primary["comparisons"], sensitivity["comparisons"])
        self.assertEqual(primary["uJointWeights"], sensitivity["uJointWeights"])
        self.assertEqual(primary["margin"], .5)
        self.assertEqual(sensitivity["margin"], .25)
        self.assertNotEqual(primary["studyFingerprintSha256"],
                            sensitivity["studyFingerprintSha256"])
        self.assertNotEqual(primary["planSha256"], sensitivity["planSha256"])
        self.assertEqual(first["MuEstimationMethods"], second["MuEstimationMethods"])
        self.assertEqual(first["Calibration"]["ModelOptions"],
                         second["Calibration"]["ModelOptions"])
        self.assertEqual(first["MaximumSearchConfigurations"], 24)
        self.assertEqual(second["MaximumSearchConfigurations"], 24)
        self.assertEqual(first["Calibration"]["MaximumComparedConfigurations"], 24)
        self.assertEqual(second["Calibration"]["MaximumComparedConfigurations"], 24)
        for bad in (-.5, 0, .3, 2, None, True):
            with self.subTest(margin=bad):
                rejected = Path(self.output).with_name("invalid.json")
                with self.assertRaises(ValueError):
                    subject.freeze(self.source, rejected, margin=bad)
                self.assertFalse(rejected.exists())

    def test_mutated_source_or_tf_baseline_refuses_without_output(self):
        mutations = [
            lambda p: p["Calibration"]["ModelOptions"][0].update(TfWeight=1),
            lambda p: p["Calibration"]["ModelOptions"][1].update(DecisionThreshold=.9),
            lambda p: p["PinnedIbgeBootstrap"].update(GeneratorSeed=43),
            lambda p: p["Calibration"].update(BaselineRuleSets=[]),
            lambda p: p["Calibration"]["ModelOptions"][0].update(SmoothingAlpha=1),
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
