"""Ensure TEST uses one winner and never claims false production parity."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
V9 = ROOT / "src/Jornada.Linkage.Core/V9FittedSnapshot.cs"
TEST = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"
VALIDATION = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs"
PRODUCTION = ROOT / "src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs"
RULES = ROOT / "src/Jornada.Linkage.Runner/LinkageRuleSetReader.cs"

class TestProductionModelFormat(unittest.TestCase):
    def test_unique_winner_but_not_prod_sql_contract(self):
        validation = VALIDATION.read_text()
        workflow = WF.read_text()
        test = TEST.read_text()
        prod = PRODUCTION.read_text()
        rules = RULES.read_text()
        snap = V9.read_text()
        self.assertIn("selectedValidationSnapshotSha256 = scientific?.SnapshotSha256", validation)
        self.assertIn("v9-tf4-validated-operational-handoff.py verify", workflow)
        self.assertIn('assert hashlib.file_digest(stream,"sha256").hexdigest()==files[0].name.split(".")[0]', workflow)
        self.assertIn("validated-operational-model/*.operational.json", workflow)
        self.assertIn("args.Length != 2", test)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", test)
        self.assertNotIn("V9FittedSnapshotArtifact.ReadPinnedAsync", test)
        self.assertIn("EvaluateFrozenTestAsync", test)
        self.assertNotIn("SelectScientificWinner", test)
        self.assertIn('public const string SupportSchemaVersion = "V9_FITTED_SNAPSHOT_V3"', snap)
        for table in ("identidade.modelo_linkage", "identidade.parametro_linkage", "identidade.frequencia_linkage"):
            self.assertIn(table, prod)
        for table in ("identidade.linkage_ruleset", "identidade.linkage_ruleset_passe"):
            self.assertIn(table, rules)
        self.assertIn("LinkageRuleSetReader.TryLoadAsync", prod)
        self.assertNotIn("V9FittedSnapshotArtifact.ReadPinnedAsync", prod)
        self.assertNotIn("V9FittedSnapshot.Load", prod)
        self.assertNotIn("ProbabilisticLinkageBatchRunner.RunAsync", test)

    def test_parity_gate_before_test_generation_and_train(self):
        workflow = WF.read_text()
        prep = workflow.split("\n  prepare-test-100k:\n",1)[1].split("\n  physical-100k:\n",1)[0]
        train = workflow.split("\n  physical-100k:\n",1)[1].split("\n  validation-100k:\n",1)[0]
        for part in (prep, train):
            self.assertIn('if [[ "${{ inputs.test_required }}" == "true" ]]', part)
            self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", part)
        self.assertLess(prep.index("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"), prep.index("--people 100000"))
        self.assertLess(train.index("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"), train.index("--v9-calibrate"))
        self.assertNotIn("timeout-minutes:", workflow)

if __name__ == "__main__":
    unittest.main()
