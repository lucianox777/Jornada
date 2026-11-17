"""Fail-closed source guards for the physical SQL graph in real V9 RunAsync.

These guards are NOT a physical integration test or TEST admission evidence.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "src/Jornada.Linkage.Runner"
SCORER = ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs"
RUNNER = ROOT / "ProbabilisticLinkageBatchRunner.cs"


class V9PhysicalRunAsyncGraphGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scorer = SCORER.read_text(encoding="utf-8")
        cls.runner = RUNNER.read_text(encoding="utf-8")

    def test_sql_graph_mode_does_not_accept_supplied_edges(self):
        s = self.scorer
        self.assertIn("readPhysicalSqlGraph = true;", s)
        self.assertIn("Array.Empty<(long A, long B)>()", s)
        self.assertIn("V9FrozenCohortSqlCandidateGraph.ReadAsync(", s)
        self.assertIn("V9OperationalBlockingChaveCohortParity.VerifyAsync(", s)
        self.assertLess(s.index("V9FrozenCohortSqlCandidateGraph.ReadAsync("),
                        s.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("))
        self.assertLess(s.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("),
                        s.index("var global = await operationalLinkage.ResolvePinnedV9FrozenTestCohortAsync("))

    def test_exact_graph_parity_required_before_global_scoring(self):
        s = self.scorer
        self.assertIn("operational.UndirectedSqlEdges != physical.Edges.Count", s)
        self.assertIn("operational.CanonicalEdgesSha256", s)
        self.assertIn("physical.CanonicalEdgesSha256", s)
        self.assertIn("edges = physical.Edges.ToList();", s)
        self.assertLess(s.index("edges = physical.Edges.ToList();"),
                        s.index("var global = await operationalLinkage.ResolvePinnedV9FrozenTestCohortAsync("))
        self.assertIn("global.SnapshotSha256 != pinnedWinnerSha", s)
        self.assertIn("global.Decisions.Count != cohort.Length", s)

    def test_runasync_requires_full_frozen_universe_before_persistence(self):
        s = self.runner
        self.assertIn("v9Grouped.PrepareAsync(runId, model, frozen, workCt)", s)
        self.assertIn("await PersistBatchAsync(", s)
        self.assertLess(s.index("v9Grouped.PrepareAsync(runId, model, frozen, workCt)"),
                        s.index("await PersistBatchAsync("))
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", s)


    def test_physical_sql_graph_rejects_capacity_excess_and_invalid_edges(self):
        graph = (ROOT / "V9FrozenCohortSqlCandidateGraph.cs").read_text(encoding="utf-8")
        for guard in ("if (signatures.Count >= MaximumSignatures)",
                      "if (++incidences > MaximumPairIncidences)",
                      "if (edges.Count >= MaximumPairIncidences)",
                      "if (a < 1 || a >= b || b > frozenRows.Count)",
                      "SELECT COUNT_BIG(*) FROM (",
                      "if (edges.Count != expectedEdges)",
                      "edges[i - 1].B >= edges[i].B"):
            self.assertIn(guard, graph)

    def test_operational_graph_checks_full_manifest_and_bidirectional_edges(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text(encoding="utf-8")
        for guard in ("expectedManifest.SetEquals(seen)",
                      "goldCount != frozenRows.Count",
                      "directions.Values.Any(value => value != 3)",
                      "expected.SetEquals(directions.Keys)",
                      "BuildCandidateUuidQuery(cmd,"):
            self.assertIn(guard, parity)


if __name__ == "__main__":
    unittest.main()
