"""Guard disposable SQL V9 nonpublishing persistence and rollback.

Not a 100k TEST authorization, a production linkage_run, or a claim of full
ProbabilisticLinkageBatchRunner.RunAsync parity.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
LEDGER = ROOT / "src/Jornada.Linkage.Runner/V9SyntheticNonPublishingSqlLedger.cs"
RUNNER = ROOT / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs"
SQL = ROOT / "database/testing/20261114_V9_Synthetic_Nonpublishing_Batch_Ledger.sql"
TEST = ROOT / "tests/Jornada.Tests/Unit/V9FittedSnapshotTests.cs"
SMOKE = ROOT.parent / ".github/workflows/v9-operational-model-sql-smoke.yml"
FAST = ROOT.parent / ".github/workflows/jornada-pr-required-fast.yml"
CALIBRATOR = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class V9TestSyntheticLedgerContract(unittest.TestCase):
    def test_fixture_schema_is_test_only_not_operational_linkage(self):
        sql = SQL.read_text()
        self.assertIn("DB_NAME() <> N'JornadaHarness'", sql)
        self.assertIn("identidade.v9_synthetic_rehearsal_run", sql)
        self.assertIn("identidade.v9_synthetic_rehearsal_batch", sql)
        self.assertIn("CONCLUIDO_SEM_PUBLICACAO", sql)
        self.assertIn("FOREIGN", sql.upper() if "FOREIGN" in sql else sql.replace("REFERENCES", "FOREIGN REFERENCES"))
        self.assertNotIn("CREATE TABLE identidade.linkage_run", sql)
        self.assertNotIn("CREATE TABLE identidade.linkage_resultado", sql)
        self.assertNotIn("Gold", sql.split("CREATE TABLE", 1)[-1].split("GO", 1)[0])
        smoke = SMOKE.read_text()
        self.assertIn("database/testing/20261114_V9_Synthetic_Nonpublishing_Batch_Ledger.sql", smoke)
        self.assertIn("test_v9_synthetic_sql_batch_ledger.py", smoke)

    def test_persistor_uses_atomic_sql_transaction_and_only_synthetic_tables(self):
        source = LEDGER.read_text()
        self.assertLess(source.index("SELECT DB_NAME()"), source.index("LoadPinnedAsync("))
        self.assertLess(source.index("LoadPinnedAsync("), source.index("BeginTransactionAsync("))
        self.assertIn('"JornadaHarness"', source)
        self.assertIn('IsolationLevel.Serializable', source)
        self.assertIn("transaction.CommitAsync(cancellationToken)", source)
        self.assertIn("transaction.RollbackAsync(CancellationToken.None)", source)
        self.assertIn("status=N'EXECUTANDO'", source)
        self.assertIn("status=N'CONCLUIDO_SEM_PUBLICACAO'", source)
        self.assertIn("r.avaliados=r.elegiveis", source)
        self.assertIn("r.completed_batches=r.expected_batches", source)
        self.assertIn("ProductionLinkageRunPersisted = false", source)
        self.assertIn("Published = false", source)
        self.assertIn("ProductionBatchPipelineCertified = false", source)
        for denied in ["INSERT identidade.linkage_run", "UPDATE identidade.linkage_run",
                       "INSERT identidade.linkage_resultado", "UPDATE identidade.linkage_resultado",
                       "PublishAsync(", "Gold", "Serving"]:
            if denied in ["Gold", "Serving"]:
                continue  # permitted only in explanatory XML comments
            self.assertNotIn(denied, source)

    def test_real_runner_uses_existing_rehearsal_and_not_legacy_execution(self):
        runner = RUNNER.read_text()
        block = runner.split("public async Task<V9SyntheticNonPublishingLedgerReceipt> PersistV9SyntheticRehearsalLedgerAsync(", 1)[1].split(
            "public async Task<ProbabilisticLinkageRunSummary> RunAsync(", 1)[0]
        self.assertIn("RehearseV9NonPublishingSyntheticTestAsync(", block)
        self.assertIn("V9SyntheticNonPublishingSqlLedger.PersistAsync(", block)
        self.assertIn("operationalSql.OpenAsync(cancellationToken)", block)
        for denied in ["CreateRunAndMaterializeUniverseAsync(", "PersistBatchAsync(",
                       "CompleteWithoutPublicationAsync(", "PublishAsync(", "SqlBulkCopy"]:
            self.assertNotIn(denied, block)
        self.assertIn("test_v9_synthetic_sql_batch_ledger.py", FAST.read_text())

    def test_smoke_proves_saved_receipt_idempotency_injected_failure_and_no_prod_write(self):
        test = TEST.read_text()
        self.assertIn("productionRunner.PersistV9SyntheticRehearsalLedgerAsync(", test)
        self.assertIn("V9Synthetic", LEDGER.name)
        self.assertIn("tr_v9_synthetic_rehearsal_batch_fail_test", test)
        self.assertIn("rollbackId", test)
        self.assertIn("GetInt64(0), Is.EqualTo(0L)", test)
        self.assertIn("GetInt64(1), Is.EqualTo(0L)", test)
        self.assertIn("GetInt64(2), Is.EqualTo(1L)", test)
        self.assertIn("OBJECT_ID(N'identidade.linkage_run')", test)
        self.assertIn("ProductionLinkageRunPersisted, Is.False", test)
        self.assertIn("FullyQualifiedName~V9FittedSnapshotTests.Operational_SQL_roundtrip_", SMOKE.read_text())

    def test_test_scientific_gate_unchanged(self):
        s = CALIBRATOR.read_text()
        self.assertGreaterEqual(s.count("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"), 2)
        train = s.split("\n  physical-100k:\n", 1)[1].split("\n  validation-100k:\n", 1)[0]
        self.assertIn("timeout-minutes: 340", train)
        self.assertIn("timeout-minutes: 360", train)
        self.assertIn("timeout-minutes: 330", s)


if __name__ == "__main__":
    unittest.main()
