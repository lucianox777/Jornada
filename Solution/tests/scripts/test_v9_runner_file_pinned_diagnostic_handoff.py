"""File-pinned V9 diagnostic handoff contract, without productive activation."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = (ROOT / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs").read_text()
AUDIT = (ROOT / "src/Jornada.Linkage.Runner/V9OperationalCandidateGraphParityAudit.cs").read_text()


class PinnedFileDiagnosticHandoff(unittest.TestCase):
    def test_boundary_used_without_sql_model_reload(self):
        method = RUNNER.split("RehearseV9PinnedFileNonPublishingAsync(", 1)[1].split(
            "RehearseV9NonPublishingSyntheticTestAsync(", 1)[0]
        self.assertIn("boundary.Runtime", method)
        self.assertIn("V9OperationalCandidateGraphParityAudit.CompareTestAsync(", method)
        self.assertIn("proof.ProductionBatchPipelineCertified", method)
        self.assertIn("CONCLUIDO_SEM_PUBLICACAO", method)
        self.assertNotIn("ReadPinnedAsync(", method)
        self.assertNotIn("INSERT INTO", method)
        self.assertNotIn("RunV9Async(", method)

    def test_sql_audit_remains_disposable_only(self):
        self.assertIn('"JornadaHarness"', AUDIT)
        self.assertIn("ReplayOperationalGraph(test,", AUDIT)


if __name__ == "__main__":
    unittest.main()
