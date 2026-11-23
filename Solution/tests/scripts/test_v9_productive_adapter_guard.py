"""Static regression guards for uncertified V9 productive dispatch.

This test cannot prove runtime behavior, scientific correctness, or certification.
"""
from pathlib import Path
import unittest

RUNNER = (Path(__file__).resolve().parents[2] /
          "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs")


class V9ProductiveAdapterGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = RUNNER.read_text(encoding="utf-8")

    def test_typed_v9_stays_fail_closed(self):
        section = self.source.split(
            "public Task<ProbabilisticLinkageRunSummary> RunV9Async(", 1)[1].split(
            "public async Task<ProbabilisticLinkageRunSummary> RunV9ControlledDevAsync(", 1)[0]
        self.assertIn("boundary.EnsureProductiveCertified();", section)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", section)
        self.assertIn("RUNNER_V9_REPLAY_NOT_SUPPORTED", section)
        self.assertIn("RUNNER_V9_SELECTION_VERSION_MISMATCH", section)
        self.assertLess(section.index("boundary.EnsureProductiveCertified();"),
                        section.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"))

    def test_legacy_runasync_refuses_v9_selection(self):
        self.assertIn("RunCoreAsync(request, controlledDev: false, ct)", self.source)
        section = self.source.split(
            "private async Task<ProbabilisticLinkageRunSummary> RunCoreAsync(", 1)[1]
        self.assertIn("request.ModelVersion == 9", section)
        self.assertIn('configuration.GetValue<int?>("Linkage:SelectedModelVersion") == 9', section)
        self.assertIn('configuration["Linkage:V9SelectionFile"]', section)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", section)


if __name__ == "__main__":
    unittest.main()
