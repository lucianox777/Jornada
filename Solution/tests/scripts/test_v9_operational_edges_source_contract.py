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
            "OperationalEdges = directions.Keys.OrderBy(x => x.A).ThenBy(x => x.B).ToArray()",
            parity,
        )
        self.assertIn("ORDER BY a.obs_id,b.obs_id;", physical)
        self.assertIn("edges[i - 1].B >= edges[i].B", physical)
        self.assertIn("physical.Edges.SequenceEqual(operational.OperationalEdges)", scorer)
        self.assertLess(
            scorer.index("physical.Edges.SequenceEqual(operational.OperationalEdges)"),
            scorer.index("edges = operational.OperationalEdges.ToList();"),
        )

    def test_operational_query_edges_are_scoring_input(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text()
        scorer = (ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text()
        self.assertIn("OperationalEdges = directions.Keys.OrderBy", parity)
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
