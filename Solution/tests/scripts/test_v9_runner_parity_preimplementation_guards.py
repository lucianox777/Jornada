"""V9 TEST_ONLY: static regression guards; NOT a scientific parity certificate.

These checks assert fail-closed boundaries while productive V9 RunAsync
integration remains unimplemented. They cannot replace disposable SQL E2E.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "src/Jornada.Linkage.Runner"
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class V9RunnerParityPreimplementationGuards(unittest.TestCase):
    def test_productive_v9_dispatch_still_fails_closed(self):
        runner = (RUNNER / "ProbabilisticLinkageBatchRunner.cs").read_text()
        main = runner.split("public async Task<ProbabilisticLinkageRunSummary> RunAsync(", 1)[1]
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", main)
        self.assertLess(
            main.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"),
            main.index("pipelineCoordinator.AcquireExclusiveJobAsync("),
        )
        self.assertIn("request.ModelVersion == 9", main)
        self.assertIn("Linkage:V9SelectionFile", main)

    def test_fixture_scorer_is_not_mistaken_for_certified_100k(self):
        scorer = (RUNNER / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        self.assertIn("public const int MaximumSyntheticObservations = 512;", scorer)
        self.assertIn("model.Version != 1", scorer)
        self.assertIn("V9FrozenCohortSqlCandidateGraph.ReadAsync(", scorer)
        self.assertIn("V9OperationalBlockingChaveCohortParity.VerifyAsync(", scorer)
        self.assertLess(
            scorer.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("),
            scorer.index("ResolvePinnedV9FrozenTestCohortAsync("),
        )
        self.assertIn("operational.CanonicalEdgesSha256", scorer)
        self.assertIn("physical.CanonicalEdgesSha256", scorer)
        self.assertIn("EnsureHarnessAndAnchorAsync(connection, ct)", scorer)

    def test_operational_oracle_rejects_non_harness_and_checks_gold_manifest(self):
        oracle = (RUNNER / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        self.assertIn('SELECT DB_NAME();', oracle)
        self.assertIn('"JornadaHarness"', oracle)
        self.assertIn("BlockingProjectionCandidateQueryBuilder", oracle)
        self.assertIn("identidade.blocking_chave", oracle)
        self.assertIn("gold.pessoa", oracle)
        self.assertIn("expectedManifest.SetEquals(seen)", oracle)
        self.assertIn("goldCount != frozenRows.Count", oracle)
        self.assertIn("ProductionGoldCertified = false", oracle)

    def test_readonly_receipt_and_workflow_cannot_claim_certification(self):
        audit = (RUNNER / "V9OperationalCandidateGraphParityAudit.cs").read_text()
        readonly = (RUNNER / "V9ReadOnlyTestBatchRunner.cs").read_text()
        workflow = WORKFLOW.read_text()
        self.assertIn("ProductionBatchPipelineCertified: false", audit)
        self.assertIn("receipt.ProductionBatchPipelineCertified", readonly)
        self.assertIn("alternate.ProductionBatchPipelineCertified", readonly)
        self.assertEqual(workflow.count("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED") >= 2, True)


if __name__ == "__main__":
    unittest.main()
