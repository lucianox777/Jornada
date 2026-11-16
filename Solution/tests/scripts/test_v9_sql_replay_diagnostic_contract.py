"""Fail-closed structural contract for the opt-in 100k SQL replay diagnostic."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9SqlReplayDiagnostic.cs").read_text()
ENTRY = (ROOT / "src/Jornada.Linkage.Parameters.Worker/Program.cs").read_text()
WORKFLOW = (ROOT.parent / ".github/workflows/v9-sql-replay-diagnostic.yml").read_text()
ORIGINAL = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlAdaptiveCalibrationProbe.cs").read_text()
HOLDOUT = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlFrozenHoldout.cs").read_text()
BOOT = (ROOT / "scripts/v9-tf-start-disposable-sql.sh").read_text()


class DiagnosticContract(unittest.TestCase):
    def test_explicit_isolated_entry_and_no_scientific_p95_changes(self):
        self.assertIn('public const string Flag = "--v9-diagnose-sql-replay";', SRC)
        self.assertIn('V9SqlReplayDiagnosticCli.RunAsync(guardedArgs)', ENTRY)
        self.assertIn('\nif (guardedArgs.Length > 0 && guardedArgs[0] == V9SqlReplayDiagnosticCli.Flag)\n', ENTRY)
        self.assertNotIn(r'\nif (guardedArgs.Length > 0 && guardedArgs[0] == V9SqlReplayDiagnosticCli.Flag)', ENTRY)
        self.assertIn('await V9PhysicalSqlAdaptiveCalibrationProbe.DiagnoseFrozenReplayPhasesAsync(', SRC)
        self.assertIn('V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges)', SRC)
        self.assertIn('scientific_p95_certified = false', SRC)
        self.assertIn('test_opened = false', SRC)
        self.assertIn('publication = false', SRC)
        self.assertNotIn('--v9-calibrate', WORKFLOW)
        self.assertNotIn('--v9-independent-test', WORKFLOW)
        self.assertNotIn('--v9-validate-finalists', WORKFLOW)

    def test_sql_is_same_query_and_indexes_but_diag_only(self):
        sql = 'SELECT DISTINCT s1.obs_id AS a,s2.obs_id AS b,s1.part'
        self.assertIn(sql, ORIGINAL)
        self.assertIn(sql, SRC)
        self.assertIn('CREATE UNIQUE CLUSTERED INDEX IX_v9_cal_pairs', ORIGINAL)
        self.assertIn('CREATE UNIQUE CLUSTERED INDEX IX_v9_cal_pairs', SRC)
        self.assertIn('BuildSignatures(validation, snapshot.Payload.BlockingPasses', SRC)
        for method in ['CreateTablesAsync', 'BulkObservationsAsync', 'BulkSignaturesAsync',
                       'MaterializePairsAsync', 'ReadValidationPairsAsync']:
            self.assertIn('await ' + method + '(', SRC)
        self.assertIn('SET STATISTICS XML ON', SRC)
        self.assertIn('SET STATISTICS IO ON', SRC)
        self.assertIn('SET STATISTICS TIME ON', SRC)
        self.assertIn('ExecuteReaderAsync', SRC)
        self.assertIn('ReadValidationPairsAsync(', HOLDOUT)

    def test_same_corpus_pinned_image_and_only_aggregates_uploaded(self):
        self.assertIn('6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1', SRC)
        self.assertIn('6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1', WORKFLOW)
        self.assertIn('82f348b7eafdd82cbcaa6055832838089d1adaf0c3be8d7fe7624d5104914555', WORKFLOW)
        self.assertIn('24f119ace8340d212dd567674b66da9b89d8ba8f28776cfc1a632d470ead9208', WORKFLOW)
        self.assertIn('14181765642', WORKFLOW)
        self.assertIn('mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89', BOOT)
        self.assertIn('v9-tf-start-disposable-sql.sh', WORKFLOW)
        self.assertIn('ReplaysMeasured', WORKFLOW)
        self.assertIn('==20', WORKFLOW)
        self.assertIn('==60', WORKFLOW)
        self.assertIn('path: Solution/.local/v9-sql-replay-diagnostic/evidence/sql-replay-phase-diagnostic.json', WORKFLOW)
        self.assertNotIn('upload-artifact', SRC)
        self.assertIn('if: always()', WORKFLOW)
        self.assertIn('pull_request:', WORKFLOW)
        self.assertIn('actions: read', WORKFLOW)


if __name__ == '__main__':
    unittest.main()
