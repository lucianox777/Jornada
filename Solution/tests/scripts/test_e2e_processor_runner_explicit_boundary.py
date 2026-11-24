"""Offline source-contract regression: Processor must not silently dispatch V9 linkage.

This is a static pre-integration guard, NOT proof of productive Runner parity.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "src"
PROCESSOR = ROOT / "Jornada.Processor.Worker"
RUNNER = ROOT / "Jornada.Linkage.Runner"


class ProcessorRunnerExplicitBoundaryTests(unittest.TestCase):
    def test_processor_loop_does_not_dispatch_probabilistic_runner(self):
        source = (PROCESSOR / "ProcessorWorker.cs").read_text(encoding="utf-8")
        self.assertIn("processor.ProcessNextAsync(stoppingToken)", source)
        self.assertNotRegex(source, r"\b(?:RunV9Async|ProbabilisticLinkageBatchRunner|IProbabilisticLinkageBatchRunner)\b")

    def test_missing_cpf_remains_pending_until_explicit_linkage(self):
        source = (PROCESSOR / "IdentityResolutionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("AGUARDA_LINKAGE_SOB_DEMANDA", source)
        self.assertIn("ResolutionStatus.NAO_RESOLVIDO", source)
        self.assertNotIn("RunV9Async(", source)

    def test_v9_dispatch_remains_fail_closed_before_legacy_pipeline(self):
        source = (RUNNER / "ProbabilisticLinkageBatchRunner.cs").read_text(encoding="utf-8")
        self.assertIn("RunCoreAsync(request, controlledDev: false, ct)", source)
        dispatch = source.index("private async Task<ProbabilisticLinkageRunSummary> RunCoreAsync(")
        guard = source.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", dispatch)
        lease = source.index("Acquire", dispatch) if "Acquire" in source[dispatch:] else -1
        self.assertGreater(guard, dispatch)
        if lease >= 0:
            self.assertLess(guard, lease)
        self.assertIn('configuration["Linkage:V9SelectionFile"]', source[dispatch:guard])


if __name__ == "__main__":
    unittest.main()
