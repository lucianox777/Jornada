"""Static regression: V9 harness must score the verified operational edges.

This is NOT a physical SQL test, scale proof, or scientific certification.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "src/Jornada.Linkage.Runner"


class OperationalEdgesSourceContract(unittest.TestCase):
    def test_both_graphs_are_canonical_and_physical_oracle_is_ordered(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        physical = (ROOT / "V9FrozenCohortSqlCandidateGraph.cs").read_text()
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        self.assertIn(
            "OperationalEdges = Array.AsReadOnly(directions.Keys.OrderBy(x => x.A).ThenBy(x => x.B).ToArray())",
            parity,
        )
        self.assertIn("ORDER BY a.obs_id,b.obs_id;", physical)
        self.assertIn("edges[i - 1].B >= edges[i].B", physical)
        self.assertIn("physical.Edges.SequenceEqual(operational.OperationalEdges)", scorer)
        self.assertLess(
            scorer.index("physical.Edges.SequenceEqual(operational.OperationalEdges)"),
            scorer.index("edges = operational.OperationalEdges.ToList();"),
        )

    def test_isolation_gap_remains_explicit_in_both_graph_readers(self):
        physical = (ROOT / "V9FrozenCohortSqlCandidateGraph.cs").read_text()
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        # Absence of a transaction is a current limitation, not a contract:
        # introducing a correct consistent snapshot must not fail this test.
        has_explicit_transaction = any(
            "BeginTransactionAsync(" in source
            for source in (physical, parity, scorer)
        )
        if not has_explicit_transaction:
            self.assertIn("not an atomic Silver/Gold snapshot", parity)
            self.assertIn("ProductionGoldCertified: false", parity)

    def test_diagnostic_gold_proof_does_not_claim_atomic_snapshot(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        self.assertIn("not an atomic Silver/Gold snapshot", parity)
        self.assertIn("ProductionGoldCertified: false", parity)
        self.assertNotIn("ProductionGoldCertified: true", parity)

    def test_gold_manifest_is_verified_before_operational_candidates(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        gold = parity.index("SELECT pessoa_uuid,cpf,nome_completo,data_nascimento,nome_mae,")
        gold_count = parity.index("if (goldCount != frozenRows.Count)")
        query = parity.index("var candidateQuery = planner.BuildCandidateUuidQuery(")
        score_input = parity.index("OperationalEdges = Array.AsReadOnly(")
        self.assertLess(gold, gold_count)
        self.assertLess(gold_count, query)
        self.assertLess(query, score_input)
        self.assertIn("!found.Add(uuid) || !byUuid.TryGetValue(uuid, out var candidate)", parity)
        self.assertIn("reader.IsDBNull(5)", parity)
        self.assertIn("reader.GetString(5) != \"REFERENCIA\"", parity)
        self.assertIn("Manifesto Gold V9 incompleto para coorte congelada.", parity)
        self.assertIn("Consulta candidata SQL retornou conteúdo Gold adulterado.", parity)

    def test_operational_parity_rejects_invalid_frozen_universe(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        start = parity.index("if (frozenRows.Count is < 2")
        end = parity.index("var expected = new HashSet", start)
        guard = parity[start:end]
        self.assertIn("row is null || row.PessoaObservacaoId <= 0", guard)
        self.assertIn("row.Observation is null || row.Observation.Cpf is not null", guard)
        self.assertIn(
            "frozenRows.Select(row => row.PessoaObservacaoId).Distinct().Count() != frozenRows.Count",
            guard,
        )
        self.assertIn("string.IsNullOrWhiteSpace(manager)", guard)

    def test_frozen_silver_manager_ids_fail_closed(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        start = scorer.index("var managers = new Dictionary<long, string>();")
        end = scorer.index("var idToIndex =", start)
        block = scorer[start:end]
        self.assertIn("reader.IsDBNull(0) || reader.IsDBNull(1)", block)
        self.assertIn("observationId <= 0 || string.IsNullOrWhiteSpace(manager)", block)
        self.assertIn("!managers.TryAdd(observationId, manager)", block)
        self.assertIn("managers.Count != frozenRows.Count", block)
        self.assertIn("frozenRows.Any(row => !managers.ContainsKey", block)
        self.assertLess(end, scorer.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("))

    def test_both_edge_receipts_reject_mutable_array_exposure(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        physical = (ROOT / "V9FrozenCohortSqlCandidateGraph.cs").read_text()
        self.assertIn(
            "OperationalEdges = Array.AsReadOnly(directions.Keys.OrderBy",
            parity,
        )
        self.assertIn("return new(edges.AsReadOnly(), signatures.Count, digest,", physical)
        self.assertNotIn("OperationalEdges = directions.Keys.ToArray()", parity)
        self.assertNotIn("return new(edges.ToArray(), signatures.Count, digest,", physical)

    def test_physical_smoke_never_falls_back_to_fixture_edges(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        start = scorer.index("if (readPhysicalSqlGraph)")
        end = scorer.index("// EXACTLY one global grouping", start)
        physical_branch = scorer[start:end]
        self.assertIn("V9FrozenCohortSqlCandidateGraph.ReadAsync(", physical_branch)
        self.assertIn("V9OperationalBlockingChaveCohortParity.VerifyAsync(", physical_branch)
        self.assertIn("edges = operational.OperationalEdges.ToList();", physical_branch)
        self.assertNotIn("edges = physical.Edges.ToList();", physical_branch)
        self.assertNotIn("edges = syntheticEdges", physical_branch)
        self.assertIn("ResolvePinnedV9FrozenTestCohortAsync(", scorer[end:])
        self.assertLess(
            scorer.index("edges = operational.OperationalEdges.ToList();"),
            scorer.index("ResolvePinnedV9FrozenTestCohortAsync("),
        )

    def test_operational_candidate_read_is_bounded_and_fail_closed(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        start = parity.index("var directions = new Dictionary")
        end = parity.index("if (directions.Values.Any(value => value != 3))", start)
        query = parity[start:end]
        self.assertIn("planner.BuildCandidateUuidQuery(cmd,", query)
        self.assertIn("JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid", query)
        self.assertIn("WHERE g.estado_identidade=N'REFERENCIA'", query)
        self.assertIn("if (!fromSource.Add(candidateUuid))", query)
        self.assertIn("if (!byUuid.TryGetValue(candidateUuid, out var candidate))", query)
        self.assertIn("Consulta candidata SQL retornou conteúdo Gold adulterado.", query)
        self.assertIn("string.Equals(candidate.Manager,", query)
        self.assertIn("directions.Count > V9FrozenCohortSqlCandidateGraph.MaximumPairIncidences", query)
        self.assertIn("ct.ThrowIfCancellationRequested();", query)

    def test_nonpublishing_group_diagnostic_has_sql_terminal_guard(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        tests = (Path(__file__).resolve().parents[2]
                 / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs").read_text()
        for required in ("global.IdentityByObservation.TryGetValue(",
                         "new ReadOnlyDictionary<long, Guid>(groups)",
                         "lr.status,lr.modelo_id,lr.modelo_versao",
                         '"CONCLUIDO_SEM_PUBLICACAO"',
                         "reader.GetInt64(7) != groups.Count"):
            self.assertIn(required, scorer)
        self.assertIn("ReadCompletedGroupingDiagnosticAsync(", tests)
        self.assertIn("failedScorer.ReadCompletedGroupingDiagnosticAsync(", tests)
        self.assertIn('"FALHOU", 100', tests)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED",
                      (ROOT / "V9OperationalRunBoundary.cs").read_text())

    def test_nonpublishing_sql_source_to_group_map_is_fail_closed(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        tests = (Path(__file__).resolve().parents[2]
                 / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs").read_text()
        self.assertIn("ReadCompletedSourceGroupingDiagnosticAsync(", scorer)
        self.assertIn("SELECT ri.pessoa_observacao_id,po.codigo_pessoa_origem,po.cpf", scorer)
        self.assertIn("JOIN silver.pessoa_observacao po", scorer)
        self.assertIn('code.StartsWith("SYNTH-", StringComparison.Ordinal)', scorer)
        self.assertIn('code.StartsWith("V9TEST_", StringComparison.Ordinal)', scorer)
        self.assertIn("!reader.IsDBNull(2)", scorer)
        self.assertIn("sources.Count != groups.Count", scorer)
        self.assertIn("new ReadOnlyDictionary<string, Guid>(sources)", scorer)
        self.assertIn("ReadCompletedSourceGroupingDiagnosticAsync(", tests)
        self.assertIn("failedScorer.ReadCompletedSourceGroupingDiagnosticAsync(", tests)

    def test_completed_sql_run_result_exact_membership_and_status_guards(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        fixture = (Path(__file__).resolve().parents[2]
                   / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs").read_text()
        start = scorer.index("public async Task<IReadOnlyDictionary<long, Guid>> ReadCompletedGroupingDiagnosticAsync(")
        end = scorer.index("public async Task<IReadOnlyDictionary<string, Guid>> ReadCompletedSourceGroupingDiagnosticAsync(", start)
        guard = scorer[start:end]
        for required in (
            "SELECT COUNT_BIG(*) FROM identidade.linkage_run_item i",
            "NOT EXISTS (",
            "SELECT 1 FROM identidade.linkage_resultado r",
            "SELECT COUNT_BIG(*) FROM identidade.linkage_resultado r",
            "SELECT 1 FROM identidade.linkage_run_item i",
            "r.pessoa_observacao_id=i.pessoa_observacao_id",
            "i.pessoa_observacao_id=r.pessoa_observacao_id",
            "r.modelo_id<>lr.modelo_id",
            "r.modelo_versao<>lr.modelo_versao",
            "lr.resolvidos,lr.nao_resolvidos,lr.conflitos",
            "r.status=N'RESOLVIDO'",
            "r.status=N'NAO_RESOLVIDO'",
            "r.status=N'CONFLITO'",
            "reader.GetInt64(11) != 0 || reader.GetInt64(12) != 0",
            "reader.GetInt64(13) != 0",
            "reader.GetInt64(14) != reader.GetInt64(8)",
        ):
            self.assertIn(required, guard)
        start = fixture.index("private static async Task ProveCompletedRunResultMembershipGuardsAsync(")
        end = fixture.index("private static async Task InsertSyntheticUniverseAsync(", start)
        adversarial = fixture[start:end]
        for required in (
            "IF DB_NAME()<>N'JornadaHarness'",
            "SET modelo_versao=2",
            "SET status=N'CONFLITO'",
            "OUTPUT INSERTED.pessoa_observacao_id",
            "SET pessoa_observacao_id=@shadow",
            "INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)",
            "AssertRawMutationBlockedAsync(",
            "Assert.ThrowsAsync<SqlException>",
            "Assert.ThrowsAsync<InvalidOperationException>",
            "51822, 51823",
            "DELETE FROM silver.pessoa_observacao",
            "finally",
            "restored.Keys",
        ):
            self.assertIn(required, adversarial)
        self.assertIn("await ProveCompletedRunResultMembershipGuardsAsync(", fixture)

    def test_run_scoped_sql_delivery_ledger_requires_frozen_membership(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        tests = (Path(__file__).resolve().parents[2]
                 / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs").read_text()
        self.assertIn("BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(", scorer)
        self.assertIn("await ReadCompletedSourceGroupingDiagnosticAsync(runId, ct)", scorer)
        self.assertIn("FROM identidade.linkage_run_item ri", scorer)
        self.assertIn("JOIN silver.pessoa_observacao po", scorer)
        self.assertIn("JOIN ingestao.lote l ON l.lote_id=po.lote_id", scorer)
        self.assertIn("JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id", scorer)
        self.assertIn("LEFT JOIN bronze.entrega_arquivo b", scorer)
        self.assertIn("e.gestor_id=po.gestor_id", scorer)
        self.assertIn("sourceDeliveryIdMatches = entry.Value.All", scorer)
        self.assertIn("sqlPayloadMetadataVerified = entry.Value.All", scorer)
        self.assertIn("httpDeliveryProven = false", scorer)
        self.assertIn("E2E_NUCLEO_PASS = false", scorer)
        self.assertIn("BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(", tests)
        self.assertIn("Synthetic fixture has no HTTP or Bronze data.", tests)

    def test_physical_disposable_delivery_metadata_tampering_is_detected(self):
        tests = (Path(__file__).resolve().parents[2]
                 / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs").read_text()
        start = tests.index("private static async Task ProveDisposableRunDeliverySqlMetadataGuardsAsync(")
        end = tests.index("private static async Task InsertSyntheticUniverseAsync(", start)
        block = tests[start:end]
        for required in (
            "BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(",
            'DB_NAME()<>N\'JornadaHarness\'',
            "INSERT bronze.entrega_arquivo(",
            "payload_sha256=REPLICATE('c',64)",
            "id_pessoa_entrega=N'ALTERADA'",
            "status=N'PENDENTE'",
            "bytes_recebidos=513",
            "Assert.That(changedBronze[\"V9TEST\"].Metadata, Is.False)",
            "Assert.That(changedSource[\"V9TEST2\"].Source, Is.False)",
            "Assert.That(changedLote[\"V9TEST2\"].Metadata, Is.False)",
            "Assert.That(changedBytes[\"V9TEST2\"].Metadata, Is.False)",
            "Assert.That(document.GetProperty(\"httpDeliveryProven\").GetBoolean(), Is.False",
            "DELETE b FROM bronze.entrega_arquivo b",
            "UPDATE silver.pessoa_observacao SET id_pessoa_entrega=NULL",
        ):
            self.assertIn(required, block)
        self.assertIn("await ProveDisposableRunDeliverySqlMetadataGuardsAsync(", tests)
        self.assertIn("finally", block)
        self.assertIn("restored.Values.All(p => !p.Metadata && !p.Source)", block)

    def test_sql_staging_never_invents_gold_state(self):
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        tests = (Path(__file__).resolve().parents[2]
                 / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs").read_text()
        self.assertIn("BuildCompletedSourceGroupingStagingJsonAsync(", scorer)
        self.assertIn('"E2E_V9_GROUPING_LINKAGE_ONLY_DEV_V2"', scorer)
        self.assertIn('sourceSetSha256Utf16le = sourceSetDigest', scorer)
        self.assertIn('Encoding.Unicode.GetBytes(sourceSetCanonical)', scorer)
        self.assertIn('identityStatesAvailable = false', scorer)
        self.assertIn('certified = false', scorer)
        self.assertIn('E2E_NUCLEO_PASS = false', scorer)
        self.assertIn('await ReadCompletedSourceGroupingDiagnosticAsync(runId, ct)', scorer)
        self.assertIn('BuildCompletedSourceGroupingStagingJsonAsync(', tests)
        self.assertIn('Is.EqualTo("STAGING_NOT_CERTIFIED")', tests)

    def test_operational_query_edges_are_scoring_input(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        self.assertIn("OperationalEdges = Array.AsReadOnly(directions.Keys.OrderBy", parity)
        self.assertIn("if (!expected.SetEquals(directions.Keys))", parity)
        self.assertIn("directions.Values.Any(value => value != 3)", parity)
        self.assertIn("edges = operational.OperationalEdges.ToList();", scorer)
        self.assertNotIn("edges = physical.Edges.ToList();", scorer)
        self.assertLess(scorer.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("),
                        scorer.index("edges = operational.OperationalEdges.ToList();"))
        self.assertLess(scorer.index("edges = operational.OperationalEdges.ToList();"),
                        scorer.index("ResolvePinnedV9FrozenTestCohortAsync("))
        self.assertIn("physical.Edges.SequenceEqual(operational.OperationalEdges)", scorer)
        self.assertIn("EnsureHarnessAndAnchorAsync(connection, ct)", scorer)


if __name__ == "__main__":
    unittest.main()
