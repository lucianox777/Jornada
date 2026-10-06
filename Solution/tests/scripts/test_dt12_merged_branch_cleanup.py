import importlib.util
import json
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "dt12-merged-branch-cleanup.py"
spec = importlib.util.spec_from_file_location("dt12_merged_branch_cleanup", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


# Pure regression coverage: no network or destructive operation is exercised here.
class Dt12MergedBranchCleanupTests(unittest.TestCase):
    def test_selects_only_exact_current_head_of_merged_master_pr(self):
        repository = "owner/repo"
        branches = {
            "merged": {"sha": "a" * 40, "protected": False},
            "moved": {"sha": "b" * 40, "protected": False},
            "protected": {"sha": "c" * 40, "protected": True},
            "open": {"sha": "d" * 40, "protected": False},
            "tagged": {"sha": "e" * 40, "protected": False},
        }
        pulls = [
            self._pr(10, "merged", "a" * 40, "2026-10-01T10:00:00Z"),
            self._pr(11, "moved", "f" * 40, "2026-10-01T10:00:00Z"),
            self._pr(12, "protected", "c" * 40, "2026-10-01T10:00:00Z"),
            self._pr(13, "open", "d" * 40, "2026-10-01T10:00:00Z"),
            self._pr(14, "tagged", "e" * 40, "2026-10-01T10:00:00Z"),
            self._pr(15, "future", "f" * 40, "2026-10-07T10:00:00Z"),
            self._pr(16, "other-base", "1" * 40, "2026-10-01T10:00:00Z", base="release"),
        ]
        selected = mod.select_merged_candidates(
            repository,
            branches,
            {"open"},
            {"e" * 40},
            pulls,
            datetime(2026, 10, 6, tzinfo=timezone.utc),
        )
        self.assertEqual([x["branch"] for x in selected], ["merged"])
        self.assertEqual(selected[0]["pr_number"], 10)

    def test_historical_branch_inventory_is_not_an_active_reference(self):
        self.assertIn("Solution/docs/DT12_Branches_20260926.csv", mod.EXCLUDED_REFERENCE_FILES)

    def test_authorization_is_explicit_and_timezone_aware(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "auth.json"
            path.write_text(json.dumps({
                "authorized": True,
                "phrase": mod.AUTH_PHRASE,
                "authorized_at": "2026-10-06T14:36:00Z",
                "authorized_by": "operator",
            }), encoding="utf-8")
            loaded = mod.load_authorization(path)
            self.assertTrue(loaded["authorized"])
            path.write_text(json.dumps({
                "authorized": True,
                "phrase": mod.AUTH_PHRASE,
                "authorized_at": "2026-10-06T14:36:00",
                "authorized_by": "operator",
            }), encoding="utf-8")
            with self.assertRaises(mod.CleanupError):
                mod.load_authorization(path)

    @staticmethod
    def _pr(number, branch, sha, merged_at, base="master"):
        return {
            "number": number,
            "merged_at": merged_at,
            "head": {
                "ref": branch,
                "sha": sha,
                "repo": {"full_name": "owner/repo"},
            },
            "base": {"ref": base},
        }


if __name__ == "__main__":
    unittest.main()
