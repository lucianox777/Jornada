import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "dt12-branch-cleanup.py"
spec = importlib.util.spec_from_file_location("dt12_cleanup", SCRIPT)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class Dt12BranchCleanupTests(unittest.TestCase):
    def test_blockers_detect_changed_sha_open_pr_tag_protection_and_reference(self):
        candidates = [
            {"branch": "a", "sha": "1" * 40},
            {"branch": "b", "sha": "2" * 40},
            {"branch": "c", "sha": "3" * 40},
            {"branch": "d", "sha": "4" * 40},
        ]
        branches = {
            "a": {"sha": "9" * 40, "protected": False},
            "b": {"sha": "2" * 40, "protected": True},
            "c": {"sha": "3" * 40, "protected": False},
            "d": {"sha": "4" * 40, "protected": False},
        }
        blockers = m.compute_blockers(
            candidates,
            branches,
            {"c"},
            {"4" * 40: "v-test"},
            {"d": ["README.md"]},
        )
        self.assertIn("sha_changed:" + "9" * 40, blockers["a"])
        self.assertIn("protected", blockers["b"])
        self.assertIn("open_pr", blockers["c"])
        self.assertIn("tag_head:v-test", blockers["d"])
        self.assertIn("active_reference:README.md", blockers["d"])

    def test_zero_blockers_for_stable_candidates(self):
        candidates = [{"branch": "a", "sha": "1" * 40}]
        blockers = m.compute_blockers(
            candidates,
            {"a": {"sha": "1" * 40, "protected": False}},
            set(),
            {},
            {},
        )
        self.assertEqual({}, blockers)

    def test_authorization_requires_exact_contract(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "auth.json"
            p.write_text(json.dumps({
                "authorized": True,
                "phrase": m.AUTH_PHRASE,
                "candidate_sha256": "abc",
                "candidate_count": 170,
                "authorized_at": "2026-10-01T20:00:00Z",
                "authorized_by": "lucianox777",
            }), encoding="utf-8")
            data = m.load_authorization(p, "abc", 170)
            self.assertTrue(data["authorized"])

    def test_authorization_rejects_wrong_phrase(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "auth.json"
            p.write_text(json.dumps({
                "authorized": True,
                "phrase": "NO",
                "candidate_sha256": "abc",
                "candidate_count": 170,
                "authorized_at": "2026-10-01T20:00:00Z",
                "authorized_by": "lucianox777",
            }), encoding="utf-8")
            with self.assertRaises(m.CleanupError):
                m.load_authorization(p, "abc", 170)

    def test_ref_endpoint_preserves_branch_path(self):
        self.assertEqual(
            "git/ref/heads/feat/example/path",
            m.ref_endpoint("feat/example/path"),
        )


if __name__ == "__main__":
    unittest.main()
