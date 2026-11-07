"""Pure fake-data regression: no real identity or SQL needed for the 30k comparator."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

P = Path(__file__).resolve().parents[2] / "scripts" / "v9-compare-physical-parallel-serial.py"
spec = importlib.util.spec_from_file_location("v9_determinism_compare", P)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

R = "a" * 64


def build(parallelism, *, changed=None):
    plan = {"StudyFingerprintSha256": "b" * 64,
            "MaximumParallelRuleSets": parallelism,
            "ModelOptions": [{"OptionId": "O" + str(i)} for i in range(45)]}
    rows, fits, selection = [], [], []
    for i in range(45):
        ident = R + ":O" + str(i)
        for margin in (0.5, 2, 4):
            row = {
                "OptionId": ident, "ConflictMargin": margin,
                "CandidateGrouping": "BLIND_FS_COMPLETE_LINK_CANDIDATES",
                "EligibleSourceObservationsBeforeBlocking": 120,
                "TrueAcceptedSourceObservations": 80,
                "FalseAcceptedSourceObservations": 3,
                "Conflicts": 2, "Unresolved": 4,
                "ReasonsByResolveStatus": {"CONFLICT": 2, "UNRESOLVED": 4}}
            rows.append(row)
            fits.append({"OptionId": ident, "ConflictMargin": margin,
                         "Snapshot": {"ContentSha256": "c" * 64}})
            selection.append({"Option": {"OptionId": ident + ":m" + str(margin)},
                              "Eligible": False,
                              "ExclusionReason": "NON_CERTIFIED"})
    study = {
        "PlanSha256": "0" * 64 if parallelism == 0 else "1" * 64,
        "TrainInputSha256": "3" * 64,
        "ValidationInputSha256": "4" * 64,
        "SourceMeasurements": rows, "FrozenConfigurations": fits,
        "Rejections": [], "TotalCandidateRuleSets": 1,
        "Pareto": {"Status": "NO_ELIGIBLE_CONFIGURATION",
                   "InitialSuggestedOptionId": None, "Table": selection}}
    manifest = {"Seed": 42, "GenerationHypothesesFingerprint": "5" * 64}
    if changed is not None:
        changed(plan, study, manifest)
    return plan, study, manifest


def pack(path, plan, study, manifest):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("study-tf/plan.json", json.dumps(plan))
        z.writestr("study-tf/study.json", json.dumps(study))
        z.writestr("people-30k/generation-manifest.json", json.dumps(manifest))


class FrozenPhysicalComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def data(self, parallelism, changed=None):
        p = Path(self.temp.name) / (str(parallelism) + ("-mut" if changed else "") + ".zip")
        pack(p, *build(parallelism, changed=changed))
        return m.compress(p)

    def test_actions_zip_root_name_fallback_is_supported(self):
        plan, study, manifest = build(0)
        path = Path(self.temp.name) / "flat.zip"
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr("plan.json", json.dumps(plan))
            z.writestr("study.json", json.dumps(study))
            z.writestr("generation-manifest.json", json.dumps(manifest))
        audit = m.compress(path)
        self.assertEqual(len(audit["Rows"]), 135)

    def test_runtime_parallelism_changes_plan_sha_but_not_scientific_outcome(self):
        parallel = self.data(0)
        serial = self.data(1)
        self.assertNotEqual(parallel["PlanSha256"], serial["PlanSha256"])
        self.assertEqual(parallel["RuntimeParallelism"], 0)
        self.assertEqual(serial["RuntimeParallelism"], 1)
        self.assertEqual(m.compare(parallel, serial), [])
        self.assertEqual(len(parallel["Rows"]), 135)
        self.assertEqual(len(parallel["FittedKeys"]), 135)

    def test_changes_in_tp_fp_fn_conflicts_pending_and_fit_are_detected(self):
        def mutate(plan, study, manifest):
            row = study["SourceMeasurements"][0]
            row["TrueAcceptedSourceObservations"] += 1
            row["FalseAcceptedSourceObservations"] += 1
            row["Conflicts"] += 1
            row["Unresolved"] += 1
            row["ReasonsByResolveStatus"]["CONFLICT"] += 1
            study["FrozenConfigurations"][0]["Snapshot"]["ContentSha256"] = "d" * 64
            study["Pareto"]["Status"] = "CHANGED"
        differences = m.compare(self.data(0), self.data(1, mutate))
        for name in ("TP", "FP", "FN", "Conflicts", "Unresolved",
                     "ReasonsByResolveStatus", "SnapshotContentSha256", "ParetoStatus"):
            self.assertTrue(any(name in row for row in differences), name)

    def test_manifest_seed_drift_blocks(self):
        changed = lambda p, s, mfest: mfest.update(Seed=43)
        self.assertTrue(any("Seed" in row for row in
                            m.compare(self.data(0), self.data(1, changed))))

    def test_missing_row_or_fit_fails_closed(self):
        def missing_row(p, s, mfest):
            s["SourceMeasurements"].pop()
        def missing_fit(p, s, mfest):
            s["FrozenConfigurations"].pop()
        for modifier in (missing_row, missing_fit):
            with self.subTest(modifier=modifier):
                with self.assertRaises(ValueError):
                    self.data(1, modifier)

    def test_duplicate_and_unstable_option_id_rejected(self):
        def duplicate(p, s, mfest):
            s["SourceMeasurements"][-1] = s["SourceMeasurements"][0]
        def invalid_id(p, s, mfest):
            s["SourceMeasurements"][0]["OptionId"] = "MISSING_RULESET"
        for modifier in (duplicate, invalid_id):
            with self.subTest(modifier=modifier):
                with self.assertRaises(ValueError):
                    self.data(1, modifier)


if __name__ == "__main__":
    unittest.main()
