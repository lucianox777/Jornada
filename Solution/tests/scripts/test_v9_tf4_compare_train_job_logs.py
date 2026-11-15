#!/usr/bin/env python3
"""Offline fixtures for sanitized V9 TRAIN performance comparator; NO 100k/SQL."""
import importlib.util
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/v9-tf4-compare-train-job-logs.py"
spec = importlib.util.spec_from_file_location("v9_tf4_timing_comparison", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def log_lines(shas, seconds=(1, 2, 3), add_group=True):
    lines = []
    if add_group:
        lines.append("2026-11-14T03:01:00.000Z [V9 SQL GROUP] batch=1 | "
                     "requests=64 | options=32 | margins=2 | run_async_seconds=160.9")
    for n, (sha, second) in enumerate(zip(shas, seconds), start=1):
        lines.append(
            f"2026-11-14T03:02:{second:02d}.000Z [V9 PROGRESS] "
            f"{n}/324 (0.1%) timestamp=2026-11-14T03:02:{second:02d}+00:00 "
            f"hipotese={sha} resultado=AVALIADA "
            "restante=GITHUB_CONTROLA eta_min=100.0")
    return "\n".join(lines) + "\n"


class V9Tf4PerformanceComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def save(self, name, content):
        path = self.root / name
        path.write_text(content, encoding="utf-8")
        return path

    def test_matching_hypotheses_with_different_completion_order_is_not_code_regression(self):
        shas = ["a" * 12, "b" * 12, "c" * 12]
        a = mod.read_log(self.save("a.log", log_lines(shas)))
        b = mod.read_log(self.save("b.log", log_lines(
            [shas[0], shas[2], shas[1]], seconds=(1, 3, 5))))
        report = mod.compare(a, b)
        self.assertEqual(report["sharedHypothesisShas"], 3)
        self.assertEqual(report["commonStatusMismatches"], 0)
        self.assertFalse(report["sameOrderedShaPrefix"])
        self.assertFalse(report["causalityCertified"])
        self.assertFalse(report["scientificTestUsed"])
        self.assertEqual(report["sqlGroupCountBaseline"], 1)
        self.assertTrue(report["sqlGroups"][0]["signatureCompatible"])
        self.assertEqual(report["sqlGroups"][0]["candidateOverBaseline"], 1)

    def test_reject_nonconsecutive_or_duplicate_progress_and_missing_timestamps(self):
        shas = ["a" * 12, "b" * 12, "c" * 12]
        good = log_lines(shas)
        invalid = {
            "gap": good.replace("2/324", "4/324"),
            "duplicate": good.replace("hipotese=" + shas[1], "hipotese=" + shas[0]),
            "timestamp": good.replace("2026-11-14T03:02:02.000Z [V9 PROGRESS]",
                                      "[V9 PROGRESS]"),
            "backwards": good.replace("03:02:02.000Z [V9 PROGRESS]",
                                       "03:02:00.000Z [V9 PROGRESS]"),
            "no-progress": "2026-11-14T03:01:00.000Z [V9 SQL GROUP] batch=1 | "
                           "requests=64 | options=32 | margins=2 | run_async_seconds=160.9",
        }
        for name, value in invalid.items():
            with self.subTest(name=name):
                with self.assertRaisesRegex(ValueError, "V9_TIMING"):
                    mod.read_log(self.save(f"{name}.log", value))

    def test_mismatched_result_counts_and_unknown_sha_are_evidence_not_certification(self):
        a = mod.read_log(self.save("a.log", log_lines(["a" * 12, "b" * 12, "c" * 12])))
        b_text = log_lines(["a" * 12, "d" * 12], seconds=(1, 4))
        b_text = b_text.replace("resultado=AVALIADA", "resultado=REJEITADA", 1)
        b = mod.read_log(self.save("b.log", b_text))
        r = mod.compare(a, b)
        self.assertEqual(r["candidateCompleted"], 2)
        self.assertEqual(r["candidateUnknownHypothesisShas"], 1)
        self.assertEqual(r["commonStatusMismatches"], 1)
        self.assertFalse(r["productionParityCertified"])

    def test_same_group_sha_matched_even_when_sql_completion_order_swaps(self):
        shas = ["a" * 12, "b" * 12, "c" * 12]
        g1, g2 = "1" * 12, "2" * 12
        def sql(sha, seconds):
            return ("2026-11-14T03:01:00.000Z [V9 SQL GROUP] batch=1 | "
                    "requests=64 | options=32 | margins=2 | "
                    f"run_async_seconds={seconds:.1f} | group_sha={sha}\n")
        def phase(sha, sql_seconds, post_seconds):
            return ("2026-11-14T03:03:00.000Z [V9 PERF GROUP] batch=1 | "
                    f"group_sha={sha} | requests=64 | replay_calls=64 | "
                    f"sql_seconds={sql_seconds:.1f} | post_sql_seconds={post_seconds:.1f} | "
                    f"total_seconds={sql_seconds+post_seconds:.1f}\n")
        progress = log_lines(shas, add_group=False)
        old = mod.read_log(self.save(
            "phase-a.log", sql(g1, 100) + sql(g2, 200) + progress
            + phase(g1, 100, 300) + phase(g2, 200, 400)))
        new = mod.read_log(self.save(
            "phase-b.log", sql(g2, 260) + sql(g1, 120) + progress
            + phase(g2, 260, 600) + phase(g1, 120, 450)))
        report = mod.compare(old, new)
        self.assertEqual([g["matchedBy"] for g in report["sqlGroups"]],
                         ["sha", "sha"])
        self.assertEqual([g["groupSha"] for g in report["sqlGroups"]], [g1, g2])
        self.assertEqual([g["candidateOverBaseline"] for g in report["sqlGroups"]],
                         [1.2, 1.3])
        self.assertEqual(report["phaseGroupCountBaseline"], 2)
        self.assertEqual(report["phaseGroupCountCandidate"], 2)
        self.assertEqual(report["pairedPostSqlGroups"][0]["postSqlRatio"], 1.5)
        self.assertEqual(report["pairedPostSqlGroups"][1]["postSqlRatio"], 1.5)
        invalid = (sql(g1, 100) + progress + phase(g1, 100, 300)
                   .replace("total_seconds=400.0", "total_seconds=900.0"))
        with self.assertRaisesRegex(ValueError, "V9_TIMING_PHASE_GROUP_INCONSISTENT"):
            mod.read_log(self.save("bad-phase.log", invalid))

    def test_phase_instrumentation_preserves_scientific_call_and_progress_frequency(self):
        source = (ROOT / "src/Jornada.Linkage.Parameters.Worker/"
                  "V9NestedCalibrationCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("[V9 PERF GROUP] batch=", source)
        self.assertIn("group_sha={groupSha}", source)
        self.assertIn("post_sql_seconds={postSqlSeconds:F1}", source)
        self.assertIn("replay_calls={replayCalls}", source)
        self.assertIn("var sqlSeconds = physicalGroupClock.Elapsed.TotalSeconds;", source)
        self.assertIn("physicalGroupClock.Start();", source)
        self.assertEqual(source.count("[V9 PERF GROUP]"), 1)
        self.assertEqual(source.count(
            "await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync("), 1)
        self.assertIn("reportPhysicalCompletion?.Invoke(individualClock.Elapsed,"
                      " result[^1], replayElapsedMilliseconds);", source)

    def test_cli_emits_aggregate_json_and_refuses_overwrite(self):
        shas = ["a" * 12, "b" * 12, "c" * 12]
        a = self.save("a.log", log_lines(shas))
        b = self.save("b.log", log_lines(shas, seconds=(2, 4, 6)))
        target = self.root / "evidence.json"
        mod.main(["--baseline-log", str(a), "--candidate-log", str(b),
                  "--output", str(target)])
        report = json.loads(target.read_text(encoding="utf-8"))
        self.assertEqual(report["schema"], "V9_TF4_TRAIN_LOG_COMPARISON_V1")
        self.assertNotIn("2026-11-14T03:02", target.read_text())
        self.assertNotIn("sqlPassword", target.read_text())
        with self.assertRaises(SystemExit):
            mod.main(["--baseline-log", str(a), "--candidate-log", str(b),
                      "--output", str(target)])


if __name__ == "__main__":
    unittest.main()
