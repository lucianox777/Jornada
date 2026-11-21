"""Contract: scientific TEST consumes only VALIDATION winner; legacy compatibility belongs to executor.

This is a static guard, NOT physical parity certification and NOT TEST authorization.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parent


class V9TestExecutorBoundaryContract(unittest.TestCase):
    def test_standard_workflow_generates_test_after_validation(self):
        wf = (REPO / ".github/workflows/v9-standard-linear-100k.yml").read_text(encoding="utf-8")
        self.assertNotIn('V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED', wf)
        self.assertNotIn('prepare-test-sealed:', wf)
        self.assertIn("needs: prepare-100k", wf)
        self.assertNotIn("needs.prepare-test-sealed.result", wf)
        self.assertIn("PREPARATION_RESULT:", wf)
        self.assertNotIn('inputs.test_required', wf)
        self.assertIn("needs.validation-100k.outputs.has_winner == 'true'", wf)
        self.assertIn('has_winner: ${{ steps.operational_file.outputs.has_model }}', wf)
        self.assertIn('needs.validation-100k.result == \'success\'', wf)
        self.assertIn('test-100k:', wf)
        self.assertNotIn('      test_required:', wf)
        self.assertIn("needs: admission", wf)
        self.assertIn("V9_TEST_REQUESTED: ${{ needs.validation-100k.outputs.has_winner == 'true' && 'true' || 'false' }}", wf)
        self.assertIn('V9_VALIDATION_HAS_WINNER: ${{ needs.validation-100k.outputs.has_winner }}', wf)
        self.assertIn('V9_VALIDATION_WINNER_OUTPUT_MISSING', wf)
        self.assertIn('needs: validation-100k', wf)

    def test_test_consumes_validation_winner_without_refitting(self):
        cli = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs").read_text(encoding="utf-8")
        self.assertIn('--v9-independent-test', cli)
        self.assertIn('V9ValidatedOperationalModelFile.ReadPinnedAsync(winnerFile)', cli)
        self.assertIn('No fit, winner selection, promotion or activation can run here.', cli)


    def test_test_job_receives_only_validation_artifact_and_disposable_sql(self):
        wf = (REPO / ".github/workflows/v9-standard-linear-100k.yml").read_text(encoding="utf-8")
        test_job = wf.split("\n  test-100k:", 1)[1].split("\n  closeout", 1)[0]
        self.assertIn('name: v9-tf4-validated-operational-model-', test_job)
        self.assertIn('Download ONLY the final JSON produced by VALIDATION', test_job)
        self.assertIn("Generate fresh independent TEST 100k after winner is frozen", test_job)
        self.assertIn('Start brand-new disposable SQL database only for TEST', test_job)
        self.assertIn('--v9-independent-test "$model"', test_job)
        self.assertNotIn('v9-tf4-train-results-', test_job)
        self.assertNotIn('v9-tf4-audit-results-', test_job)

    def test_scientific_cli_rejects_non_disposable_sql_and_does_not_certify_itself(self):
        cli = (ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs").read_text(encoding="utf-8")
        self.assertIn('InitialCatalog != "JornadaHarness"', cli)
        self.assertIn('DataSource != "127.0.0.1,1433"', cli)
        self.assertIn("JornadaV9IndependentTest", cli)
        self.assertNotIn("ConnectionStrings__Jornada", cli)
        self.assertIn('V9OperationalPinnedCohortRuntime.LoadPinnedAsync(', cli)
        self.assertIn('V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync(', cli)
        self.assertIn('productionRunnerFullPipelineParityCertified = false', cli)
        self.assertIn('operationalModelActive = false', cli)
        self.assertIn('operationalModelPublished = false', cli)
        self.assertIn('refitted = false', cli)
        self.assertIn('activated = false', cli)

    def test_executor_retains_legacy_and_explicit_v9_routes(self):
        runner = (ROOT / "src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs").read_text(encoding="utf-8")
        self.assertIn('LoadLegacyCandidatesAsync', runner)
        self.assertIn('GetActiveModelAsync', runner)
        self.assertIn("SELECT modelo_id FROM identidade.modelo_linkage WHERE status='ATIVO'", runner)
        self.assertIn('GetOrLoadRuntimeSnapshotAsync(modeloId, ct)', runner)
        self.assertIn('BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation)', runner)
        self.assertIn('observation.DataNascimento is null', runner)
        self.assertIn('return await LoadLegacyCandidatesAsync(', runner)
        self.assertIn('LoadPinnedV9OperationalRuntimeAsync(', runner)
        self.assertIn('LoadPinnedV9GroupedCohortRuntimeAsync(', runner)
        self.assertIn('not wired into GetActiveModelAsync, ResolveWithoutCpfAsync', runner)
        self.assertIn('V9OperationalPinnedCohortRuntime.LoadPinnedAsync', runner)
        converter = (ROOT / "src/Jornada.Linkage.Core/V9OperationalSqlModelConverter.cs").read_text(encoding="utf-8")
        self.assertIn('não usar modelo legado', converter)
        self.assertIn('TEST_ONLY', converter)


if __name__ == "__main__":
    unittest.main()
