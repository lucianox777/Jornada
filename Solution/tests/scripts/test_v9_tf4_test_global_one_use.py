"""TEST exposure is global per seed, not merely GitHub run_attempt == 1."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
LOCK = ROOT / "config/calibration/v9-tf4-100k-test-preparation-lock.json"


class TestGlobalOneUse(unittest.TestCase):
    def test_one_use_tag_atomically_reserved_before_decrypting_test(self):
        content = WORKFLOW.read_text()
        train = content.split("\n  physical-100k:\n", 1)[1].split("\n  validation-100k:\n", 1)[0]
        validation, test = content.split("\n  validation-100k:\n", 1)[1].split("\n  test-100k:\n", 1)
        self.assertIn("test-100k:", content)
        self.assertIn("contents: write", test)
        self.assertIn("actions: read", test)
        self.assertIn("GH_TOKEN: ${{ github.token }}", test)
        self.assertIn("github.actor == github.repository_owner", test)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', test)
        self.assertIn("refs/tags/v9-tf4-test-seed-20261113001-exposed", test)
        self.assertIn('gh api "repos/$GITHUB_REPOSITORY/git/refs" --method POST', test)
        self.assertIn('-f ref="$exposure_ref" -f sha="$GITHUB_SHA"', test)
        self.assertIn("V9_TEST_EXPOSURE_ALREADY_RESERVED_OR_GITHUB_TAG_DENIED", test)
        self.assertIn('assert expected==actual', test)
        self.assertIn('hashlib.sha256(cert.read_bytes()).hexdigest()==r["certificateSha256"]', test)
        self.assertLess(test.index("test \"$GITHUB_RUN_ATTEMPT\""),
                        test.index("gh api"))
        self.assertLess(test.index("gh api"), test.index("unseal \\\n"))
        self.assertLess(test.index('assert expected==actual'), test.index("gh api"))
        self.assertNotIn("contents: write", train)
        self.assertNotIn("contents: write", validation)
        # TRAIN already uses a read-only GH_TOKEN to retrieve a pinned historical
        # artifact; its presence is not TEST access or repository write permission.
        self.assertIn("GH_TOKEN:", train)
        self.assertNotIn("GH_TOKEN", validation)
        self.assertNotIn("TEST_PRIVATE_KEY_B64", train)
        self.assertNotIn("TEST_PRIVATE_KEY_B64", validation)
        self.assertIn('"testSeed": 20261113001', LOCK.read_text())
        self.assertIn("v9-tf4-test-one-winner-", validation)
        self.assertNotIn("finalist-validation.json", test)
        self.assertNotIn("selection.json", test)
        self.assertNotIn("plan.json", test)

    def test_only_test_job_has_write_permission_not_full_workflow(self):
        full = WORKFLOW.read_text()
        header = full.split("\njobs:\n", 1)[0]
        prep = full.split("\n  prepare-test-100k:\n", 1)[1].split("\n  physical-100k:\n", 1)[0]
        self.assertIn("contents: read", header)
        self.assertNotIn("contents: write", header)
        self.assertNotIn("contents: write", prep)
        self.assertNotIn("TEST_PRIVATE_KEY_B64", prep)


if __name__ == "__main__":
    unittest.main()
