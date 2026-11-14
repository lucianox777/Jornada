"""GOV-DOC-01: production class opt-in V9 TEST is NOT legacy publishing RunAsync.

This fast contract complements, never replaces, real SQL Server smoke and a
future controlled production E2E proof. TRAIN/VALIDATION must remain unchanged.
"""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
RUNNER=ROOT / "src/Jornada.Linkage.Runner"
TESTS=ROOT / "tests/Jornada.Tests/Unit/V9FittedSnapshotTests.cs"
WORKFLOW=ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
FAST=ROOT.parent / ".github/workflows/jornada-pr-required-fast.yml"
SMOKE=ROOT.parent / ".github/workflows/v9-operational-model-sql-smoke.yml"


class V9ProductionBatchOptInSafety(unittest.TestCase):
    def test_actual_production_runner_has_isolated_nonpublishing_entry(self):
        src=(RUNNER / "ProbabilisticLinkageBatchRunner.cs").read_text()
        entry=src.split("public async Task<V9NonPublishingBatchExecutionReceipt> RehearseV9NonPublishingSyntheticTestAsync(",1)[1].split("public async Task<ProbabilisticLinkageRunSummary> RunAsync(",1)[0]
        self.assertIn("operationalSql.OpenAsync(cancellationToken)", entry)
        self.assertIn("V9ReadOnlyTestBatchRunner.RunAsync(connection,", entry)
        self.assertIn("proof.Resolved + proof.Unresolved + proof.Conflicts",entry)
        self.assertIn("LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO", entry)
        for forbidden in (
            "CreateRunAndMaterializeUniverseAsync(", "PersistBatchAsync(",
            "PublishAsync(", "CompleteWithoutPublicationAsync(",
            "pipelineCoordinator.AcquireExclusiveJobAsync(", "SqlBulkCopy",
        ):
            self.assertNotIn(forbidden, entry)

    def test_rehearsal_receipt_cannot_claim_real_run_or_parity_certification(self):
        src=(RUNNER / "V9NonPublishingBatchExecutionReceipt.cs").read_text()
        self.assertIn("bool PersistedRun = false", src)
        self.assertIn("bool Published = false", src)
        self.assertIn("bool ProductionBatchPipelineCertified = false", src)
        proof=(RUNNER / "V9OperationalCandidateGraphParityAudit.cs").read_text()
        self.assertIn("ProbabilisticLinkageBatchRunner.IsNoCandidateReason(", proof)
        self.assertIn("Resolved: resolved, Unresolved: unresolved,", proof)
        self.assertIn("Conflicts: conflicts, NoCandidateInBlock: noCandidate", proof)
        self.assertIn("if (observationsRead != ordered.Length", proof)
        self.assertIn("actualDirections.Values.Any(v => v != 3)", proof)
        self.assertIn("expected.SetEquals(actual)", proof)
        readonly=(RUNNER / "V9ReadOnlyTestBatchRunner.cs").read_text()
        self.assertLess(readonly.index("DB_NAME()"),readonly.index("V9OperationalPinnedCohortRuntime.LoadPinnedAsync("))
        self.assertIn('"JornadaHarness"', readonly)
        self.assertNotIn("UPDATE ", readonly)
        self.assertNotIn("INSERT INTO ", readonly)
        self.assertNotIn("DELETE FROM ", readonly)

    def test_sql_smoke_executes_actual_class_and_negative_database(self):
        s=TESTS.read_text()
        self.assertIn("productionRunner.RehearseV9NonPublishingSyntheticTestAsync(",s)
        self.assertIn('InitialCatalog = "master"',s)
        self.assertIn("wrongDatabaseRunner.RehearseV9NonPublishingSyntheticTestAsync(",s)
        self.assertIn("summary.ExpectedTerminalStatus,",s)
        self.assertIn("summary.PersistedRun, Is.False",s)
        self.assertIn("summary.Published, Is.False",s)
        self.assertIn("summary.ProductionBatchPipelineCertified, Is.False",s)
        self.assertIn("summary.DecisionsSha256, Is.EqualTo(parity.DecisionsSha256)",s)
        self.assertIn("summary.IdentityGroupsSha256, Is.EqualTo(parity.IdentityGroupsSha256)",s)
        self.assertIn("OBJECT_ID(N'identidade.linkage_run')",s)
        self.assertIn("test_v9_production_runner_test_only_bridge.py",FAST.read_text())
        self.assertIn("FullyQualifiedName~V9FittedSnapshotTests.Operational_SQL_roundtrip_",SMOKE.read_text())
        self.assertIn("one-winner-sql-smoke:",SMOKE.read_text())

    def test_test_100k_remains_blocked_and_train_not_touched(self):
        source=WORKFLOW.read_text()
        train=source.split("\n  physical-100k:\n",1)[1].split("\n  validation-100k:\n",1)[0]
        prep=source.split("\n  prepare-test-100k:\n",1)[1].split("\n  physical-100k:\n",1)[0]
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",train)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",prep)
        self.assertNotIn("timeout-minutes:",train)
        self.assertIn("v9-v3-four-name-100k-preaudit.py",train)
        self.assertNotIn("timeout-minutes:",source)


if __name__=="__main__":
    unittest.main()
