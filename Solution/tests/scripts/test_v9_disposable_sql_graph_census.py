import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class V9DisposableSqlGraphCensusTests(unittest.TestCase):
    def test_sql_transport_is_diagnostic_and_exhaustive(self):
        source = (ROOT / "V9DisposableSqlGraphCensus.cs").read_text(encoding="utf-8")
        for required in (
            '"JornadaHarness"',
            "#V9CohortIds",
            "#V9GraphEdges",
            "COUNT_BIG(*)",
            "while (await reader.ReadAsync(ct))",
            "ids.Count != sqlIds",
            "edges.Count != sqlEdges",
            "RUNNER_V9_SQL_GRAPH_DIAGNOSTIC_ONLY",
        ):
            self.assertIn(required, source)
        for forbidden in ("TOP (", "OFFSET ", "FETCH NEXT", "Take(", "PublishAsync(",
                          "INSERT INTO", "UPDATE ", "DELETE FROM"):
            self.assertNotIn(forbidden, source)

    def test_bridge_rejects_frozen_cohort_and_independent_census_mismatch(self):
        source = (ROOT / "V9DisposableSqlGraphCensus.cs").read_text(encoding="utf-8")
        for required in (
            "ReadAndVerifyAsync(",
            "independentlyFrozenObservationIds.ToHashSet()",
            "expectedIds.Count != independentlyFrozenObservationIds.Count",
            "receipt.ObservationIds.Any(id => !expectedIds.Contains(id))",
            "receipt.ObservationIds.Distinct().Count()",
            "receipt.SqlObservationRows != independentSourceCensus.ObservationCount",
            "receipt.SqlEdgeRows != independentSourceCensus.EdgeCount",
            "V9OperationalCandidateGraphIntegrity.Verify(",
            "RUNNER_V9_SQL_GRAPH_SOURCE_CENSUS_MISMATCH",
        ):
            self.assertIn(required, source)

    def test_snapshot_transaction_is_mandatory_for_integrity_bridge(self):
        source = (ROOT / "V9DisposableSqlGraphCensus.cs").read_text(encoding="utf-8")
        for required in (
            "SqlTransaction snapshotTransaction",
            "ValidateSnapshot(connection, snapshotTransaction)",
            "transaction.IsolationLevel != IsolationLevel.Snapshot",
            "ReferenceEquals(transaction.Connection, connection)",
            "RUNNER_V9_SQL_GRAPH_SNAPSHOT_REQUIRED",
            "new SqlCommand(\"SELECT DB_NAME();\", connection, transaction)",
        ):
            self.assertIn(required, source)

    def test_every_sql_command_in_verified_read_uses_snapshot(self):
        import re

        source = (ROOT / "V9DisposableSqlGraphCensus.cs").read_text(encoding="utf-8")
        verified_read = source.split("private static async Task<Receipt> ReadAsync(", 1)[1]
        commands = re.findall(r"new SqlCommand\\s*\\((.*?)\\)\\s*(?:\\{|\\))",
                              verified_read, flags=re.DOTALL)
        self.assertEqual(len(commands), 4, "Every diagnostic SQL command must be audited")
        for command in commands:
            self.assertRegex(command, r",\\s*connection\\s*,\\s*transaction\\s*$",
                             "A SQL read escaped the required snapshot transaction")

    def test_production_gate_is_still_closed(self):
        source = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", source)


if __name__ == "__main__":
    unittest.main()
