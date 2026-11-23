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
        cls.run_source = text.split("public async Task<ProbabilisticLinkageRunSummary> RunAsync(", 1)[1].split(
            "    private async Task CaptureBronzeSourcesAsync(", 1)[0]

    def test_v9_rejected_before_lock_and_run_creation_if_publication_requested(self):
        run = self.run_source
        self.assertIn("if (linkage is IV9NonPublishingFrozenCohortScorer)", run)
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", run)
        self.assertIn('v9GuardCommand.CommandText = "SELECT DB_NAME();"', run)
        self.assertIn('"JornadaHarness"', run)
        self.assertLess(run.index("if (linkage is IV9NonPublishingFrozenCohortScorer)"),
                        run.index("pipelineCoordinator.AcquireExclusiveJobAsync("))
        self.assertLess(run.index('v9GuardCommand.CommandText = "SELECT DB_NAME();"'),
                        run.index("CreateRunAndMaterializeUniverseAsync("))

    def test_frozen_cohort_rejects_all_partial_scopes_before_opening_sql(self):
        guard = self.run_source.split("if (linkage is IV9NonPublishingFrozenCohortScorer)", 1)[1]
        guard = guard.split("await operationalSql.OpenAsync(ct)", 1)[0]
        for field in ("PessoaObservacaoId", "GestorCodigo", "Since", "MaxRecords"):
            with self.subTest(field=field):
                self.assertIn(f"request.{field} is not null", guard)

    def test_full_global_grouping_precedes_first_batch_persistence(self):
        run = self.run_source
        self.assertIn("v9Grouped.PrepareAsync(runId, model, frozen, workCt)", run)
        self.assertRegex(SOURCE.read_text(encoding="utf-8"),
                         r"linkage is IV9NonPublishingFrozenCohortScorer (\w+)\s+"
                         r"\? await \1\.ResolvePreparedAsync\(runId, row.PessoaObservacaoId, "
                         r"row.Observation, model.ModelId, token\)")
        self.assertLess(run.index("v9Grouped.PrepareAsync(runId, model, frozen, workCt)"),
                        run.index("await PersistBatchAsync("))
        self.assertIn("if (evaluated != eligible)", run)

    def test_v9_follows_real_nonpublishing_terminal_path(self):
        run = self.run_source
        self.assertIn("await CompleteWithoutPublicationAsync(", run)
        self.assertIn("await MarkTerminalAsync(", run)
        self.assertIn("if (request.Publish)", run)
        self.assertIn("await EnsureBatchPublicationProcedureAvailableAsync(ct)", run)
        self.assertIn("LinkageRunStatus.FALHOU", run)
        self.assertIn("LinkageRunStatus.CANCELADO", run)


if __name__ == "__main__":
    unittest.main()
