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

    def test_reverted_tf4_search_space_contract_is_explicit(self):
        # Regression gate after #1865/#1879: do not silently reintroduce
        # fractional PF values or drop the second conflict margin.
        self.assertEqual(tuple(p.STATES), (0, 0.5, 1))
        self.assertEqual(tuple(p.MARGINS), (0.5, 2))
        options = p.grid(self.base()["Calibration"]["ModelOptions"][0])
        self.assertEqual(len(options), 81)
        self.assertEqual(len(options) * len(p.LAMBDAS) * len(p.MARGINS), 324)
        self.assertEqual(set(p.LAMBDAS), {0.5, 1})
        self.assertFalse(any("Q125" in x["OptionId"] or "Q250" in x["OptionId"]
                             or "Q375" in x["OptionId"] for x in options))
        self.assertEqual(sum(x["OptionId"] == p.TF0 for x in options), 1)

    def test_unified_calibration_json_is_versioned_into_tf_plan(self):
        config = json.loads((ROOT / "config/calibration/v9-synthetic-sampling.json").read_text())
        self.assertEqual(config["syntheticPopulation"], 100000)
        self.assertEqual(config["sampling"],
                         {"trainPercent": 60, "validationPercent": 20, "testPercent": 20})
        self.assertEqual(config["tfNominalPolicy"]["contextPopulation"], 11911337)
        self.assertEqual(config["tfNominalPolicy"]["unpublishedOccurrenceCount"], 20)
        options = p.grid(self.base()["Calibration"]["ModelOptions"][0],
                         config["tfNominalPolicy"])
        self.assertEqual({x["ContextPopulation"] for x in options}, {11911337})
        self.assertEqual({x["UnpublishedOccurrenceCount"] for x in options}, {20})
        self.assertEqual(options[0]["UnpublishedTermPolicy"], 0)
        self.assertEqual({x["UnpublishedTermPolicy"] for x in options[1:]}, {1})

    def test_unified_config_receipt_and_fingerprint_are_bound(self):
        config_path = ROOT / "config/calibration/v9-synthetic-sampling.json"
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            bp, br = d / "source-plan.json", d / "source-receipt.json"
            bp.write_text(json.dumps(self.base()))
            br.write_text(json.dumps(self.receipt(p.digest(bp.read_bytes()))))
            output, receipt = d / "plan.json", d / "receipt.json"
            p.prepare(bp, br, output, receipt, config_path)
            plan = json.loads(output.read_text())
            evidence = json.loads(receipt.read_text())
            config = json.loads(config_path.read_text())
            self.assertEqual(evidence["tfPolicySha256"], p.digest(config_path.read_bytes()))
            self.assertEqual(evidence["calibratorConfiguration"], config)
            self.assertEqual(evidence["tfUnpublishedPolicy"], "TF0_REJECT_ACTIVE_TF_ABSTAIN_CANDIDATE")
            self.assertEqual(plan["Calibration"]["ModelOptions"][1]["ContextPopulation"],
                             config["tfNominalPolicy"]["contextPopulation"])
            self.assertEqual(plan["Calibration"]["ModelOptions"][1]["UnpublishedOccurrenceCount"],
                             config["tfNominalPolicy"]["unpublishedOccurrenceCount"])
            self.assertEqual(plan["Calibration"]["ModelOptions"][1]["UnpublishedTermPolicy"], 1)

    def test_people_override_is_frozen_and_checked_against_source(self):
        config_path = ROOT / "config/calibration/v9-synthetic-sampling.json"
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            bp, br = d / "source-plan.json", d / "source-receipt.json"
            bp.write_text(json.dumps(self.base()))
            source = self.receipt(p.digest(bp.read_bytes()))
            source["people"] = 120000
            br.write_text(json.dumps(source))
            output, receipt = d / "plan.json", d / "receipt.json"
            with self.assertRaises((ValueError, AssertionError)):
                p.prepare(bp, br, output, receipt, config_path)
            self.assertFalse(output.exists())
            p.prepare(bp, br, output, receipt, config_path, expected_people=120000)
            evidence = json.loads(receipt.read_text())
            self.assertEqual(evidence["configuredSyntheticPopulation"], 100000)
            self.assertEqual(evidence["effectiveSyntheticPopulation"], 120000)
            self.assertTrue(evidence["populationOverrideApplied"])

    def test_standard_workflow_preserves_people_override_and_unified_default(self):
        workflow = (ROOT.parent / ".github/workflows/v9-standard-linear-100k.yml").read_text()
        self.assertIn("synthetic_population:", workflow)
        self.assertIn('V9_PEOPLE_OVERRIDE=(--people "$V9_SYNTHETIC_POPULATION")', workflow)
        self.assertEqual(workflow.count('--calibrator-config config/calibration/v9-synthetic-sampling.json'), 3)
        self.assertIn('effective_people="${V9_SYNTHETIC_POPULATION:-$configured_people}"', workflow)
        self.assertEqual(workflow.count('--expected-people "$effective_people"'), 2)
        self.assertNotIn('--expected-people "${V9_SYNTHETIC_POPULATION:-100000}"', workflow)

    def test_invalid_scientific_policy_rejected_before_output(self):
        original = json.loads((ROOT / "config/calibration/v9-synthetic-sampling.json").read_text())
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            bp, br = d / "source-plan.json", d / "source-receipt.json"
            bp.write_text(json.dumps(self.base()))
            br.write_text(json.dumps(self.receipt(p.digest(bp.read_bytes()))))
            for change in ("negative_sampling", "missing_nominal_unit", "wrong_u_policy"):
                config = json.loads(json.dumps(original))
                if change == "negative_sampling":
                    config["sampling"].update(trainPercent=-10, validationPercent=30, testPercent=80)
                elif change == "missing_nominal_unit":
                    config["tfNominalPolicy"]["nominalUnits"].pop()
                else:
                    config["tfNominalPolicy"]["uEstimation"] = "UNSUPPORTED"
                cfg = d / "config.json"
                cfg.write_text(json.dumps(config))
                with self.subTest(change=change):
                    with self.assertRaises(ValueError):
                        p.prepare(bp, br, d / "plan.json", d / "receipt.json", cfg)
                    self.assertFalse((d / "plan.json").exists())
                    self.assertFalse((d / "receipt.json").exists())

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

    def test_train_100k_is_generated_locally_before_fitting_without_corpus_ref(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        train = workflow.split("\n  physical-100k:\n", 1)[1].split("\n  audit-100k:\n", 1)[0]
        self.assertIn("Generate known-seed 100k clean TRAIN", train)
        self.assertIn("src/Jornada.Linkage.SyntheticCorpus -- generate", train)
        self.assertIn("--error-profile clean --people 100000 --seed 42", train)
        self.assertIn('--out "$root/people-100k"', train)
        self.assertIn("scripts/v9-scale-100k-technical-plan.py", train)
        self.assertIn('scripts/v9-100k-load-frozen-ibge.sh', train)
        self.assertNotIn("Admit pinned persistent REF 100k", train)
        self.assertNotIn("v9-synthetic-corpus-100000", train)
        self.assertNotIn("V9_TRAIN_INPUT_FROM_PERSISTED_REF", train)
        self.assertLess(train.index("SyntheticCorpus -- generate"),
                        train.index("Run physical V9 CSharp on 324"))
        self.assertIn("Prepare byte-verified TRAIN evidence", train)
        self.assertIn("Dispose temporary TRAIN bases", train)

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
        self.assertNotIn("timeout-minutes:", workflow)
        self.assertIn("Remove disposable SQL even after failure", workflow)

    def test_minimal_progress_without_relaxing_guards(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        self.assertIn("V9_PHYSICAL_PROGRESS_LOG: '1'", workflow)
        self.assertNotIn("replay_timing_diagnostics:", workflow)
        self.assertNotIn("V9_REPLAY_TIMING_DIAGNOSTICS", workflow)
        self.assertIn("github.actor == github.repository_owner", workflow)
        self.assertIn("security_preflight_approved == true", workflow)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', workflow)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', workflow)
        self.assertIn("workflow_dispatch:", workflow)

    def test_physical_job_and_audit_are_distinct_without_local_deadlines(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        coordinator = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs").read_text()
        train = workflow.split("\n  physical-100k:\n", 1)[1].split("\n  audit-100k:\n", 1)[0]
        audit = workflow.split("\n  audit-100k:\n", 1)[1].split("\n  validation-100k:\n", 1)[0]
        self.assertNotIn("timeout-minutes:", workflow)
        self.assertIn("Run physical V9 CSharp on 324", train)
        self.assertIn("v9-v3-four-name-100k-preaudit.py prepare", train)
        self.assertIn("v9-v3-four-name-100k-preaudit.py verify", audit)
        self.assertIn("v9-v3-four-name-100k-audit.py", audit)
        self.assertNotIn("python3 scripts/v9-v3-four-name-100k-audit.py \\", train)
        self.assertIn("needs: audit-100k", workflow)
        self.assertIn("V9_PHYSICAL_PROGRESS_LOG: '1'", train)
        self.assertIn("{done}/{physicalTotal} - {sha[..Math.Min(12, sha.Length)]}", coordinator)
        self.assertNotIn("V9_PHYSICAL_STEP_START_UTC", workflow)
        self.assertNotIn("physicalDeadlineUtc", coordinator)
        self.assertNotIn("AddMinutes(340)", coordinator)
        self.assertNotIn("Stopwatch", coordinator)
        self.assertNotIn("margem_etapa_305min=", coordinator)
        self.assertNotIn("ETA_min=", coordinator)
        self.assertIn("security_preflight_approved == true", workflow)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', workflow)


if __name__ == "__main__":
    unittest.main()
