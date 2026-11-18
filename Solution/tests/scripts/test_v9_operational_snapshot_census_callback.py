import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class SnapshotCensusCallbackTests(unittest.TestCase):
    def test_census_read_before_graph_on_same_snapshot(self):
        code = (ROOT / "V9OperationalSnapshotGraphVerification.cs").read_text(encoding="utf-8")
        method = code.split("public static async Task<Receipt> VerifyWithSnapshotCensusAsync(", 1)[1]
        for expected in (
            "Func<SqlConnection, SqlTransaction, CancellationToken,",
            "IsolationLevel.Snapshot",
            "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "await readIndependentCensus(connection, snapshotTransaction, ct)",
            "RUNNER_V9_GRAPH_CENSUS_MISSING",
            "return await VerifyAsync(connection, snapshotTransaction, pinnedRuntime,",
        ):
            self.assertIn(expected, method)
        self.assertLess(method.index("await readIndependentCensus("),
                        method.index("return await VerifyAsync("))
        self.assertNotIn("new V9OperationalCandidateGraphIntegrity.Census(", method)
        self.assertNotIn("INSERT INTO", method)


if __name__ == "__main__":
    unittest.main()
