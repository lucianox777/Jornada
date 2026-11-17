"""Static contract checks for V9 file-only selection; does not certify productive execution."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
LOADER = ROOT / "src/Jornada.Linkage.Runner/V9ConfiguredArtifactLoader.cs"

class V9FileSelectionContractTests(unittest.TestCase):
    def test_v9_without_selection_never_falls_back_to_sql(self):
        program = (ROOT / "src/Jornada.Linkage.Runner/Program.cs").read_text(encoding="utf-8")
        self.assertIn('RUNNER_V9_SELECTION_MISSING', program)
        self.assertIn('SelectedModelVersion") == 9', program)
        self.assertLess(program.index("RUNNER_V9_SELECTION_MISSING"), program.index("GetModelByVersionAsync"))

    def test_v9_package_requires_explicit_version_nine(self):
        program = (ROOT / "src/Jornada.Linkage.Runner/Program.cs").read_text(encoding="utf-8")
        self.assertIn('RUNNER_V9_SELECTION_VERSION_MISMATCH', program)
        self.assertIn('GetValue<int?>("Linkage:SelectedModelVersion") != 9', program)
        self.assertIn('options.ModelVersion is int requestedV9Version && requestedV9Version != 9', program)
        self.assertLess(program.index('RUNNER_V9_SELECTION_VERSION_MISMATCH'),
                        program.index('V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(v9SelectionFile)'))

    def test_runner_startup_rejects_uncertified_v9_without_sql_fallback(self):
        program = (ROOT / "src/Jornada.Linkage.Runner/Program.cs").read_text(encoding="utf-8")
        self.assertIn('Linkage:V9SelectionFile', program)
        self.assertIn('V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(v9SelectionFile)', program)
        self.assertIn('RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED', program)
        self.assertLess(program.index('V9ConfiguredArtifactLoader.LoadAsync(v9SelectionFile)'),
                        program.index('GetModelByVersionAsync(configuredVersion.Value'))

    def test_file_snapshot_connects_to_shared_nonpublishing_cohort_runtime(self):
        loader = LOADER.read_text(encoding="utf-8")
        cohort = (ROOT / "src/Jornada.Linkage.Core/V9OperationalPinnedCohortRuntime.cs").read_text(encoding="utf-8")
        self.assertIn("FromVerifiedFileSnapshot(loaded.Snapshot)", loader)
        self.assertIn("V9OperationalSqlModelConverter.VerifyConversion(snapshot)", cohort)
        self.assertIn("new V9OperationalPinnedCohortRuntime(snapshot, verified.ModelId)", cohort)
        self.assertIn("V9FrozenSourceResolveReplay.ReplayTest(winner, test, completePhysicalSqlEdges)", cohort)

    def test_file_only_sha_pinned_no_sql_fallback(self):
        source = LOADER.read_text(encoding="utf-8")
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync(artifact, ct)", source)
        self.assertIn("V9ValidatedOperationalModelFile.FileName(selection.WinnerSha256)", source)
        self.assertIn("RUNNER_V9_ARTIFACT_SHA_MISMATCH", source)
        self.assertIn("UnmappedMemberHandling.Disallow", source)
        self.assertNotIn("GetModelByVersionAsync", source)
        self.assertNotIn("SqlConnection", source)
        self.assertNotIn("ReadPinnedAsync(connection", source)

if __name__ == "__main__":
    unittest.main()
