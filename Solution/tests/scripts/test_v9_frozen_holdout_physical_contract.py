"""Guard the certified V9 frozen holdout physical replay protocol.

These are source-level regression guards, not a substitute for SQL integration
or scientific parity tests. They intentionally forbid silent SQL caching in P95.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
HOLDOUT = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlFrozenHoldout.cs"
COORDINATOR = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs"


class FrozenHoldoutPhysicalContractTests(unittest.TestCase):
    def test_full_sql_preparation_remains_inside_each_timed_iteration(self):
        source = HOLDOUT.read_text()
        loop = source.index("for (var iteration = 0;")
        timer = source.index("var timer = Stopwatch.StartNew();", loop)
        prepare = source.index("await PrepareFrozenValidationPairsAsync(", timer)
        replay = source.index("V9FrozenSourceResolveReplay.Replay(", prepare)
        sample = source.index("samples.Add(timer.Elapsed.TotalMilliseconds);", replay)
        self.assertLess(loop, timer)
        self.assertLess(timer, prepare)
        self.assertLess(prepare, replay)
        self.assertLess(replay, sample)
        self.assertIn('"V9_FROZEN_HOLDOUT_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1"', source)
        self.assertIn("execution.EndToEndRepetitions >= 20", source)

    def test_physical_preparation_is_not_bypassed_or_cached(self):
        source = HOLDOUT.read_text()
        start = source.index("private static async Task<IReadOnlyList<(int A, int B)>> PrepareFrozenValidationPairsAsync(")
        body = source[start:]
        expected = (
            "BuildSignatures(validation, snapshot.Payload.BlockingPasses,",
            "CountPairIncidences(signatures, validation, execution.MaximumCandidatePairIncidences)",
            "await CreateTablesAsync(connection, cancellationToken);",
            "await BulkObservationsAsync(connection, validation, cancellationToken);",
            "await BulkSignaturesAsync(connection, signatures, cancellationToken);",
            "await MaterializePairsAsync(connection, cancellationToken);",
            "await ReadValidationPairsAsync(connection, cancellationToken);",
            "if (pairCount != edges.Count)",
        )
        for step in expected:
            with self.subTest(step=step):
                self.assertIn(step, body)
        self.assertEqual(source.count("await PrepareFrozenValidationPairsAsync("), 1)
        self.assertNotRegex(body, r"(?i)\b(?:cache|memoiz|GetOrAdd|TryGetValue)\s*\(")

    def test_coordinator_keeps_individual_replay_and_parity_checks(self):
        source = COORDINATOR.read_text()
        self.assertIn("foreach (var request in requests)", source)
        self.assertIn("await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(", source)
        self.assertIn("measured.Metric.TrueAcceptedSourceObservations != metric.TrueAcceptedSourceObservations", source)
        self.assertIn("measured.Metric.FalseAcceptedSourceObservations != metric.FalseAcceptedSourceObservations", source)
        self.assertIn("measured.Metric.Conflicts != metric.Conflicts", source)
        self.assertIn("measured.Metric.Unresolved != metric.Unresolved", source)
        self.assertIn("V9CalibradorSafeFailureCategory.PhysicalReplayMismatch", source)


if __name__ == "__main__":
    unittest.main()
