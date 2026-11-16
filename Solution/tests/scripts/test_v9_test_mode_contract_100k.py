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

    def test_train_audit_validation_are_one_mandatory_campaign(self):
        """PO contract: TEST optional; TRAIN/AUDIT/VALIDATION never independent."""
        wf = self.workflow
        train = wf.split("\n  physical-100k:\n", 1)[1].split("\n  audit-100k:\n", 1)[0]
        audit = wf.split("\n  audit-100k:\n", 1)[1].split("\n  validation-100k:\n", 1)[0]
        validation = wf.split("\n  validation-100k:\n", 1)[1].split("\n  test-100k:\n", 1)[0]
        closeout = wf.split("\n  e2e-closeout:\n", 1)[1]
        self.assertIn("needs: [prepare-100k, prepare-test-100k, test-database-preflight]", train)
        self.assertIn("needs.prepare-100k.result == 'success'", train)
        prepare = wf.split("\n  prepare-100k:\n", 1)[1].split("\n  test-database-preflight:\n", 1)[0]
        self.assertIn("V9_PREPARE_ADMISSION=SUCCESS", prepare)
        self.assertIn("Upload immutable PREPARE admission receipt", prepare)
        self.assertIn("v9-prepare-admission-", prepare)
        self.assertIn("Download PREPARE admission receipt from same campaign", train)
        self.assertIn("V9_PREPARE_RECEIPT_VERIFIED", train)
        self.assertIn('receipt["runId"] == os.environ["GITHUB_RUN_ID"]', train)
        self.assertIn('receipt["commitSha"] == os.environ["EXACT_SHA"]', train)
        self.assertIn("PREPARE mandatory campaign: generate frozen TRAIN corpus", train)
        self.assertIn("PREPARE mandatory campaign: start masked disposable SQL Server", train)
        self.assertIn("PREPARE mandatory campaign: rehydrate frozen IBGE", train)
        self.assertLess(train.index("PREPARE mandatory campaign: generate frozen TRAIN corpus"),
                        train.index("Run physical V9 CSharp on 324"))
        self.assertNotIn("needs.prepare-test-100k.result == 'success' && inputs.test_required != true", train)
        self.assertIn("inputs.test_required != true", train)
        self.assertIn("needs: physical-100k", audit)
        self.assertIn("needs.physical-100k.result == 'success'", audit)
        self.assertIn("needs: audit-100k", validation)
        self.assertIn("needs.audit-100k.result == 'success'", validation)
        self.assertIn("needs.audit-100k.outputs.has_finalists == 'true'", validation)
        self.assertIn("needs: [prepare-test-100k, test-database-preflight, physical-100k, audit-100k, validation-100k, test-100k, test-database-readonly]", closeout)
        self.assertIn("always()", closeout)
        # No standalone validation dispatch or independent optional phase flag.
        self.assertNotIn("validation_required:", wf)
        self.assertNotIn("train_only:", wf)

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
        self.assertIn("candidateExecutionPlanSchemaVersion = winner.Payload.ProjectionSchemaVersion", cli)
        self.assertIn("candidateExecutionPlanFingerprintSha256 = winner.Payload.ProjectionFingerprintSha256", cli)
        self.assertIn("candidateGroupingAlgorithm = winner.Payload.CandidateGrouping?.CandidateGrouping", cli)
        self.assertIn("activated = false", cli)
        self.assertIn("GetActiveModelAsync(CancellationToken ct) =>", scorer)
        self.assertIn("Modelo ativo é proibido no V9 agrupado TEST_ONLY.", scorer)
        self.assertIn("model.BlockingContract?.ProjectionSchemaVersion != winner.Payload.ProjectionSchemaVersion", scorer)
        self.assertIn("model.BlockingContract?.ProjectionFingerprintSha256 != winner.Payload.ProjectionFingerprintSha256", scorer)
        runtime = (ROOT / "src/Jornada.Linkage.Core/V9OperationalPinnedCohortRuntime.cs").read_text(encoding="utf-8")
        self.assertIn("runtime.RequireSupportedFrozenExecutionPlan();", cli)
        self.assertIn("pinned.RequireSupportedFrozenExecutionPlan();", scorer)
        self.assertIn("V9_TEST_EXECUTION_PLAN_UNSUPPORTED", runtime)
        self.assertIn("plan.MaximumObservationsPerIdentity <= 0", runtime)
        self.assertIn("plan.MinimumPairPosterior is < 0m or > 1m", runtime)
        self.assertIn("winner.Payload.ProjectionFingerprintSha256 is not { Length: 64 }", runtime)

    def test_sql_candidate_preflight_counts_all_cross_manager_incidences(self):
        graph = (ROOT / "src/Jornada.Linkage.Runner/V9FrozenCohortSqlCandidateGraph.cs").read_text(encoding="utf-8")
        self.assertIn("n * (n - 1) / 2 - sameManager", graph)
        self.assertIn("GroupBy(row => row.Manager, StringComparer.Ordinal)", graph)
        self.assertIn("incidences = checked(incidences + crossManager)", graph)
        self.assertIn("if (incidences > MaximumPairIncidences)", graph)
        self.assertIn("SELECT DISTINCT a.obs_id,b.obs_id", graph)
        self.assertIn("SELECT COUNT_BIG(*)", graph)
        self.assertIn("INTO #v9_run_edges", graph)
        self.assertIn("CREATE UNIQUE CLUSTERED INDEX IX_v9_run_edges", graph)
        self.assertIn("SELECT TOP (@page_size) a,b", graph)\n        self.assertIn("const int EdgePageSize = 2048;", graph)\n        self.assertIn("while (edges.Count < distinctEdgeCount)", graph)\n        self.assertIn("WHERE a>@last_a OR (a=@last_a AND b>@last_b)", graph)\n        self.assertIn("if (pageCount == 0)", graph)\n        self.assertIn("Paginação SQL V9 encontrou arestas além do censo.", graph)
        self.assertIn("SELECT COUNT_BIG(*) FROM #v9_run_edges", graph)
        self.assertIn("distinctEdgeCount > MaximumPairIncidences", graph)
        self.assertIn("if (edges.Count != distinctEdgeCount)", graph)
        self.assertIn("IncrementalHash.CreateHash(HashAlgorithmName.SHA256)", graph)
        self.assertIn("edgeDigest.AppendData(Encoding.UTF8.GetBytes(line))", graph)
        self.assertIn("edgeDigest.GetHashAndReset()", graph)
        self.assertNotIn("string.Concat(edges.Select(", graph)
        self.assertIn("if (a < previousA || (a == previousA && b <= previousB))", graph)
        self.assertIn("edgeDigest.AppendData(Encoding.UTF8.GetBytes(line));\n                edges.Add((a, b));", graph)
        self.assertNotIn("foreach (var edge in edges)", graph)
        self.assertIn("const int SignatureTransferBatchSize = 2048;", graph)
        self.assertIn("if (table.Rows.Count < SignatureTransferBatchSize)", graph)
        self.assertIn("table.Clear();", graph)
        self.assertIn("if (table.Rows.Count > 0)", graph)
        self.assertIn("public const int MaximumObservations = 512;", graph)

    def test_operational_candidate_audit_rejects_unexpected_sql_edges_early(self):
        audit = (ROOT / "src/Jornada.Linkage.Runner/V9OperationalCandidateGraphParityAudit.cs").read_text(encoding="utf-8")
        self.assertIn("operationalRuntime.RequireSupportedFrozenExecutionPlan();", audit)
        self.assertIn("if (!expected.Contains(key))", audit)
        self.assertIn("SQL operacional retornou aresta ausente do grafo físico TEST.", audit)
        self.assertIn("actualDirections.Values.Any(v => v != 3)", audit)
        self.assertIn("expected.SetEquals(actual)", audit)
        self.assertIn("ProductionBatchPipelineCertified: false", audit)
        self.assertIn("IncrementalHash.CreateHash(HashAlgorithmName.SHA256)", audit)
        self.assertIn("canonicalHash.AppendData(Encoding.UTF8.GetBytes(", audit)
        self.assertIn("referenceHash.AppendData(Encoding.UTF8.GetBytes(", audit)
        self.assertIn("if (!string.Equals(digest, referenceDigest, StringComparison.Ordinal))", audit)

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
