"""Offline regression of V9 productive dispatch: never silently use legacy SQL model."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs"


class V9ProductiveDispatchBoundary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        source = SOURCE.read_text(encoding="utf-8")
        # Regression: the productive typed entry ends at the explicit DEV
        # entry, while the public RunAsync facade shares the guarded core.
        typed_start = source.index("public Task<ProbabilisticLinkageRunSummary> RunV9Async(")
        dev_start = source.index(
            "public async Task<ProbabilisticLinkageRunSummary> RunV9ControlledDevAsync(", typed_start)
        facade_start = source.index(
            "public Task<ProbabilisticLinkageRunSummary> RunAsync(", dev_start)
        core_start = source.index(
            "private async Task<ProbabilisticLinkageRunSummary> RunCoreAsync(", facade_start)
        core_end = source.index("private async Task CaptureBronzeSourcesAsync(", core_start)
        cls.typed = source[typed_start:dev_start]
        cls.facade = source[facade_start:core_start]
        cls.run_source = source[core_start:core_end]

    def test_typed_dispatch_refuses_uncertified_productive_adapter(self):
        self.assertIn("boundary.EnsureProductiveCertified();", self.typed)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", self.typed)
        self.assertLess(self.typed.index("boundary.EnsureProductiveCertified();"),
                        self.typed.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"))
        self.assertIn("RUNNER_V9_REPLAY_NOT_SUPPORTED", self.typed)
        self.assertIn("RUNNER_V9_SELECTION_VERSION_MISMATCH", self.typed)

    def test_typed_dispatch_is_explicitly_non_operational(self):
        typed = self.typed
        self.assertIn("ct.ThrowIfCancellationRequested();", typed)
        self.assertIn("request.Mode == LinkageRunType.REPLAY", typed)
        self.assertIn("request.ModelVersion is int version && version != 9", typed)
        for forbidden in ("operationalSql.OpenAsync(",
                          "pipelineCoordinator.AcquireExclusiveJobAsync(",
                          "CreateRunAndMaterializeUniverseAsync(",
                          "PersistBatchAsync(",
                          "PublishAsync("):
            self.assertNotIn(forbidden, typed)
        self.assertTrue(typed.strip().endswith("}"))

    def test_legacy_runasync_rejects_all_explicit_v9_selectors_before_sql(self):
        self.assertIn("RunCoreAsync(request, controlledDev: false, ct)", self.facade)
        self.assertIn("RunCoreAsync(request, controlledDev: true, ct)",
                      SOURCE.read_text(encoding="utf-8"))
        run = self.run_source
        start = run.index("ValidateRequest(request);")
        lock = run.index("pipelineCoordinator.AcquireExclusiveJobAsync(")
        model = run.index("linkage.GetModelByVersionAsync(")
        guard = run.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED")
        self.assertLess(start, guard)
        self.assertLess(guard, lock)
        self.assertLess(guard, model)
        for selector in ('request.ModelVersion == 9',
                         'configuration.GetValue<int?>("Linkage:SelectedModelVersion") == 9',
                         'configuration["Linkage:V9SelectionFile"]'):
            self.assertIn(selector, run[start:guard])
        self.assertIn("request.Mode != LinkageRunType.REPLAY", run[start:guard])

    def test_synthetic_scorer_cannot_publish_or_target_operational_db(self):
        run = self.run_source
        start = run.index("if (linkage is IV9NonPublishingFrozenCohortScorer)")
        end = run.index("// A publicação exige a migration SQL de lote.", start)
        guard = run[start:end]
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", guard)
        self.assertIn("request.ReplaySourceRunId is not null", guard)
        self.assertIn("request.MaxRecords is not null", guard)
        self.assertIn("request.ModelVersion != 1", guard)
        self.assertIn('v9GuardCommand.CommandText = "SELECT DB_NAME();"', guard)
        self.assertIn('"JornadaHarness", StringComparison.Ordinal', guard)
        self.assertLess(start, run.index("pipelineCoordinator.AcquireExclusiveJobAsync("))


if __name__ == "__main__":
    unittest.main()
