"""Canaries: physical SQL resource monitor never changes limits or logs secrets."""
import importlib.util
from pathlib import Path
import unittest
import tempfile
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

    def test_sql_container_cgroup_oom_counts_and_peak(self):
        sample = "low 2\nhigh 0\nmax 3\noom 4\noom_kill 1\nPEAK_KIB:10485760\n"
        self.assertEqual(module.parse_sql_cgroup_snapshot(sample),
                         {"oom": 4, "oom_kill": 1, "peakKiB": 10240})
        self.assertIsNone(module.parse_sql_cgroup_snapshot("cgroup unavailable"))
        self.assertIsNone(module.parse_sql_cgroup_snapshot("oom 3\nPEAK_KIB:10"))
        self.assertIsNone(module.parse_sql_cgroup_snapshot("oom secret\noom_kill 0\nPEAK_KIB:10"))
        source = PATH.read_text()
        self.assertIn("sqlCgroupV2OomKillDeltaObserved", source)
        self.assertIn("sqlCgroupV2MemoryPeakKiB", source)
        self.assertIn("runnerCgroupV2Resolved", source)
        self.assertIn("sqlCgroupMemoryMaxKiB", source)
        self.assertIn("sql_cgroup_oom_kill_delta_max or 0", source)

    def test_cgroup_mount_root_and_process_membership(self):
        mountinfo = (
            "31 20 0:31 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n"
        )
        self.assertEqual(
            module.resolve_runner_cgroup("0::/runner/task\n", mountinfo),
            Path("/sys/fs/cgroup/runner/task"))
        self.assertIsNone(module.resolve_runner_cgroup(
            "1:memory:/other\n", mountinfo))
        nested = "31 20 0:31 /runner /sys/fs/cgroup rw - cgroup2 cgroup rw\n"
        self.assertEqual(module.resolve_runner_cgroup(
            "0::/runner/task\n", nested), Path("/sys/fs/cgroup/task"))
        self.assertIsNone(module.resolve_runner_cgroup(
            "0::/other/task\n", nested))

    def test_oom_events_unknown_and_valid_no_false_zero(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            self.assertEqual(module.oom_events(root), {"oom": None, "oom_kill": None})
            (root / "memory.events").write_text("oom 4\noom_kill 2\n")
            self.assertEqual(module.oom_events(root), {"oom": 4, "oom_kill": 2})
            (root / "memory.events").write_text("oom bad\n")
            self.assertEqual(module.oom_events(root), {"oom": None, "oom_kill": None})

    def test_ppid_tree_includes_descendants_independent_of_thread(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for pid, ppid, rss in [(100, 1, 100), (200, 100, 200),
                                   (300, 200, 300), (999, 1, 999)]:
                directory = root / str(pid)
                directory.mkdir()
                (directory / "stat").write_text(
                    f"{pid} (worker with (brackets)) S {ppid} 1 1 0\n")
                (directory / "status").write_text(f"VmRSS:\t{rss} kB\n")
            self.assertEqual(module.parse_proc_stat_ppid(
                "123 (complex (name)) S 42 2 3"), 42)
            self.assertEqual(module.process_tree_rss_kib(100, root), 600)
            self.assertIsNone(module.process_tree_rss_kib(400, root))

    def test_sql_explicit_memory_limits_are_numeric_only(self):
        class Completed:
            def __init__(self, stdout, returncode=0):
                self.stdout, self.returncode = stdout, returncode
        with mock.patch.object(module.subprocess, "run",
                side_effect=[Completed("memory.memorylimitmb : 4096"),
                             Completed("3072"), Completed("5368709120"),
                             Completed("4294967296")]):
            values = module.sql_memory_limits("v9-probe-sql-123-1")
        self.assertEqual(values, {
            "sqlMemoryMemorylimitMb": 4096,
            "sqlEnvironmentMemoryLimitMb": 3072,
            "sqlDockerHardLimitKiB": 5242880,
            "sqlCgroupMemoryMaxKiB": 4194304})
        with mock.patch.object(module.subprocess, "run",
                side_effect=[Completed("", 1), Completed("unset"), Completed("0"),
                             Completed("max")]):
            defaults = module.sql_memory_limits("v9-probe-sql-123-1")
        self.assertTrue(all(value is None for value in defaults.values()))
        self.assertIsNone(module.parse_positive_limit("max"))
        self.assertIsNone(module.parse_positive_limit("not-a-number"))

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
