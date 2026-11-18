import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class GraphReceiptBridgeTests(unittest.TestCase):
    def test_bridge_requires_separate_census_and_checks_preview_receipt(self):
        code = (ROOT / "V9OperationalGraphReceiptVerification.cs").read_text(encoding="utf-8")
        for required in (
            "V9OperationalCandidateGraphIntegrity.Census independentSourceCensus",
            "preview.ObservationCount != frozenCohort.Count",
            "preview.UndirectedEdgeCount != preview.Edges.Count",
            "preview.DirectedCandidateCount != checked(preview.UndirectedEdgeCount * 2)",
            "RUNNER_V9_GRAPH_PREVIEW_HASH_MISMATCH",
            "V9OperationalCandidateGraphIntegrity.Verify(",
            "ids, preview.Edges, independentSourceCensus",
        ):
            self.assertIn(required, code)
        for forbidden in ("new V9OperationalCandidateGraphIntegrity.Census(",
                          "INSERT INTO", "PublishAsync(", "NOLOCK", "IBGE"):
            self.assertNotIn(forbidden, code)


if __name__ == "__main__":
    unittest.main()
