"""Regression guards for the *real* production batch RunAsync V9 opt-in.

Source-level guards only. Physical SQL equivalence and TEST certification
remain blocked by the independent scientific admission gate.
"""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[2]
          / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs")


class V9RealRunAsyncNonPublicationContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        text = SOURCE.read_text(encoding="utf-8")
        cls.run = text.split("public async Task<ProbabilisticLinkageRunSummary> RunAsync(", 1)[1].split(
            "    private async Task CaptureBronzeSourcesAsync(", 1)[0]

    def test_v9_rejected_before_lock_and_run_creation_if_publication_requested(self):
        run = self.run
        self.assertIn("if (linkage is IV9NonPublishingFrozenCohortScorer)", run)
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", run)
        self.assertIn('v9GuardCommand.CommandText = "SELECT DB_NAME();"', run)
        self.assertIn('"JornadaHarness"', run)
        self.assertLess(run.index("if (linkage is IV9NonPublishingFrozenCohortScorer)"),
                        run.index("pipelineCoordinator.AcquireExclusiveJobAsync("))
        self.assertLess(run.index('v9GuardCommand.CommandText = "SELECT DB_NAME();"'),
                        run.index("CreateRunAndMaterializeUniverseAsync("))

    def test_full_global_grouping_precedes_first_batch_persistence(self):
        run = self.run
        self.assertIn("v9Grouped.PrepareAsync(runId, model, frozen, workCt)", run)
        self.assertIn("v9Grouped.ResolvePreparedAsync", SOURCE.read_text(encoding="utf-8"))
        self.assertLess(run.index("v9Grouped.PrepareAsync(runId, model, frozen, workCt)"),
                        run.index("await PersistBatchAsync("))
        self.assertIn("if (evaluated != eligible)", run)

    def test_v9_follows_real_nonpublishing_terminal_path(self):
        run = self.run
        self.assertIn("await CompleteWithoutPublicationAsync(", run)
        self.assertIn("await MarkTerminalAsync(", run)
        self.assertIn("if (request.Publish)", run)
        self.assertIn("await EnsureBatchPublicationProcedureAvailableAsync(ct)", run)
        self.assertIn("LinkageRunStatus.FALHOU", run)
        self.assertIn("LinkageRunStatus.CANCELADO", run)


if __name__ == "__main__":
    unittest.main()
