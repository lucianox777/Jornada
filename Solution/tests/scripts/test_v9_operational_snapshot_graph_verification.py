import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class OperationalSnapshotGraphVerificationTests(unittest.TestCase):
    def test_one_snapshot_and_independent_receipt_required(self):
        code = (ROOT / "V9OperationalSnapshotGraphVerification.cs").read_text(encoding="utf-8")
        for required in (
            "IsolationLevel.Snapshot",
            "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "V9OperationalGoldCohortMembership.VerifyAsync(",
            "V9OperationalCohortGraphPreview.ReadAsync(",
            "V9OperationalGraphReceiptVerification.Verify(",
            "independentlyReadGraphCensus",
            "RUNNER_V9_SNAPSHOT_COHORT_GRAPH_COUNT_MISMATCH",
        ):
            self.assertIn(required, code)
        self.assertEqual(code.count("connection, snapshotTransaction, frozenCohort, ct"), 1)
        self.assertEqual(code.count("connection, snapshotTransaction, pinnedRuntime, frozenCohort, ct"), 1)
        for forbidden in ("new V9OperationalCandidateGraphIntegrity.Census(",
                          "INSERT INTO", "PublishAsync(", "NOLOCK", "IBGE"):
            self.assertNotIn(forbidden, code)


if __name__ == "__main__":
    unittest.main()
