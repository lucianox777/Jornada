"""Offline contract tests for the read-only campaign #38 artifact preservation tool."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/v9-preserve-campaign38-artifacts.py"
SPEC = importlib.util.spec_from_file_location("v9_preserve_campaign38", SCRIPT)
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class Campaign38PreservationTests(unittest.TestCase):
    def test_inventory_has_exact_campaign_provenance_and_14_unique_artifacts(self):
        self.assertEqual(module.RUN_ID, 44970012048)
        self.assertEqual(module.RUN_ATTEMPT, 1)
        self.assertEqual(module.CODE_SHA, "30c71a0db1bdeac6ba536db5e428ade4d5c70c9c")
        self.assertEqual(module.WINNER_SHA, "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee")
        self.assertEqual(len(module.ARTIFACTS), 14)
        self.assertEqual(len(set(module.ARTIFACTS)), 14)
        for artifact_id, (role, digest) in module.ARTIFACTS.items():
            self.assertGreater(artifact_id, 0)
            self.assertTrue(role)
            self.assertEqual(len(digest), 64)
            self.assertTrue(all(c in "0123456789abcdef" for c in digest))

    def test_default_dry_run_never_downloads_or_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            output = io.StringIO()
            with patch.object(module, "api_json", side_effect=AssertionError("network forbidden")), \
                 patch.object(module, "download_verified", side_effect=AssertionError("network forbidden")), \
                 contextlib.redirect_stdout(output), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(module.main(["--destination", directory]), 0)
            payload = json.loads(output.getvalue())
            self.assertEqual(payload["artifact_count"], 14)
            self.assertEqual(list(Path(directory).iterdir()), [])

    def test_execute_rejects_missing_token_before_network(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.dict("os.environ", {"GH_TOKEN": "", "GITHUB_TOKEN": ""}), \
                 patch.object(module, "api_json", side_effect=AssertionError("network forbidden")), \
                 self.assertRaises(SystemExit):
                module.main(["--destination", directory, "--execute"])

    def test_execute_rejects_nonempty_destination_before_network(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory) / "private"
            folder.mkdir(mode=0o700)
            (folder / "previous.zip").write_bytes(b"not replaceable")
            with patch.dict("os.environ", {"GH_TOKEN": "test-token"}), \
                 patch.object(module, "api_json", side_effect=AssertionError("network forbidden")), \
                 self.assertRaises(SystemExit):
                module.main(["--destination", str(folder), "--execute"])


if __name__ == "__main__":
    unittest.main()
