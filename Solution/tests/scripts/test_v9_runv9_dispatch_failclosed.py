"""Offline regression: typed V9 dispatch remains closed until certified.

This is static source verification, not SQL parity or scientific TEST evidence.
"""
from pathlib import Path
import unittest

RUNNER = (Path(__file__).resolve().parents[2] / "src/Jornada.Linkage.Runner"
          / "ProbabilisticLinkageBatchRunner.cs")


class V9TypedDispatchFailClosedTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        source = RUNNER.read_text(encoding="utf-8")
        start = source.index("public Task<ProbabilisticLinkageRunSummary> RunV9Async(")
        end = source.index("public async Task<ProbabilisticLinkageRunSummary> RunAsync(", start)
        cls.typed = source[start:end]
        cls.general = source[end:]

    def test_pinned_rehearsal_remains_noncertifying_and_reconciled(self):
        source = RUNNER.read_text(encoding="utf-8")
        start = source.index(
            "public async Task<V9NonPublishingBatchExecutionReceipt> "
            "RehearseV9PinnedFileNonPublishingAsync(")
        end = source.index(
            "public async Task<V9NonPublishingBatchExecutionReceipt> "
            "RehearseV9NonPublishingSyntheticTestAsync(", start)
        rehearsal = source[start:end]
        sql_open = rehearsal.index("operationalSql.OpenAsync(")
        for guard in (
            "ArgumentNullException.ThrowIfNull(boundary);",
            "ArgumentNullException.ThrowIfNull(syntheticTest);",
            "ArgumentNullException.ThrowIfNull(physicalSqlEdges);",
            "cancellationToken.ThrowIfCancellationRequested();",
            "ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);",
            "ArgumentOutOfRangeException.ThrowIfGreaterThan(",
            "syntheticTest.Count is < 2 or >",
        ):
            self.assertIn(guard, rehearsal[:sql_open])
        self.assertIn("V9OperationalCandidateGraphParityAudit.CompareTestAsync(", rehearsal)
        self.assertIn("proof.ProductionBatchPipelineCertified", rehearsal)
        self.assertIn("proof.WinnerSha256 != boundary.Runtime.WinnerSha256", rehearsal)
        self.assertIn("proof.DecisionsSha256 is not { Length: 64 }", rehearsal)
        self.assertIn("proof.IdentityGroupsSha256 is not { Length: 64 }", rehearsal)
        self.assertIn("LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO", rehearsal)
        for forbidden in ("PublishAsync(", "PersistBatchAsync(", "EnsureProductiveCertified("):
            self.assertNotIn(forbidden, rehearsal)

    def test_typed_dispatch_rejects_replay_and_wrong_version(self):
        self.assertIn("request.Mode == LinkageRunType.REPLAY", self.typed)
        self.assertIn("request.ModelVersion is int version && version != 9", self.typed)
        self.assertIn("ct.ThrowIfCancellationRequested();", self.typed)

    def test_certification_gate_precedes_any_productive_execution(self):
        self.assertIn("boundary.EnsureProductiveCertified();", self.typed)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", self.typed)
        self.assertLess(
            self.typed.index("boundary.EnsureProductiveCertified();"),
            self.typed.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"))
        for forbidden in ("AcquireExclusiveJobAsync(", "GetModelByVersionAsync(",
                          "PersistBatchAsync(", "PublishAsync(", "OpenAsync("):
            self.assertNotIn(forbidden, self.typed)

    def test_general_dispatch_rejects_v9_before_legacy_sql(self):
        gate = self.general.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED")
        self.assertIn("request.ModelVersion == 9", self.general[:gate])
        self.assertIn("request.Mode != LinkageRunType.REPLAY", self.general[:gate])
        self.assertIn('configuration["Linkage:V9SelectionFile"]', self.general[:gate])
        self.assertIn('configuration.GetValue<int?>("Linkage:SelectedModelVersion") == 9',
                      self.general[:gate])
        self.assertLess(gate, self.general.index("AcquireExclusiveJobAsync("))
        self.assertLess(gate, self.general.index("await linkage.GetModelByVersionAsync("))


if __name__ == "__main__":
    unittest.main()
