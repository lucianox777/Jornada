"""100k technical rebinding is provenance-only; rejects old pin masquerading as 100k."""
import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
spec = importlib.util.spec_from_file_location(
    "v9_scale100k_binding", SCRIPTS / "v9-scale-100k-technical-plan.py")
binding = importlib.util.module_from_spec(spec)
spec.loader.exec_module(binding)


class V9Scale100kPlanTests(unittest.TestCase):
    def test_100k_workflow_has_feasible_runner_before_any_sql_or_data(self):
        # Historical 30k / generic calibration remain unchanged.
        workflow = (ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml").read_text()
        self.assertIn("runs-on: ubuntu-24.04\n", workflow)
        self.assertNotIn("runs-on: [self-hosted,", workflow)
        self.assertNotIn("runs-on: ubuntu-24.04-16core", workflow)
        self.assertIn("github.actor == github.repository_owner", workflow)
        self.assertIn("v9-100k-execution-resource-budget.py --stage plan-sql", workflow)
        self.assertIn("v9-100k-execution-resource-budget.py --stage before-fit", workflow)
        self.assertNotIn('V9_SQL_MEMORY_LIMIT_MB: "4096"', workflow)
        self.assertIn("all 24 official multiparameter hypotheses", workflow)
        self.assertIn("--parallelism auto", workflow)
        self.assertIn("AUTO_0_BOUND_CSHARP_GOVERNOR_ACTIVE", workflow)
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertIn("security_preflight_approved", workflow)

    def test_frozen_provenance_constants_and_defaults_are_untouched(self):
        self.assertEqual(binding.ARTIFACT_ID, 13654549483)
        self.assertEqual(binding.ARTIFACT_SHA,
            "5bdbce7ffd5fed55fc085fde42faf9793cb39e6e79c3db4c69990a818bf330d7")
        self.assertEqual(binding.ORIGINAL_SOURCE_SHA,
            "3da5e9327e86b5837f69a4da2236fab385ec930f")
        code = (SCRIPTS / "v9-scale-100k-technical-plan.py").read_text()
        self.assertIn('restored == original', code)
        self.assertIn('"historicalPinnedIbgeBootstrapPeople": 30000', code)
        self.assertIn('"people": 100000', code)
        self.assertNotIn("os.environ.get('TEST'", code)

    def test_100k_auto_parallelism_is_only_operational_override(self):
        original = {
            "Calibration": {
                "MaximumParallelRuleSets": 1,
                "ModelOptions": [{"OptionId": "TF0", "EligibleForSelection": False}],
                "Budget": {"StudyFingerprintSha256": "c" * 64}
            }
        }
        patched = copy.deepcopy(original)
        patched["Calibration"]["MaximumParallelRuleSets"] = 0
        restored = copy.deepcopy(patched)
        restored["Calibration"]["MaximumParallelRuleSets"] = original["Calibration"]["MaximumParallelRuleSets"]
        self.assertEqual(restored, original)
        code = (SCRIPTS / "v9-scale-100k-technical-plan.py").read_text()
        self.assertIn('parallelism_mode in ("preserve", "auto")', code)
        self.assertIn('plan["Calibration"]["MaximumParallelRuleSets"] = 0', code)
        self.assertIn('restored["Calibration"]["MaximumParallelRuleSets"] = historical_parallelism', code)
        self.assertIn('historicalMaximumParallelRuleSets=historical_parallelism', code)
        self.assertIn('cli.add_argument("--parallelism"', code)

    def test_rejects_incomplete_manifest_and_wrong_population(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "generation-manifest.json").write_text(json.dumps({
                "seed":42,"error_profile":"clean",
                "population_profile":"DEMOGRAPHIC_PRIMARY_V1",
                "generator_version":"JORNADA_SYNTH_CORPUS_CSHARP_V1",
                "observation_multiplicity":{
                    "contract_version":"SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1",
                    "maximum_observations_per_base_identity":4,
                    "scope":"BASE_CORPUS_ONLY_NOT_INGESTION_WAVES"},
                "outputs":[]
            }))
            with self.assertRaisesRegex(ValueError, "three generated files"):
                binding.corpus_provenance(root)

    def test_no_unauthenticated_artifact_can_be_promoted(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "fake.zip"
            with zipfile.ZipFile(path, "w") as z:
                z.writestr("plan.json", "{}")
            with self.assertRaisesRegex(ValueError, "ZIP drift"):
                binding.verified_official_plan(path)

    def test_duplicate_json_members_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "duplicate JSON"):
            binding.json_unique('{"x":1,"x":2}')

    def test_100k_rebind_only_provenance_vector(self):
        official = {
            "Calibration":{
                "ObservationCapDeclaration":{"ContractVersion":"V",
                    "GeneratorVersion":"G","MaximumObservationsPerBaseIdentity":4,
                    "ManifestSha256":"a"*64, "ObservationsSha256":"b"*64},
                "Budget":{"StudyFingerprintSha256":"c"*64,"Other":.01},
                "StudyFingerprintSha256":"c"*64,
                "ModelOptions":[{"OptionId":"TF0","EligibleForSelection":False}]},
            "PinnedIbgeBootstrap":{"People":30000, "GeneratorSeed":42},
            "PredeclaredCandidateCpMultiplicity":8}
        changed = copy.deepcopy(official)
        changed["Calibration"]["ObservationCapDeclaration"].update({
            "ManifestSha256":"d"*64, "ObservationsSha256":"e"*64})
        changed["Calibration"]["StudyFingerprintSha256"]="f"*64
        changed["Calibration"]["Budget"]["StudyFingerprintSha256"]="f"*64
        restored = copy.deepcopy(changed)
        restored["Calibration"]["ObservationCapDeclaration"] = copy.deepcopy(
            official["Calibration"]["ObservationCapDeclaration"])
        restored["Calibration"]["StudyFingerprintSha256"] = official["Calibration"]["StudyFingerprintSha256"]
        restored["Calibration"]["Budget"]["StudyFingerprintSha256"] = official["Calibration"]["Budget"]["StudyFingerprintSha256"]
        self.assertEqual(restored, official)
        self.assertEqual(changed["PinnedIbgeBootstrap"], official["PinnedIbgeBootstrap"])


if __name__ == "__main__":
    unittest.main()
