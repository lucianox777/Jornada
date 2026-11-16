"""Structural regression for the V9 TEST-only double-page-boundary proof.

This is a source contract guard, not a physical SQL or production RunAsync proof.
"""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[2]
          / "src/Jornada.Linkage.Runner/V9ReadOnlyTestBatchRunner.cs")


class V9ReadOnlyBatchBoundaryInvarianceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_second_physical_query_uses_distinct_page_size(self):
        source = self.source
        self.assertIn("var alternateBatchSize = batchSize == 1 ? 2 : 1;", source)
        self.assertEqual(source.count("V9OperationalCandidateGraphParityAudit.CompareTestAsync("), 2)
        self.assertIn("alternateBatchSize, cancellationToken", source)

    def test_fail_closed_comparison_includes_graph_decisions_groups_and_counts(self):
        source = self.source
        for field in (
            "WinnerSha256", "ProjectionSchemaVersion",
            "ProjectionFingerprintSha256", "CanonicalEdgesSha256",
            "DecisionsSha256", "IdentityGroupsSha256", "Resolved",
            "Unresolved", "Conflicts", "NoCandidateInBlock",
            "ProductionBatchPipelineCertified",
        ):
            self.assertIn("alternate." + field + " != receipt." + field
                          if field != "ProductionBatchPipelineCertified"
                          else "alternate.ProductionBatchPipelineCertified", source)
        self.assertLess(source.index("if (alternate.WinnerSha256"),
                        source.index("return receipt;"))

    def test_disposable_database_guard_precedes_winner_load(self):
        source = self.source
        self.assertLess(source.index('guard.CommandText = "SELECT DB_NAME();"'),
                        source.index("V9OperationalPinnedCohortRuntime.LoadPinnedAsync("))
        self.assertIn("NOT ProbabilisticLinkageBatchRunner.RunAsync", source)


if __name__ == "__main__":
    unittest.main()
