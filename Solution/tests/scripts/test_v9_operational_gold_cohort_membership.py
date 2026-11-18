import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class GoldCohortMembershipTests(unittest.TestCase):
    def test_full_snapshot_membership_not_only_count(self):
        code = (ROOT / "V9OperationalGoldCohortMembership.cs").read_text(encoding="utf-8")
        for required in (
            "IsolationLevel.Snapshot", "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "SELECT COUNT_BIG(*) FROM gold.pessoa",
            "SELECT pessoa_uuid FROM gold.pessoa",
            "ORDER BY pessoa_uuid", "expected.Contains(id)",
            "seen.Count != count", "RUNNER_V9_GOLD_COHORT_MEMBERSHIP_MISMATCH",
            "RUNNER_V9_GOLD_COHORT_DUPLICATE_OR_INVALID", "IncrementalHash",
        ):
            self.assertIn(required, code)
        for forbidden in ("TOP (", "OFFSET ", "NOLOCK", "IBGE", "INSERT INTO", "PublishAsync("):
            self.assertNotIn(forbidden, code)

    def test_productive_gate_still_closed(self):
        code = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", code)


if __name__ == "__main__":
    unittest.main()
