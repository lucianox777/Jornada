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
