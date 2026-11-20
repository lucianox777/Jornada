"""Offline regression contracts for the still-uncertified V9 TEST_ONLY boundary.

These checks deliberately do not certify physical production pipeline parity.
They prevent accidental removal of fail-closed guards while #1575/#1584
remain open. No TEST, TRAIN, SQL connection or corpus is used.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
RUNNER = ROOT / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs"
TEST = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"


class TestV9TestOnlyFailClosedContract(unittest.TestCase):
    def test_test_cannot_start_before_parity_certification(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        prep = workflow.split("\n  prepare-test-100k:\n", 1)[1].split(
            "\n  physical-100k:\n", 1)[0]
        train = workflow.split("\n  physical-100k:\n", 1)[1].split(
            "\n  validation-100k:\n", 1)[0]
        for stage, start in ((prep, "--people 100000"), (train, "--v9-calibrate")):
            marker = "V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"
            self.assertIn(marker, stage)
            self.assertLess(stage.index(marker), stage.index(start))
        self.assertEqual(
            workflow.count('productionRunnerFullPipelineParityCertified"] is False'), 2,
            "Both synthetic and external-db TEST reports must remain uncertified",
        )

    def test_runner_refuses_v9_production_fallback(self):
        runner = RUNNER.read_text(encoding="utf-8")
        run = runner.split("public async Task<ProbabilisticLinkageRunSummary> RunAsync(", 1)[1]
        guard = run.split("var drainTimeoutSeconds", 1)[0]
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", guard)
        self.assertIn("request.ModelVersion == 9", guard)
        self.assertIn('configuration.GetValue<int?>("Linkage:SelectedModelVersion") == 9', guard)
        self.assertIn('configuration["Linkage:V9SelectionFile"]', guard)
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", guard)
        self.assertIn('"JornadaHarness"', guard)
        self.assertLess(
            guard.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"),
            run.index("AcquireExclusiveJobAsync"),
        )

    def test_test_uses_exact_pinned_winner_without_claiming_parity(self):
        test = TEST.read_text(encoding="utf-8")
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync(winnerFile)", test)
        self.assertIn("validatedFileWinner.ContentSha256 != winnerSha", test)
        self.assertIn('InitialCatalog != "JornadaHarness"', test)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", test)
        self.assertIn("conversion.ByteExactRoundTrip", test)
        self.assertIn("V9IndependentTestCorpusReader.ReadAsync(fullTestDir)", test)
        self.assertLess(
            test.index("V9OperationalPinnedCohortRuntime.LoadPinnedAsync("),
            test.index("V9IndependentTestCorpusReader.ReadAsync(fullTestDir)"),
        )
        self.assertIn("EvaluateFrozenTestAsync(", test)
        self.assertIn("evaluated.Refitted", test)
        self.assertIn("evaluated.SnapshotSha256 != winnerSha", test)
        self.assertIn("operationalModelActive = false", test)
        self.assertIn("operationalModelPublished = false", test)
        self.assertIn("productionRunnerFullPipelineParityCertified = false", test)
        self.assertNotIn("ProbabilisticLinkageBatchRunner.RunAsync", test)


if __name__ == "__main__":
    unittest.main()
