"""Structural safety contract for V9 TEST/Runner shared frozen grouped replay.

This is NOT production-batch parity certification. It safeguards the incremental
runtime bridge while TEST admission remains fail-closed.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "src/Jornada.Linkage.Core"
WORKER = ROOT / "src/Jornada.Linkage.Parameters.Worker"
RUNNER = ROOT / "src/Jornada.Linkage.Runner"
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class V9OperationalCohortParityContract(unittest.TestCase):
    def test_single_identical_compiled_grouping_and_replay_source(self):
        for filename in (
            "V9BlindIncrementalIdentityGrouping.cs",
            "V9FrozenSourceResolveReplay.cs",
            "V9PhysicalCalibrationObservation.cs",
        ):
            self.assertTrue((CORE / filename).is_file(), filename)
            self.assertFalse((WORKER / filename).exists(), "Duplicated scientific kernel: " + filename)
        group = (CORE / "V9BlindIncrementalIdentityGrouping.cs").read_text()
        replay = (CORE / "V9FrozenSourceResolveReplay.cs").read_text()
        self.assertIn("public static V9BlindIncrementalGroupingResult BuildTest(", group)
        self.assertIn('partition == "TEST"', replay)
        self.assertIn("V9BlindIncrementalIdentityGrouping.BuildTest(rows, edges,", replay)
        self.assertIn("snapshot.RankPreview(", replay)
        self.assertIn("snapshot.ResolveRankedPreview(", replay)
        self.assertIn("new ReadOnlyDictionary<int, Guid>", replay)

    def test_runner_and_test_load_only_same_pinned_sql_winner(self):
        runtime = (CORE / "V9OperationalPinnedCohortRuntime.cs").read_text()
        prod = (RUNNER / "SqlProbabilisticIdentityLinkage.cs").read_text()
        cli = (WORKER / "V9IndependentTestCli.cs").read_text()
        holdout = (WORKER / "V9PhysicalSqlFrozenHoldout.cs").read_text()
        self.assertIn("V9OperationalSqlModelConverter.ReadPinnedAsync(", runtime)
        self.assertIn("V9OperationalSqlModelConverter.VerifyConversion(pinned)", runtime)
        self.assertIn("V9FrozenSourceResolveReplay.ReplayTest(winner, test, completePhysicalSqlEdges)", runtime)
        self.assertIn("LoadPinnedV9GroupedCohortRuntimeAsync(", prod)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", prod)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", cli)
        self.assertIn("operationalRuntime!.ReplayTest(validation, edges)", holdout)
        self.assertIn("if (pairCount != edges.Count)", holdout)
        self.assertIn('purpose == "VALIDATION" && operationalRuntime is not null', holdout)
        self.assertIn("V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges)", holdout)
        self.assertNotIn("V9FittedSnapshotArtifact.ReadPinnedAsync", cli)
        for forbidden in ("SelectScientificWinner", "selectedValidationSnapshotSha256", "fitAsync", "PublishAsync"):
            self.assertNotIn(forbidden, runtime)

    def test_no_change_to_train_validation_or_premature_test_enablement(self):
        workflow = WF.read_text()
        train = workflow.split("\n  physical-100k:\n", 1)[1].split("\n  validation-100k:\n", 1)[0]
        test = workflow.split("\n  test-100k:\n", 1)[1]
        prep = workflow.split("\n  prepare-test-100k:\n", 1)[1].split("\n  physical-100k:\n", 1)[0]
        self.assertIn("Run physical V9 CSharp on 324 V3-only four-name hypotheses", train)
        self.assertNotIn("timeout-minutes:", train)
        self.assertIn("v9-v3-four-name-100k-preaudit.py", train)
        self.assertIn("Mandatory fresh independent VALIDATION of frozen finalists", workflow)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", train)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", prep)
        self.assertIn("inputs.test_required == true", test)
        self.assertNotIn("timeout-minutes:", test)


if __name__ == "__main__":
    unittest.main()
