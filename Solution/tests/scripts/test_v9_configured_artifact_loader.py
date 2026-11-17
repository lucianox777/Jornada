"""Static contract checks for V9 file-only selection; does not certify productive execution."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
LOADER = ROOT / "src/Jornada.Linkage.Runner/V9ConfiguredArtifactLoader.cs"

class V9FileSelectionContractTests(unittest.TestCase):
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
