"""Official plan.json: V2 exact historical parameter vector and role+CP invariants."""
from __future__ import annotations
import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

TESTS = Path(__file__).resolve().parent
SCRIPTS = TESTS.parents[1] / "scripts"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


official = load(SCRIPTS / "v9-multiparameter-official-plan.py", "official_v9")
history = load(TESTS / "test_v9_mu_historical_plan.py", "historical_v9_test")
control = load(SCRIPTS / "v9-mu-tf0-controlled-plan.py", "historical_tf0_v9")


class OfficialMultiparameterPlanTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "source-nested-plan.json"
        self.output = self.root / "official" / "plan.json"
        self.source_data = history.baseline()
        self.source_data["Calibration"]["ModelOptions"].insert(
            0, control.mu._helper().option_grid(.5, .95)[0])

    def write_source(self):
        self.source.write_text(json.dumps(self.source_data), encoding="utf8")

    def test_single_official_plan_retains_all_exact_v2_history_explicit_and_cp16(self):
        self.write_source()
        raw_before = self.source.read_bytes()
        receipt = official.freeze(self.source, self.output)
        plan = official.read(self.output)
        self.assertEqual(self.source.read_bytes(), raw_before)
        self.assertEqual(receipt["predeclaredCandidateCpMultiplicity"], 16)
        self.assertEqual(plan["PredeclaredCandidateCpMultiplicity"], 16)
        self.assertEqual(plan["MaximumSearchConfigurations"], 24)
        self.assertEqual(plan["Calibration"]["MaximumComparedConfigurations"], 24)
        self.assertEqual(plan["MaximumBatches"], 1)
        self.assertEqual(plan["Calibration"]["ConflictMargins"], [.5])
        self.assertEqual(plan["SelectionDuplicatePolicies"], ["COUNT_ALL", "COUNT_UNIQUE"])
        self.assertEqual([x["Method"] for x in plan["MuEstimationMethods"]],
            ["DEFAULT", "CLASSWISE_SUPPORT_BACKOFF", "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"])
        self.assertEqual(plan["MuEstimationMethods"][0], {
            "Method": "DEFAULT", "Role": "WITNESS", "Anchor": True})
        self.assertTrue(all(x["Role"] == "CANDIDATE" for x in plan["MuEstimationMethods"][1:]))
        self.assertEqual(plan["MuEstimationMethods"][2]["Parameters"]["UBackoffJointMixtureWeights"],
                         [0, .25, .5, 1])
        self.assertEqual([x["OptionId"] for x in plan["Calibration"]["ModelOptions"]],
                         [control.TF0, control.ONE_MF])
        self.assertEqual(plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"], .01)
        self.assertEqual(plan["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"], .01)
        self.assertEqual(plan["PinnedIbgeBootstrap"], self.source_data["PinnedIbgeBootstrap"])
        self.assertEqual(plan["Calibration"]["ObservationCapDeclaration"],
                         self.source_data["Calibration"]["ObservationCapDeclaration"])
        self.assertEqual(plan["Calibration"]["BaselineRuleSets"],
                         self.source_data["Calibration"]["BaselineRuleSets"])
        self.assertEqual(plan["Calibration"]["DeclaredSearchSpace"],
                         self.source_data["Calibration"]["DeclaredSearchSpace"])
        originals = self.source_data["Calibration"]["ModelOptions"]
        defaults = {
            "TfSide": 0, "TfMinimumFrequency": .000001,
            "TfMinimumIndependentSupport": 1, "JointSupportPolicy": None,
            "UnitTfOptions": None, "SharedSurnameMode": 0,
            "EligibleForSelection": True, "UnpublishedTermPolicy": 0}
        for old, explicit in zip(originals, plan["Calibration"]["ModelOptions"]):
            self.assertEqual(explicit, {**defaults, **old},
                             "All effective historical C# V2 config fields must be explicit")
        self.assertFalse(list(self.root.rglob("experiment.json")))
        self.assertEqual(list(self.output.parent.iterdir()), [self.output])
        with self.assertRaises(FileExistsError):
            official.freeze(self.source, self.output)

    def test_declared_sensitivity_role_is_still_one_official_plan_with_v2_anchor(self):
        self.write_source()
        receipt = official.freeze(self.source, self.output,
                                  role="PREDECLARED_SENSITIVITY")
        plan = official.read(self.output)
        self.assertEqual(plan["Calibration"]["ConflictMargins"], [.25])
        self.assertEqual(receipt["marginRole"], "PREDECLARED_SENSITIVITY")
        self.assertEqual(receipt["predeclaredCandidateCpMultiplicity"], 16)
        self.assertEqual(plan["MuEstimationMethods"][0]["Role"], "WITNESS")
        self.assertTrue(plan["MuEstimationMethods"][0]["Anchor"])

    def test_historical_source_drift_fails_before_creating_official_file(self):
        variants = [
            lambda d: d["Calibration"]["ModelOptions"][0].update(UnitTfOptions=[]),
            lambda d: d["Calibration"]["ModelOptions"][0].update(TfWeight=1),
            lambda d: d["Calibration"]["ModelOptions"][1].update(SmoothingAlpha=.25),
            lambda d: d["Calibration"]["BaselineRuleSets"][0][0].update(Fields=["CHANGED"]),
            lambda d: d["PinnedIbgeBootstrap"].update(GeneratorSeed=43),
        ]
        for edit in variants:
            with self.subTest(edit=str(edit)):
                data = copy.deepcopy(self.source_data)
                edit(data)
                self.source.write_text(json.dumps(data), encoding="utf8")
                with self.assertRaises((ValueError, KeyError, AssertionError)):
                    official.freeze(self.source, self.output)
                self.assertFalse(self.output.exists())
        self.write_source()
        with self.assertRaises(ValueError):
            official.freeze(self.source, self.root / "experiment.json")
        self.assertFalse((self.root / "experiment.json").exists())

    def test_missing_default_role_or_anchor_is_rejected_by_contract(self):
        self.write_source()
        official.freeze(self.source, self.output)
        result = official.read(self.output)
        self.assertEqual(result["PredeclaredCandidateCpMultiplicity"], 16)
        result["MuEstimationMethods"][0].pop("Anchor")
        self.assertNotEqual(result["MuEstimationMethods"][0],
                            {"Method": "DEFAULT", "Role": "WITNESS", "Anchor": True})


if __name__ == "__main__":
    unittest.main()
