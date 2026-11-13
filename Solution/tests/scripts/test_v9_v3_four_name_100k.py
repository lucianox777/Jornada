"""Static + isolated unit guards for the V3-only 100k four-name study."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


p = load("v9-v3-four-name-100k-plan.py", "v9_tf4_plan_test")
a = load("v9-v3-four-name-100k-audit.py", "v9_tf4_audit_test")


class V9FourName100kTests(unittest.TestCase):
    def base(self):
        tf0 = {"OptionId": p.TF0, "SmoothingAlpha": .5,
               "DecisionThreshold": .95, "TfWeight": 0, "UnitTfOptions": None,
               "UnpublishedTermPolicy": 0}
        return {
            "Calibration": {
                "MaximumParallelRuleSets": 0,
                "ModelOptions": [tf0, {"OptionId": "TF_ONE_MF_W1_M0_A0.5_T0.95"}],
                "ConflictMargins": [.5],
                "MaximumPhysicalRuleSets": 2, "MaximumComparedConfigurations": 24,
                "BaselineRuleSets": [[
                    {"PassId": n, "Fields": ["name_first"]}
                    for n in ("P_FIRST_BIRTH_YEAR", "P_LAST_M_PHON", "P_PHON_M_LAST")]],
                "Budget": {"MaximumObservedAcceptedFdrPoint": .01,
                           "StudyFingerprintSha256": "a" * 64},
                "StudyFingerprintSha256": "a" * 64,
                "SelectionPolicy": {"MaximumAcceptedFdrUpper": .01,
                                    "MaximumP95Milliseconds": 60000},
                "ObservationCapDeclaration": {
                    "ManifestSha256": "b" * 64, "ObservationsSha256": "c" * 64}},
            "MuEstimationMethods": [
                {"Method": "DEFAULT", "Role": "WITNESS", "Anchor": True},
                {"Method": "CLASSWISE_SUPPORT_BACKOFF"},
                {"Method": "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE",
                 "Role": "CANDIDATE",
                 "Parameters": {"ClassMassCoverage": [.99],
                                "MinimumObservedMPerCell": [5],
                                "MinimumObservedUPerCell": [5],
                                "RetainSupportedTail": [True],
                                "UBackoffJointMixtureWeights": [0, .25, .5, 1]}}],
            "MaximumBatches": 1, "PinnedIbgeBootstrap": {"People": 30000},
            "MaximumSearchConfigurations": 24,
            "PredeclaredCandidateCpMultiplicity": 8}

    def receipt(self, sha):
        return {"schema": p.BASE_SCHEMA, "technicalPlanSha256": sha,
                "physicalEvaluations": 24, "people": 100000, "knownTrainSeed": 42,
                "runtimeParallelismMode": "AUTO_0",
                "testUsed": False, "certified": False, "activated": False,
                "technicalCorpusManifestSha256": "b" * 64,
                "technicalCorpusObservationsSha256": "c" * 64,
                "sourceArtifactId": 13654549483,
                "sourceArtifactZipSha256": "d" * 64}

    def test_exact_81_states_no_duplicate_tf0_and_bilateral_only(self):
        options = p.grid(self.base()["Calibration"]["ModelOptions"][0])
        self.assertEqual(len(options), 81)
        self.assertEqual(len({x["OptionId"] for x in options}), 81)
        self.assertEqual(options[0]["OptionId"], p.TF0)
        self.assertIsNone(options[0]["UnitTfOptions"])
        self.assertEqual({x["UnpublishedTermPolicy"] for x in options[1:]}, {1})
        self.assertTrue(all(x["SharedSurnameMode"] == 0 for x in options[1:]))
        self.assertEqual({tuple(s["Weight"] for s in x["UnitTfOptions"])
                          for x in options[1:]},
                         {tuple(v) for v in __import__("itertools").product(p.STATES, repeat=4)}
                         - {(0, 0, 0, 0)})

    def test_one_plan_324_V3_only_and_provenance_bound(self):
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            bp, br = d / "source-plan.json", d / "source-receipt.json"
            bp.write_text(json.dumps(self.base()))
            br.write_text(json.dumps(self.receipt(p.digest(bp.read_bytes()))))
            output, receipt = d / "plan.json", d / "receipt.json"
            result = p.prepare(bp, br, output, receipt)
            plan = json.loads(output.read_text())
            self.assertEqual(result["planSha256"], p.digest(output.read_bytes()))
            self.assertEqual(result["physicalHypotheses"], 324)
            self.assertEqual(plan["MaximumBatches"], 1)
            self.assertEqual(plan["PredeclaredCandidateCpMultiplicity"], 324)
            self.assertEqual(plan["Calibration"]["ConflictMargins"], [.5, 2])
            self.assertEqual(plan["Calibration"]["MaximumPhysicalRuleSets"], 1)
            self.assertEqual(len(plan["Calibration"]["DeclaredSearchSpace"]["Catalogue"]), 3)
            self.assertEqual(len(plan["Calibration"]["ModelOptions"]), 81)
            self.assertEqual(len(plan["MuEstimationMethods"]), 1)
            self.assertEqual(plan["MuEstimationMethods"][0]["Method"],
                             "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE")
            self.assertEqual(plan["MuEstimationMethods"][0]["Parameters"]
                             ["UBackoffJointMixtureWeights"], [.5, 1])
            self.assertNotIn("Role", plan["MuEstimationMethods"][0])
            self.assertEqual(plan["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"], .01)
            self.assertEqual(plan["Calibration"]["SelectionPolicy"]["MaximumP95Milliseconds"], 60000)
            self.assertEqual(json.loads(br.read_text())["physicalEvaluations"], 24,
                             "source artifact must remain unchanged")
            with self.assertRaises(ValueError):
                p.prepare(bp, br, output, d / "another.json")

    def test_fail_closed_on_source_drift_and_invalid_p95(self):
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            source = d / "source.json"
            source.write_text(json.dumps(self.base()))
            bad = self.receipt("e" * 64)
            receipt = d / "receipt.json"
            receipt.write_text(json.dumps(bad))
            with self.assertRaisesRegex(ValueError, "source plan"):
                p.prepare(source, receipt, d / "plan.json", d / "out.json")
        self.assertTrue(a.p95_was_measured(
            {"Rejections": [], "p95Milliseconds": 6000}))
        self.assertFalse(a.p95_was_measured({
            "Rejections": ["FANOUT_P99_ACIMA_DO_ORCAMENTO"],
            "outcome": "MEASURED_NOT_ELIGIBLE",
            "p95Milliseconds": None}))
        self.assertFalse(a.p95_was_measured({
            "Rejections": ["SUPORTE_OBSERVADO_NO_NUCLEO_DE_MASSA_INSUFICIENTE"],
            "outcome": "MEASURED_NOT_ELIGIBLE", "p95Milliseconds": None}))
        self.assertFalse(a.p95_was_measured({
            "Rejections": ["FDR_INDEFINIDO_SEM_ACEITOS"],
            "outcome": "MEASURED_NOT_ELIGIBLE", "p95Milliseconds": None}))
        for row in ({"Rejections": [], "p95Milliseconds": None},
                    {"Rejections": ["FDR_INDEFINIDO_SEM_ACEITOS"],
                     "p95Milliseconds": None},
                    {"Rejections": [], "p95Milliseconds": float("nan")}):
            with self.assertRaisesRegex(ValueError, "AUDIT_FAIL_CLOSED"):
                a.p95_was_measured(row)

    def test_manual_workflow_and_scientific_gates(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        self.assertIn("workflow_dispatch:", workflow)
        self.assertIn("github.actor == github.repository_owner", workflow)
        self.assertIn("expected_sha", workflow)
        self.assertIn("security_preflight_approved", workflow)
        self.assertIn("--v9-calibrate", workflow)
        self.assertIn("scripts/v9-v3-four-name-100k-plan.py", workflow)
        self.assertIn("scripts/v9-v3-four-name-100k-audit.py", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertNotIn("  pull_request:", workflow)
        self.assertNotIn("  schedule:", workflow)
        self.assertIn("timeout-minutes:", workflow)
        self.assertIn("Remove disposable SQL even after failure", workflow)

    def test_manual_workflow_exposes_opt_in_replay_timing_without_relaxing_guards(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        self.assertIn("replay_timing_diagnostics:", workflow)
        self.assertIn("V9_REPLAY_TIMING_DIAGNOSTICS: ${{ inputs.replay_timing_diagnostics && '1' || '0' }}", workflow)
        self.assertIn("github.actor == github.repository_owner", workflow)
        self.assertIn("security_preflight_approved == true", workflow)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', workflow)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', workflow)
        self.assertIn("workflow_dispatch:", workflow)


    def test_324_timeout_matches_progress_margin_and_preserves_validation_budget(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        coordinator = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs").read_text()
        physical = workflow.split("- name: Run physical V9 CSharp on 324 V3-only four-name hypotheses", 1)[1].split("- name: Audit all 324", 1)[0]
        self.assertIn("timeout-minutes: 315", physical)
        self.assertIn("timeout-minutes: 360", workflow)
        self.assertIn("timeout-minutes: 40", workflow)
        self.assertIn("TimeSpan.FromMinutes(315) - elapsed - eta", coordinator)
        self.assertIn("margem_etapa_315min=", coordinator)
        self.assertNotIn("margem_etapa_305min=", coordinator)
        self.assertIn("security_preflight_approved == true", workflow)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', workflow)
        self.assertIn("V9_REPLAY_TIMING_DIAGNOSTICS:", workflow)


if __name__ == "__main__":
    unittest.main()
