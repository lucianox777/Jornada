"""Regression guards for independent TEST's one-use seed binding."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
READER = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCorpusReader.cs"
WORKFLOW = ROOT.parent / ".github/workflows/v9-standard-linear-100k.yml"

class TestIndependentTestSeedBinding(unittest.TestCase):
    def test_reader_rejects_missing_or_mismatched_reserved_seed(self):
        source = READER.read_text()
        self.assertIn('Environment.GetEnvironmentVariable("JORNADA_V9_TEST_RESERVED_SEED")', source)
        self.assertIn('!ulong.TryParse(expectedSeedText', source)
        self.assertIn('seed != expectedSeed', source)
        self.assertNotIn('const ulong Seed = 20261113001UL;', source)
        self.assertIn('outputs["observacoes.csv"].ToLowerInvariant(), seed,', source)

    def test_receipt_and_manifest_bound_before_test_exposure(self):
        workflow = WORKFLOW.read_text()
        job = workflow.split("\n  test-100k:\n", 1)[1]
        self.assertLess(job.index("Verify reserved TEST seed receipt"), job.index("Apply one frozen winner to TEST"))
        self.assertIn('receipt["seed"] == seed == manifest["seed"]', job)
        self.assertIn('receipt["codeSha"] == os.environ["GITHUB_SHA"]', job)
        self.assertIn('receipt["exposureRef"] == "refs/tags/v9-tf4-test-seed-v2-"+str(seed)', job)
        self.assertIn('JORNADA_V9_TEST_RESERVED_SEED: ${{ steps.test_seed.outputs.test_seed }}', job)

if __name__ == "__main__":
    unittest.main()
