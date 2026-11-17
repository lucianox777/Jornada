"""Contract: scientific TEST consumes only VALIDATION winner; legacy compatibility belongs to executor.

This is a static guard, NOT physical parity certification and NOT TEST authorization.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parent


class V9TestExecutorBoundaryContract(unittest.TestCase):
    def test_standard_workflow_stays_fail_closed_until_certification(self):
        wf = (REPO / ".github/workflows/v9-standard-linear-100k.yml").read_text(encoding="utf-8")
        self.assertIn('V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED', wf)
        self.assertIn('inputs.test_required == true', wf)
        self.assertIn('needs.validation-100k.result == \'success\'', wf)
        self.assertIn('test-100k:', wf)

    def test_test_consumes_validation_winner_without_refitting(self):
        cli = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs").read_text(encoding="utf-8")
        self.assertIn('--v9-independent-test', cli)
        self.assertIn('V9ValidatedOperationalModelFile.ReadPinnedAsync(winnerFile)', cli)
        self.assertIn('No fit, winner selection, promotion or activation can run here.', cli)

    def test_executor_retains_legacy_and_explicit_v9_routes(self):
        runner = (ROOT / "src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs").read_text(encoding="utf-8")
        self.assertIn('LoadLegacyCandidatesAsync', runner)
        self.assertIn('GetActiveModelAsync', runner)
        self.assertIn('V9OperationalPinnedCohortRuntime.LoadPinnedAsync', runner)
        converter = (ROOT / "src/Jornada.Linkage.Core/V9OperationalSqlModelConverter.cs").read_text(encoding="utf-8")
        self.assertIn('não usar modelo legado', converter)
        self.assertIn('TEST_ONLY', converter)


if __name__ == "__main__":
    unittest.main()
