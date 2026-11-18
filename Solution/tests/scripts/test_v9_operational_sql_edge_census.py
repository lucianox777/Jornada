import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class OperationalSqlEdgeCensusTests(unittest.TestCase):
    def test_separate_sql_reader_and_snapshot(self):
        code = (ROOT / "V9OperationalSqlEdgeCensus.cs").read_text(encoding="utf-8")
        for required in (
            "IsolationLevel.Snapshot",
            "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "pinnedRuntime.BuildCandidateUuidQuery(command,",
            "INNER JOIN gold.pessoa g",
            "estado_identidade=N'REFERENCIA'",
            "uniqueCandidates.Add(candidate)",
            "RUNNER_V9_CENSUS_OUTSIDE_COHORT",
            "RUNNER_V9_CENSUS_ASYMMETRIC",
            "IncrementalHash",
            "new V9OperationalCandidateGraphIntegrity.Census(",
        ):
            self.assertIn(required, code)
        for forbidden in (
            "V9OperationalCandidateRead.ReadAsync",
            "V9OperationalCohortGraphPreview.ReadAsync",
            "TOP (", "NOLOCK", "OFFSET ", "INSERT INTO", "IBGE",
        ):
            self.assertNotIn(forbidden, code)


if __name__ == "__main__":
    unittest.main()
