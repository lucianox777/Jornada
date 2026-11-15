"""Fail-closed contract for the opt-in 200-observation full-schema RunAsync smoke.

This is not scientific TEST, and it must not become a shortcut around the
production parity gate or the immutable one-use holdout.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parent
WF = REPO / ".github/workflows/v9-test-fullschema-runasync-smoke.yml"
TF4 = REPO / ".github/workflows/v9-v3-four-name-100k.yml"
TEST = ROOT / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs"


class V9FullSchemaNonPublishingContract(unittest.TestCase):
    def test_physical_smoke_is_isolated_from_real_test(self):
        wf = WF.read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", wf)
        caller = (REPO / ".github/workflows/v9-operational-model-sql-smoke.yml").read_text(encoding="utf-8")
        self.assertIn("full-schema-runasync-nonpublish:", caller)
        self.assertIn("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL", caller)
        self.assertIn("V9FullSchemaNonPublishingRunnerTests.Full_schema_RunAsync_200", caller)
        self.assertIn("workflow_dispatch:", wf)
        self.assertIn("timeout-minutes: 45", wf)
        self.assertIn("v9-tf-start-disposable-sql.sh", wf)
        self.assertIn("database/Jornada_Fase1_v3.70.sql", wf)
        self.assertIn("20261114_V9_Operational_Schema_Readonly_Preflight.sql", wf)
        self.assertIn("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL", wf)
        self.assertIn("dotnet test tests/Jornada.Tests/Jornada.Tests.csproj", wf)
        self.assertIn("test_corpus_opened=false", wf)
        self.assertIn("scientific_test_100k=BLOCKED", wf)
        self.assertNotIn("V9_TF4_TEST_PRIVATE_KEY_B64", wf)
        self.assertNotIn("TEST_PRIVATE_KEY", wf)
        self.assertNotIn("gh workflow run", wf)
        self.assertNotIn("Publish=true", wf)

    def test_real_runasync_is_called_but_never_publishes(self):
        src = TEST.read_text(encoding="utf-8")
        self.assertIn("ProbabilisticLinkageBatchRunner MakeRunner(", src)
        self.assertIn(".RunAsync(Request(), CancellationToken.None)", src)
        self.assertIn("LinkageRunType.MODEL_VALIDATION", src)
        self.assertIn("Guid.NewGuid(), false", src)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", src)
        self.assertIn("CompleteTf4OperationalWinner()", src)
        self.assertIn("Assert.ThrowsAsync<InvalidDataException>", src)
        self.assertIn("V9PinnedSyntheticGraphCohortRunScorer(", src)
        self.assertIn("Array.Empty<(long A, long B)>()", src)
        self.assertIn("unsafeRequest = Request() with { Publish = true }", src)
        self.assertIn("new (long A, long B)[] { (1, 2) }", src)
        self.assertNotIn("PinnedGloballyGroupedV9Decisions", src)
        self.assertNotIn("var byName = frozen.Decisions.ToDictionary", src)
        self.assertIn("AssertRunRowsAsync(connection, (Guid)negativeId!, \"FALHOU\", 0)", src)
        self.assertIn("IdentityComparison.NormalizeText(", src)
        self.assertIn("V9_SYNTHETIC_SECOND_BATCH_INJECTED_FAILURE", src)
        self.assertIn('"CONCLUIDO_SEM_PUBLICACAO"', src)
        self.assertIn('"FALHOU"', src)
        self.assertIn("ReadPublicationFingerprintAsync", src)
        self.assertIn("JornadaHarness", src)
        self.assertIn("CREATE DATABASE", (REPO / ".github/workflows/v9-test-fullschema-runasync-smoke.yml").read_text())
        self.assertNotIn("UpdateRef", src)
        self.assertNotIn("PublishAsync(", src)
        self.assertNotIn("test_required=true", src)

    def test_runasync_captures_entire_real_sql_universe_before_any_batch_persistence(self):
        runner = (ROOT / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs").read_text()
        scorer = (ROOT / "src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        self.assertIn("IV9NonPublishingFrozenCohortScorer", runner)
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", runner)
        self.assertIn('"JornadaHarness"', runner)
        self.assertLess(runner.index("await v9Grouped.PrepareAsync(runId, model, frozen, workCt)"),
                        runner.index("while (evaluated < eligible)"))
        self.assertIn("v9.ResolvePreparedAsync(runId, row.PessoaObservacaoId", runner)
        self.assertIn("IV9NonPublishingFrozenCohortScorer : IProbabilisticIdentityLinkage", scorer)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", scorer)
        self.assertIn("preparedRunId = runId", scorer)
        self.assertIn("pinned.ReplayTest(cohort, edges)", scorer)
        self.assertIn("syntheticEdges = explicitSyntheticEdges.ToArray()", scorer)
        self.assertIn("if (!consumed.TryAdd(pessoaObservacaoId, 0))", scorer)
        self.assertIn("SameObservation(item.Observation, observation)", scorer)
        self.assertIn('return Task.FromResult(item.Decision)', scorer)
        self.assertIn('"JornadaHarness"', scorer)
        self.assertIn('status=N\'RASCUNHO\'', scorer)
        self.assertNotIn("PublishAsync(", scorer)
        self.assertNotIn("INSERT INTO ", scorer)
        self.assertNotIn("UPDATE identidade.", scorer)
        self.assertNotIn("GetActiveModelAsync(ct)", runner)

    def test_sql_positive_crossbatch_matches_worker_and_stays_disposable(self):
        sql_graph = (ROOT / "src/Jornada.Linkage.Runner/V9FrozenCohortSqlCandidateGraph.cs").read_text()
        scorer = (ROOT / "src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        fixture = TEST.read_text()
        self.assertIn("CREATE TABLE #v9_run_sig(", sql_graph)
        self.assertIn("SqlBulkCopy(", sql_graph)
        self.assertIn("SELECT DISTINCT a.obs_id,b.obs_id", sql_graph)
        self.assertIn('pass.PassId + "\\u001e" + string.Join("\\u001f", values)', sql_graph)
        self.assertIn("MaximumPairIncidences = 25_000", sql_graph)
        self.assertIn("JornadaHarness", sql_graph)
        self.assertNotIn("INSERT INTO identidade.", sql_graph)
        self.assertNotIn("UPDATE gold.", sql_graph)
        self.assertNotIn("UPDATE serving.", sql_graph)
        self.assertIn("V9FrozenCohortSqlCandidateGraph.ReadAsync(", scorer)
        self.assertIn("SqlGraphEvidence = physical", scorer)
        self.assertIn('physical.Edges.Count == 0', scorer)
        self.assertIn('1 or 101 => "MARIA SILVA"', fixture)
        self.assertIn('2 => "SILVANA SILVA"', fixture)
        self.assertIn('102 => "SILVIA SILVA"', fixture)
        self.assertIn('(1, 101), (2, 102)', fixture)
        self.assertIn("AssertFrozenRunDecisionsAsync(connection, positive.RunId, reference)", fixture)
        self.assertIn("ReadFrozenTestCandidateEdgesForParityAsync(", fixture)
        self.assertIn("SeedDisposableDiagnosticIdentitiesAsync(connection)", fixture)
        self.assertIn("new V9PinnedSyntheticGraphCohortRunScorer(", fixture)
        self.assertIn('Is.EqualTo(positiveBefore)', fixture)
        caller = (REPO / ".github/workflows/v9-operational-model-sql-smoke.yml").read_text()
        self.assertIn("V9FrozenCohortSqlCandidateGraph.cs", caller)

    def test_scientific_test_stays_blocked(self):
        wf = TF4.read_text(encoding="utf-8")
        prep = wf.split("\n  prepare-test-100k:\n", 1)[1].split("\n  physical-100k:\n", 1)[0]
        train = wf.split("\n  physical-100k:\n", 1)[1].split("\n  audit-100k:\n", 1)[0]
        for stage in (prep, train):
            self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", stage)
        self.assertIn("test_required:", wf)
        self.assertIn("test-100k:", wf)


if __name__ == "__main__":
    unittest.main()
