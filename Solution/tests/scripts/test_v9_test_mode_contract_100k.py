"""Regression guards for PO-approved V9 TEST mode selection (GOV-DOC-01).

These checks protect isolation and single-winner semantics; they DO NOT
certify full production Runner parity or unlock the synthetic TEST gate.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
TEST_CLI = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"
SEALED = ROOT / "scripts/v9-tf4-sealed-test-preparation.py"


class TestV9TestModeContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.test_cli = TEST_CLI.read_text(encoding="utf-8")
        cls.sealed = SEALED.read_text(encoding="utf-8")

    def test_selector_has_two_mutually_exclusive_test_jobs(self):
        wf = self.workflow
        self.assertIn("test_required:", wf)
        self.assertIn("test_database:", wf)
        self.assertIn("inputs.test_required == true && inputs.test_database == ''", wf)
        self.assertIn("inputs.test_required == true && inputs.test_database != ''", wf)
        self.assertIn("test-100k:", wf)
        self.assertIn("test-database-readonly:", wf)

    def test_synthetic_is_new_sealed_corpus_before_train(self):
        wf = self.workflow
        prep = wf.split("\n  prepare-test-100k:\n", 1)[1].split("\n  physical-100k:\n", 1)[0]
        test = wf.split("\n  test-100k:\n", 1)[1].split("\n  test-database-readonly:\n", 1)[0]
        self.assertIn("--people 100000", prep)
        self.assertIn("v9-tf4-sealed-test-preparation.py", prep)
        self.assertIn("v9-tf4-sealed-test-preparation.py unseal", test)
        self.assertIn("needs: [validation-100k, prepare-test-100k]", test)
        self.assertIn("test-seed-20261113001-exposed", test)
        self.assertIn("testSeed", self.sealed)
        self.assertIn("len({lock[\"trainSeed\"], lock[\"validationSeed\"], lock[\"testSeed\"]}) == 3", self.sealed)

    def test_one_frozen_winner_no_refit(self):
        wf = self.workflow
        test = wf.split("\n  test-100k:\n", 1)[1].split("\n  test-database-readonly:\n", 1)[0]
        self.assertIn("validated-operational-model-", test)
        self.assertIn("configurationsEvaluated", test)
        self.assertIn('assert r["refitted"] is False', test)
        self.assertIn('assert r["trainPlanRead"] is False', test)
        self.assertIn('r["validationResultsRead"] is False', test)
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync", self.test_cli)
        self.assertIn("TotalComparedConfigurations: 1", self.test_cli)
        self.assertIn("EvaluateFrozenTestAsync", self.test_cli)

    def test_existing_database_does_not_generate_synthetic_data(self):
        wf = self.workflow
        external = wf.split("\n  test-database-readonly:\n", 1)[1]
        self.assertIn("V9_EXTERNAL_TEST_READONLY_CONNECTION", external)
        self.assertIn("--test-database \"$V9_EXTERNAL_DATABASE\"", external)
        self.assertIn('report["generatedSyntheticData"] is False', external)
        self.assertIn('report["independentScientificTest"] is False', external)
        self.assertIn('report["inputReadOnly"] is True', external)
        self.assertIn("V9ExternalDatabaseReadOnlyTest.RunAsync", self.test_cli)

    def test_candidate_winner_is_not_active_model_or_new_test_selection(self):
        cli = self.test_cli
        scorer = (ROOT / "src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs").read_text(encoding="utf-8")
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync(winnerFile)", cli)
        self.assertIn("V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winnerSha)", cli)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", cli)
        self.assertNotIn("GetActiveModelAsync(", cli)
        self.assertNotIn("GetModelByVersionAsync(", cli)
        self.assertIn("TotalComparedConfigurations: 1", cli)
        self.assertIn("operationalModelActive = false", cli)
        self.assertIn("operationalModelPublished = false", cli)
        self.assertIn("activated = false", cli)
        self.assertIn("GetActiveModelAsync(CancellationToken ct) =>", scorer)
        self.assertIn("Modelo ativo é proibido no V9 agrupado TEST_ONLY.", scorer)
        self.assertIn("model.BlockingContract?.ProjectionSchemaVersion != winner.Payload.ProjectionSchemaVersion", scorer)
        self.assertIn("model.BlockingContract?.ProjectionFingerprintSha256 != winner.Payload.ProjectionFingerprintSha256", scorer)

    def test_runner_capacity_limit_cannot_be_silently_bypassed(self):
        scorer = (ROOT / "src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs").read_text(encoding="utf-8")
        runner = (ROOT / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs").read_text(encoding="utf-8")
        self.assertIn("public const int MaximumSyntheticObservations = 512;", scorer)
        self.assertIn("frozenRows.Count is < 2 or > MaximumSyntheticObservations", scorer)
        self.assertIn("V9PinnedSyntheticGraphCohortRunScorer.MaximumSyntheticObservations", runner)
        self.assertIn("IV9NonPublishingFrozenCohortScorer", runner)
        self.assertIn("JornadaHarness", runner)

    def test_fail_closed_parity_gate_remains_until_end_to_end_proof(self):
        wf = self.workflow
        self.assertGreaterEqual(wf.count("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"), 2)
        self.assertIn("productionRunnerFullPipelineParityCertified = false", self.test_cli)


if __name__ == "__main__":
    unittest.main()
