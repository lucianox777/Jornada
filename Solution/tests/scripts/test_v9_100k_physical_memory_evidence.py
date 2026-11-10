"""Canaries: physical SQL resource monitor never changes limits or logs secrets."""
import importlib.util
from pathlib import Path
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "scripts/v9-100k-physical-memory-evidence.py"
WORKFLOW = ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml"
spec = importlib.util.spec_from_file_location("v9_physical_memory", PATH)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class PhysicalMemoryEvidenceTests(unittest.TestCase):
    def test_docker_units_and_unreadable_stats(self):
        self.assertEqual(module.parse_docker_usage("1.5GiB / 15GiB"), 1572864)
        self.assertEqual(module.parse_docker_usage("1024MiB / 4GiB"), 1048576)
        self.assertIsNone(module.parse_docker_usage("unavailable"))

    def test_sql_oom_indicator_is_monotone_across_disappearance(self):
        samples = [False, True, None]
        seen = False
        inspected = 0
        with mock.patch.object(module, "docker_oom_killed", side_effect=samples):
            for _ in samples:
                reading = module.docker_oom_killed("v9-probe-sql-123-1")
                if reading is not None:
                    inspected += 1
                    seen = seen or reading
        self.assertTrue(seen)
        self.assertEqual(inspected, 2)
        source = PATH.read_text()
        self.assertIn("sql_oom_observed = sql_oom_observed or sql_oom", source)
        self.assertIn("sql_oom_observed = sql_oom_observed or final_sql_oom", source)

    def test_read_only_and_artifact_only(self):
        text = PATH.read_text()
        workflow = WORKFLOW.read_text()
        self.assertIn("V9_100K_PHYSICAL_MEMORY_EVIDENCE_V1", text)
        self.assertIn("docker\", \"stats", text)
        self.assertNotIn("docker\", \"update", text)
        self.assertNotIn("MSSQL_SA_PASSWORD", text)
        self.assertNotIn("ConnectionStrings__", text)
        self.assertIn("--sql-container", workflow)
        self.assertIn("physical-memory-evidence.json", workflow)
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("v9-100k-physical-memory-evidence.py", workflow.split("Start masked disposable SQL Server")[0])

if __name__ == "__main__":
    unittest.main()
