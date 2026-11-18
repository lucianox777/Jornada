import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class OperationalCandidateReadTests(unittest.TestCase):
    def test_exact_v9_planner_and_full_gold_union(self):
        code = (ROOT / "V9OperationalCandidateRead.cs").read_text(encoding="utf-8")
        for required in (
            "IsolationLevel.Snapshot", "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "pinnedRuntime.BuildCandidateUuidQuery", "gold.pessoa",
            "estado_identidade=N'REFERENCIA'", "ORDER BY g.pessoa_uuid",
            "COUNT_BIG(*)", "count != ids.Count", "IncrementalHash",
            "SHA256", "snapshotTransaction",
        ):
            self.assertIn(required, code)
        for forbidden in ("NOLOCK", "TOP (", "OFFSET ", "JornadaHarness",
                          "INSERT INTO", "UPDATE ", "DELETE FROM", "IBGE"):
            self.assertNotIn(forbidden, code)

    def test_no_productive_activation(self):
        guard = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", guard)


if __name__ == "__main__":
    unittest.main()
