"""Offline regression tests for the read-only generator isolation planner."""
import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/plan-tf-locked-generator-isolation.py"
spec = importlib.util.spec_from_file_location("isolation_planner", SCRIPT)
planner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(planner)


class IsolationPlannerTests(unittest.TestCase):
    def test_changed_files_and_no_repository_mutation(self):
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp)
            subprocess.run(["git", "init", "-q", str(repo)], check=True)
            subprocess.run(["git", "-C", str(repo), "config", "user.email", "ci@example.invalid"], check=True)
            subprocess.run(["git", "-C", str(repo), "config", "user.name", "CI"], check=True)
            path = repo / planner.DIRECTORY / "SyntheticCorpusGenerator.cs"
            path.parent.mkdir(parents=True)
            path.write_bytes(b"original")
            subprocess.run(["git", "-C", str(repo), "add", "."], check=True)
            subprocess.run(["git", "-C", str(repo), "commit", "-qm", "historical"], check=True)
            historical = planner.git(repo, "rev-parse", "HEAD").decode().strip()
            path.write_bytes(b"parametrized")
            subprocess.run(["git", "-C", str(repo), "add", "."], check=True)
            subprocess.run(["git", "-C", str(repo), "commit", "-qm", "current"], check=True)
            before = planner.git(repo, "status", "--porcelain")
            result = planner.plan(repo, historical)
            self.assertEqual(result["historical_file_count"], 1)
            self.assertEqual(result["current_file_count"], 1)
            self.assertEqual(result["changed_file_count"], 1)
            self.assertNotEqual(result["historical_tree_sha256"], result["current_tree_sha256"])
            self.assertFalse(result["migration_executed"])
            self.assertFalse(result["manifests_modified"])
            self.assertEqual(before, planner.git(repo, "status", "--porcelain"))
            self.assertEqual(path.read_bytes(), b"parametrized")


if __name__ == "__main__":
    unittest.main()
