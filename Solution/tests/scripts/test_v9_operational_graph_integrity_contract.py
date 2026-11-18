import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class OperationalGraphIntegrityContractTests(unittest.TestCase):
    def test_fail_closed_complete_cohort_graph_contract(self):
        source = (ROOT / "V9OperationalCandidateGraphIntegrity.cs").read_text(encoding="utf-8")
        for required in (
            "frozenObservationIds.ToHashSet()",
            "ids.Count != frozenObservationIds.Count",
            "ids.Count != independentSourceCensus.ObservationCount",
            "!ids.Contains(a) || !ids.Contains(b)",
            "!edges.Add((a, b))",
            "edges.Count > independentSourceCensus.EdgeCount",
            "edges.Count != independentSourceCensus.EdgeCount",
            "OrderBy(edge => edge.A).ThenBy(edge => edge.B)",
            "IncrementalHash.CreateHash(HashAlgorithmName.SHA256)",
            "RUNNER_V9_GRAPH_HASH_MISMATCH",
            "RUNNER_V9_GRAPH_DUPLICATE_EDGE",
            "RUNNER_V9_GRAPH_ORPHAN_OR_INVALID_EDGE",
        ):
            self.assertIn(required, source)
        self.assertNotIn("Take(", source)
        self.assertNotIn("MaximumObservations", source)
        self.assertNotIn("PublishAsync(", source)

    def test_productive_certification_remains_closed(self):
        source = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", source)
        self.assertIn("throw new InvalidOperationException(", source)


if __name__ == "__main__":
    unittest.main()
