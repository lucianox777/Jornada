"""No-retune TF0 V2/V3 controlled plan guardrails."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPTS=Path(__file__).resolve().parents[2]/"scripts"
def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path)
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

control=load("mu_tf0_controlled",SCRIPTS/"v9-mu-tf0-controlled-plan.py")
fixture=load("historical_fixture",Path(__file__).with_name("test_v9_mu_historical_plan.py"))


class Tf0ControlledPlanTests(unittest.TestCase):
    def setUp(self):
        temp=tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root=Path(temp.name)
        self.source=self.root/"source.json"
        self.output=self.root/"controlled.json"
        self.base=fixture.baseline()
        self.base["Calibration"]["ModelOptions"].insert(
            0,control.mu._helper().option_grid(.5,.95)[0])

    def run_freeze(self):
        self.source.write_text(json.dumps(self.base),encoding="utf-8")
        return control.freeze(self.source,self.output)

    def test_one_frame_eight_arms_and_no_changed_original_plan(self):
        receipt=self.run_freeze()
        result=json.loads(self.output.read_text())
        self.assertEqual(receipt["comparisons"],8)
        self.assertEqual([x["OptionId"] for x in result["Calibration"]["ModelOptions"]],
                         [control.TF0,control.ONE_MF])
        self.assertEqual(result["MaximumSearchConfigurations"],8)
        self.assertEqual(result["Calibration"]["MaximumComparedConfigurations"],8)
        self.assertEqual(result["MaximumBatches"],1)
        self.assertEqual(len(result["MuEstimationMethods"]),2)
        self.assertEqual(len(result["Calibration"]["BaselineRuleSets"]),1)
        self.assertEqual(result["Calibration"]["ConflictMargins"],[.5])
        self.assertEqual(result["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"],.01)
        self.assertEqual(result["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"],.01)
        self.assertEqual(json.loads(self.source.read_text()),self.base)
        with self.assertRaises(FileExistsError):
            self.run_freeze()

    def test_canonical_tf0_matches_legacy_numeric_baseline_and_no_hidden_tf(self):
        control_tf0=self.base["Calibration"]["ModelOptions"][0]
        self.assertEqual(set(control_tf0),{"OptionId","SmoothingAlpha","DecisionThreshold","TfWeight"})
        self.assertEqual((control_tf0["SmoothingAlpha"],control_tf0["DecisionThreshold"],control_tf0["TfWeight"]),
                         (.5,.95,0))
        self.assertNotEqual(control_tf0["OptionId"],control.LEGACY_TF0_ALIAS)

    def test_mutated_option_id_or_parameters_fail_closed(self):
        for modify in (
            lambda d:d["Calibration"]["ModelOptions"][0].update(OptionId=control.LEGACY_TF0_ALIAS),
            lambda d:d["Calibration"]["ModelOptions"][0].update(UnitTfOptions=[]),
            lambda d:d["Calibration"]["ModelOptions"][0].update(SmoothingAlpha=1),
            lambda d:d["Calibration"]["ModelOptions"][0].update(DecisionThreshold=.90),
            lambda d:d["Calibration"]["ModelOptions"][1].update(TfWeight=1),
            lambda d:d["PinnedIbgeBootstrap"].update(GeneratorSeed=43),
        ):
            with self.subTest(modify=modify):
                self.base=fixture.baseline()
                self.base["Calibration"]["ModelOptions"].insert(0,control.mu._helper().option_grid(.5,.95)[0])
                modify(self.base)
                self.source.write_text(json.dumps(self.base),encoding="utf-8")
                with self.assertRaises((KeyError,ValueError,AssertionError)):
                    control.freeze(self.source,self.output)
                self.assertFalse(self.output.exists())


if __name__=="__main__":
    unittest.main()
