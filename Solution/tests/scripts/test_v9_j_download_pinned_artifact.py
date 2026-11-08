"""Offline regressions: GitHub API token must not follow cross-host artifact redirects."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import urllib.request


MODULE_PATH = Path(__file__).resolve().parents[2] / "scripts" / "v9-j-download-pinned-artifact.py"
spec = importlib.util.spec_from_file_location("v9_j_artifact_fetch", MODULE_PATH)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PinnedArtifactRedirectTests(unittest.TestCase):
    def setUp(self):
        self.request = urllib.request.Request(
            "https://api.github.com/repos/lucianox777/Jornada/actions/artifacts/123/zip",
            headers={"Authorization": "Bearer TEST_SECRET", "Accept": "application/json"},
        )
        self.handler = module.DropCrossOriginAuthorization()

    def test_cross_host_redirect_drops_token_but_preserves_accept(self):
        redirect = self.handler.redirect_request(
            self.request, None, 302, "Found", {},
            "https://results.example.net/frozen.zip?signature=pinned"
        )
        self.assertNotIn("authorization", {k.lower() for k in redirect.headers})
        self.assertNotIn("authorization", {k.lower() for k in redirect.unredirected_hdrs})
        self.assertEqual(redirect.get_header("Accept"), "application/json")

    def test_same_host_redirect_retains_api_token(self):
        redirect = self.handler.redirect_request(
            self.request, None, 302, "Found", {}, "https://api.github.com/repos/next"
        )
        self.assertEqual(redirect.get_header("Authorization"), "Bearer TEST_SECRET")

    def test_invalid_artifact_and_sha_rejected_before_network(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaisesRegex(ValueError, "artifact ID"):
                module.download("-1", "a" * 64, Path(tmp) / "a.zip")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                module.download("123", "unverified", Path(tmp) / "a.zip")


if __name__ == "__main__":
    unittest.main()
