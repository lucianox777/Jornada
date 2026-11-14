#!/usr/bin/env python3
"""Contracts for bounded, secret-free public failure-stage evidence in V9 100k."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs"
COORD = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs"
WORKFLOW = ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml"

PHASES = (
    "CLI_ARGUMENTS_AND_PLAN",
    "PLAN_VALIDATED_AND_OUTPUT_ADMISSION",
    "TRAIN_HASH_AND_MANIFEST",
    "TRAIN_CSV_LOAD",
    "TRAIN_SPLIT_AND_PHYSICAL_SQL_FIT",
    "MU_VARIANT_EXPANSION",
    "TRAIN_SPLIT",
    "DECLARED_REQUESTS_AND_CP",
    "PHYSICAL_SQL_BATCHES",
    "FIT_EVIDENCE_EXPORT",
)


class V9CalibratorSafeStageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cli = CLI.read_text(encoding="utf-8")
        cls.coord = COORD.read_text(encoding="utf-8")
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")

    def test_failures_publish_only_allowlisted_static_stage_without_exception_message(self):
        full = self.cli + self.coord
        for phase in PHASES:
            self.assertIn('"' + phase + '"', full)
        self.assertIn('safeFailureStage = safeStage', self.cli)
        self.assertIn('"V9_CALIBRATOR_SAFE_STAGE:" + safeStage', self.cli)
        self.assertIn('exceptionType = exception.GetType().Name', self.cli)
        self.assertNotRegex(self.cli, r'safeFailureStage\s*=\s*exception')
        self.assertNotIn('exception.Message', self.cli)
        self.assertNotIn('exception.ToString()', self.cli)

    def test_all_stages_are_hardcoded_literals_not_external_data(self):
        self.assertIn('Action<string> updateSafeStage', self.cli)
        self.assertIn('Action<string>? reportSafeStage', self.coord)
        self.assertIn('reportSafeStage: updateSafeStage', self.cli)
        for match in re.findall(r'(?:updateSafeStage|reportSafeStage\?\.Invoke)\(([^\n]+)\);', self.cli + self.coord):
            self.assertRegex(match.strip(), r'^"[A-Z_]+"$')

    def test_100k_still_manual_sha_pinned_and_uploads_only_aggregate_failure_stage(self):
        self.assertIn('workflow_dispatch:', self.workflow)
        self.assertIn('github.actor == github.repository_owner', self.workflow)
        self.assertIn('security_preflight_approved == true', self.workflow)
        self.assertIn('results/interrupted.json', self.workflow)
        self.assertNotIn('last-error.log', self.workflow)
        self.assertNotIn('people-100k/observacoes.csv\n', self.workflow)
        self.assertNotRegex(self.workflow, r'(?m)^\s+push:\s*$')
        self.assertNotRegex(self.workflow, r'(?m)^\s+pull_request:\s*$')


    def test_optional_replay_performance_diagnostics_preserve_core_contract(self):
        # Source contract only: the physical A/B experiment remains mandatory.
        self.assertIn('Environment.GetEnvironmentVariable("V9_REPLAY_TIMING_DIAGNOSTICS") == "1"', self.coord)
        self.assertIn('var indexClock = diagnoseReplayTiming ? Stopwatch.StartNew() : null;', self.coord)
        self.assertIn('var replayClock = diagnoseReplayTiming ? Stopwatch.StartNew() : null;', self.coord)
        self.assertIn('if (indexClock is not null)', self.coord)
        self.assertIn('if (replayClock is not null)', self.coord)
        self.assertIn('[V9 PERF DIAG] phase=index_build', self.coord)
        self.assertIn('[V9 PERF DIAG] phase=frozen_replay', self.coord)
        # Diagnostics must not gate or reduce the number of physical frozen replays.
        self.assertEqual(self.coord.count('await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync('), 1)
        self.assertIn('reportPhysicalCompletion?.Invoke(individualClock.Elapsed, result[^1], replayElapsedMilliseconds);', self.coord)
        self.assertIn('var metricsByKey = run.SourceResolveValidation!.ToLookup(', self.coord)
        self.assertIn('var frozenByKey = run.FrozenConfigurations!.ToLookup(', self.coord)
        self.assertIn('var optionsById = run.CalibrationReport.Options.ToLookup(', self.coord)


    def test_one_numbered_github_log_row_per_hypothesis_without_pipes(self):
        # PO clarified with a screenshot: one GitHub-numbered physical row per
        # hypothesis, mobile auto-wrap, with new fields and without pipe delimiters.
        # Explicit newlines would create SEVEN separately numbered GitHub rows.
        self.assertIn('var replayTimingEnabled = Environment.GetEnvironmentVariable("V9_REPLAY_TIMING_DIAGNOSTICS") == "1";', self.coord)
        self.assertIn('var diagnosticSuffix = replayTimingEnabled && replayElapsedMilliseconds is { } replayMs', self.coord)
        self.assertIn(' replay_elapsed_ms={replayMs:F3}', self.coord)
        self.assertIn('if (!physicalProgressEnabled)', self.coord)
        self.assertIn('if (replayTimingEnabled && replayElapsedMilliseconds is { } ms)', self.coord)
        self.assertIn('reportHypothesis?.Invoke(evaluation);', self.coord)
        progress = self.coord.split('void ReportPhysicalCompletion(', 1)[1].split(
            'reportSafeStage?.Invoke("PHYSICAL_SQL_BATCHES");', 1)[0]
        # Exactly one WriteLine, one lock and one Flush for NORMAL progress.
        # The separate diagnostic-only branch is disabled by default.
        self.assertEqual(progress.count('lock (Console.Out)'), 2)
        self.assertEqual(progress.count('Console.WriteLine('), 2)
        self.assertEqual(progress.count('Console.Out.Flush();'), 1)
        normal = progress.split('lock (Console.Out)\n            {', 1)[1]
        self.assertIn('[V9 PROGRESS] {done}/{physicalTotal} ({100d * done / physicalTotal:F1}%) timestamp=', normal)
        for field in (' hipotese=', ' resultado=', ' restante=', ' limite=', ' status='):
            self.assertIn(field, normal)
        self.assertIn('status={status}{diagnosticSuffix}', normal)
        self.assertNotIn(r'\n', normal)
        self.assertNotIn(' | ', normal)
        self.assertNotIn('ETA_min=', normal)
        self.assertNotIn('margem_etapa_315min=', normal)
        self.assertIn('Interlocked.Increment(ref physicalCompleted)', progress)
        self.assertIn('DateTimeOffset.UtcNow', progress)
        self.assertIn('physicalDeadlineUtc - nowUtc', progress)
        self.assertIn('status = remaining > TimeSpan.Zero', progress)
        self.assertIn('stepStartUtc.AddMinutes(340)', self.coord)
        replay = self.coord.split('var replayClock = diagnoseReplayTiming ?', 1)[1].split(
            'p95 = measured.P95Milliseconds;', 1)[0]
        self.assertIn('replayElapsedMilliseconds = replayClock.Elapsed.TotalMilliseconds;', replay)
        self.assertNotIn('lock (Console.Out)', replay)
        self.assertNotIn('Console.WriteLine', replay)
        self.assertEqual(self.coord.count(
            'reportPhysicalCompletion?.Invoke(TimeSpan.Zero, evaluation, null);'), 2)
        self.assertIn('reportPhysicalCompletion?.Invoke(individualClock.Elapsed, result[^1], replayElapsedMilliseconds);',
                      self.coord)

if __name__ == "__main__":
    unittest.main()
