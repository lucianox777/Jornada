import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class OperationalCohortGraphPreviewTests(unittest.TestCase):
    def test_full_cohort_read_and_closed_integrity(self):
        code = (ROOT / "V9OperationalCohortGraphPreview.cs").read_text(encoding="utf-8")
        for required in (
            "IsolationLevel.Snapshot", "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "frozenCohort.OrderBy", "V9OperationalCandidateRead.ReadAsync",
            "RUNNER_V9_CANDIDATE_OUTSIDE_FROZEN_COHORT",
            "RUNNER_V9_GRAPH_ASYMMETRIC", "RUNNER_V9_GRAPH_DUPLICATE_DIRECTION",
            "RUNNER_V9_GRAPH_DIRECTION_COUNT_MISMATCH", "IncrementalHash",
            "SHA256", "checked(directed + 1)",
        ):
            self.assertIn(required, code)
        for forbidden in ("TOP (", "NOLOCK", "OFFSET ", "MaximumObservations",
                          "JornadaHarness", "PublishAsync(", "INSERT INTO", "IBGE"):
            self.assertNotIn(forbidden, code)

    def test_productive_gate_preserved(self):
        code = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", code)


if __name__ == "__main__":
    unittest.main()
