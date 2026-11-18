import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class WiredOperationalSqlCensusTests(unittest.TestCase):
    def test_sql_census_is_wired_through_same_snapshot(self):
        code = (ROOT / "V9OperationalSnapshotGraphVerification.cs").read_text(encoding="utf-8")
        method = code.split("public static Task<Receipt> VerifyWithOperationalSqlCensusAsync(", 1)[1]
        self.assertIn("return VerifyWithSnapshotCensusAsync(", method)
        self.assertIn("V9OperationalSqlEdgeCensus.ReadAsync(", method)
        self.assertIn("sameConnection, sameTransaction, pinnedRuntime, frozenCohort, token", method)
        self.assertNotIn("Graph.Edges", method)
        self.assertNotIn("INSERT INTO", method)
        self.assertIn("IsolationLevel.Snapshot", code)
        self.assertLess(code.index("await readIndependentCensus("),
                        code.index("return await VerifyAsync("))


if __name__ == "__main__":
    unittest.main()
