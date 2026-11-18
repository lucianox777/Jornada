"""V9 synthetic SQL ledger transaction safety structural regressions."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
LEDGER = (ROOT / "src/Jornada.Linkage.Runner/V9SyntheticNonPublishingSqlLedger.cs").read_text()


class SyntheticLedgerRollbackContract(unittest.TestCase):
    def test_disposable_guard_precedes_transaction(self):
        self.assertLess(LEDGER.index('"JornadaHarness"'), LEDGER.index('BeginTransactionAsync('))

    def test_all_writes_share_one_transaction(self):
        self.assertEqual(LEDGER.count('BeginTransactionAsync('), 1)
        self.assertEqual(LEDGER.count('CommitAsync('), 1)
        self.assertIn('IsolationLevel.Serializable', LEDGER)
        self.assertIn('INSERT identidade.v9_synthetic_rehearsal_batch', LEDGER)
        self.assertIn('INSERT identidade.v9_synthetic_rehearsal_decision', LEDGER)

    def test_cancel_before_commit_and_preserve_original_failure(self):
        closing = LEDGER.split('IF @@ROWCOUNT <> 1 THROW 52543', 1)[1]
        self.assertLess(closing.index('cancellationToken.ThrowIfCancellationRequested();'),
                        closing.index('transaction.CommitAsync(cancellationToken)'))
        self.assertIn('await transaction.RollbackAsync(CancellationToken.None)', closing)
        self.assertIn('catch (Exception rollbackError) when', closing)
        self.assertIn('throw;', closing)

    def test_no_productive_publication(self):
        self.assertNotIn('UPDATE identidade.linkage_run', LEDGER)
        self.assertNotIn('INSERT identidade.linkage_resultado', LEDGER)
        self.assertIn('ProductionBatchPipelineCertified = false', LEDGER)


if __name__ == "__main__":
    unittest.main()
