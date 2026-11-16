"""Contract guards for V9 production-batch parity, without false certification.

A rehearsal on synthetic SQL must never be confused with the real publishing
RunAsync path. These tests intentionally remain valid while scientific TEST
admission is blocked.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "src/Jornada.Linkage.Runner"


class V9ProductionBatchParityBoundaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.production = (RUNNER / "ProbabilisticLinkageBatchRunner.cs").read_text(encoding="utf-8")
        cls.rehearsal = (RUNNER / "V9ReadOnlyTestBatchRunner.cs").read_text(encoding="utf-8")

    def test_rehearsal_explicitly_rejects_production_certification(self):
        self.assertIn("proof.ProductionBatchPipelineCertified", self.production)
        self.assertIn("proof.ProductionBatchPipelineCertified", self.rehearsal)
        self.assertIn("NOT ProbabilisticLinkageBatchRunner.RunAsync", self.rehearsal)

    def test_rehearsal_requires_disposable_sql_before_loading_winner(self):
        guard = self.rehearsal.index('guard.CommandText = "SELECT DB_NAME();"')
        restriction = self.rehearsal.index('"JornadaHarness"')
        load = self.rehearsal.index("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(")
        self.assertLess(guard, restriction)
        self.assertLess(restriction, load)

    def test_production_rehearsal_does_not_claim_real_run_transition(self):
        self.assertIn("RehearseV9NonPublishingSyntheticTestAsync(", self.production)
        self.assertIn("PersistV9SyntheticRehearsalLedgerAsync(", self.production)
        self.assertIn("never", self.production.lower())
        self.assertIn("LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO", self.production)


if __name__ == "__main__":
    unittest.main()
