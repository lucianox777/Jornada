"""Offline regression for resource-only V9 100k dynamic SQL admission (no physics)."""
import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
script = ROOT / "scripts/v9-100k-execution-resource-budget.py"
spec = importlib.util.spec_from_file_location("v9_100k_runtime_budget", script)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
G = mod.GIB
SHA = "a" * 40


def sample(available=14 * G, disk=10 * G, total=15 * G):
    return {"hostRamBytes": total, "hostRamAvailableBytes": available,
            "effectiveRamAvailableBytes": available, "diskFreeBytes": disk,
            "cgroupLimitBytes": None, "cgroupUsedBytes": None}


class Adaptive100kResourceBudgetTests(unittest.TestCase):
    def test_standard_runner_frees_sql_memory_for_csharp(self):
        result = mod.budget("plan-sql", sample(), SHA)
        self.assertEqual(result["status"], "PASS_ADMISSION_ONLY_NOT_FIT_PROOF")
        self.assertEqual(result["sqlMemoryLimitMb"], 2048)
        self.assertEqual(result["sqlContainerMemoryMb"], 3072)
        self.assertFalse(result["fitted100k"])
        self.assertFalse(result["historical100kGeneratorResourceProfileReused"])
        self.assertFalse(result["testUsed"])
        self.assertFalse(result["goldUsed"])
        self.assertFalse(result["activated"])

    def test_larger_runner_budget_increases_only_with_measured_headroom(self):
        for available, selected in ((18 * G, 3072), (25 * G, 3072),
                                    (26 * G, 4096)):
            with self.subTest(available=available):
                result = mod.budget("plan-sql", sample(available=available, total=30 * G), SHA)
                self.assertEqual(result["sqlMemoryLimitMb"], selected)
                self.assertEqual(result["sqlContainerMemoryMb"], selected + 1024)
        # Nominal RAM does not license a 4GiB SQL budget when available RAM is low.
        self.assertEqual(mod.budget("plan-sql", sample(total=30 * G), SHA)
                         ["sqlMemoryLimitMb"], 2048)

    def test_fail_closed_without_ram_disk_or_worker_reserve(self):
        for s in (sample(total=11 * G), sample(disk=5 * G),
                  sample(available=9 * G), sample(available=1 * G)):
            self.assertEqual(mod.budget("plan-sql", s, SHA)["status"],
                             "FAIL_CLOSED_INSUFFICIENT_RESOURCES")

    def test_fit_reuses_only_same_sha_runtime_receipt_not_old_generator(self):
        before = mod.budget("plan-sql", sample(), SHA)
        after = mod.budget("before-fit", sample(available=8 * G), SHA, before)
        self.assertEqual(after["status"], "PASS_ADMISSION_ONLY_NOT_FIT_PROOF")
        self.assertEqual(after["sqlMemoryLimitMb"], 2048)
        self.assertEqual(mod.budget("before-fit", sample(available=5 * G), SHA, before)
                         ["status"], "FAIL_CLOSED_INSUFFICIENT_RESOURCES")
        self.assertEqual(mod.budget("before-fit", sample(disk=2 * G), SHA, before)
                         ["status"], "FAIL_CLOSED_INSUFFICIENT_RESOURCES")
        with self.assertRaisesRegex(ValueError, "same-SHA"):
            mod.budget("before-fit", sample(), "b" * 40, before)
        with self.assertRaisesRegex(ValueError, "same-SHA"):
            mod.budget("before-fit", sample(), SHA, {"schema": "generator-100k"})
        with self.assertRaisesRegex(ValueError, "SQL memory receipt"):
            tampered = dict(before, sqlContainerMemoryMb=2048)
            mod.budget("before-fit", sample(), SHA, tampered)

    def test_rejects_corrupt_samples_and_unverified_sha(self):
        with self.assertRaises(ValueError):
            mod.budget("bad", sample(), SHA)
        with self.assertRaises(ValueError):
            mod.budget("plan-sql", sample(), "short")
        with self.assertRaises(ValueError):
            mod.budget("plan-sql", dict(sample(), diskFreeBytes=-1), SHA)

    def test_cgroup_remaining_is_used_if_finite(self):
        with tempfile.TemporaryDirectory() as d:
            cgroup = Path(d)
            (cgroup / "memory.max").write_text(str(16 * G))
            (cgroup / "memory.current").write_text(str(10 * G))
            self.assertEqual(mod.read_bytes(cgroup / "memory.max"), 16 * G)
            self.assertEqual(mod.read_bytes(cgroup / "memory.current"), 10 * G)
            (cgroup / "memory.max").write_text("max")
            self.assertIsNone(mod.read_bytes(cgroup / "memory.max"))
            (cgroup / "memory.current").write_text("not_a_number")
            with self.assertRaisesRegex(ValueError, "unreadable"):
                mod.read_bytes(cgroup / "memory.current")

    def test_opt_out_of_python_sql_sizing_is_explicit_and_still_guarded(self):
        workflow = (ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml").read_text()
        script = ROOT / "scripts/v9-100k-sql-budget-optout-guard.sh"
        guard = script.read_text()
        self.assertIn("disable_python_sql_budget:", workflow)
        self.assertIn("default: true", workflow)
        self.assertIn("V9_100K_DISABLE_PYTHON_SQL_BUDGET:", workflow)
        self.assertEqual(workflow.count("if: ${{ inputs.disable_python_sql_budget != true }}"), 1)
        self.assertEqual(workflow.count("if: ${{ inputs.disable_python_sql_budget == true }}"), 1)
        sql_script = (ROOT / "scripts/v9-tf-start-disposable-sql.sh").read_text()
        self.assertIn("v9-100k-sql-budget-optout-guard.sh", sql_script)
        self.assertIn("before-sql \"$capacity_receipt\"", sql_script)
        self.assertIn("v9-100k-execution-resource-budget.py", sql_script)
        self.assertIn("--stage plan-sql", sql_script)
        self.assertLess(sql_script.index("v9-100k-execution-resource-budget.py"),
                        sql_script.index("docker run --detach"))
        self.assertNotIn("Recalculate LIVE resource budget", workflow)
        self.assertNotIn("Guard native SQL defaults when Python SQL budget is disabled", workflow)
        self.assertIn("v9-100k-sql-budget-optout-guard.sh before-fit", workflow)
        self.assertIn("Start masked disposable SQL Server", workflow)
        self.assertLess(workflow.index("Guard memory and disk in native SQL mode"),
                        workflow.index("Run physical V9 CSharp on all 24"))
        self.assertIn('[[ -n "${V9_SQL_MEMORY_LIMIT_MB:-}"', guard)
        self.assertIn('-n "${V9_SQL_CONTAINER_MEMORY_MB:-}"', guard)
        self.assertIn("memory.max", guard)
        self.assertIn("MemAvailable", guard)
        self.assertIn("df -Pk", guard)
        self.assertIn("PASS_ADMISSION_ONLY_NOT_FIT_PROOF", guard)
        self.assertIn("FAIL_CLOSED_INSUFFICIENT_RESOURCES", guard)
        self.assertIn('"testUsed": false', guard)
        self.assertIn('"activated": false', guard)
        proc = subprocess.run(["bash", "-n", str(script)], capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, proc.stderr)

    def test_exact_workflow_separation_and_no_safety_bypass(self):
        workflow = (ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml").read_text()
        self.assertIn("runs-on: ubuntu-24.04", workflow)
        self.assertIn("github.actor == github.repository_owner", workflow)
        self.assertIn("security_preflight_approved == true", workflow)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', workflow)
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertNotIn("  pull_request:", workflow)
        self.assertNotIn("V9_SQL_MEMORY_LIMIT_MB: \"4096\"", workflow)
        self.assertNotIn("v9-standard-100k-capacity.py", workflow)
        gen = workflow.index("Generate known-seed 100k clean TRAIN")
        sql = workflow.index("Start masked disposable SQL Server")
        shell = (ROOT / "scripts/v9-tf-start-disposable-sql.sh").read_text()
        self.assertIn("v9-100k-execution-resource-budget.py", shell)
        self.assertIn("--stage plan-sql", shell)
        pin = workflow.index("Reconstruct historical 30k TF reference")
        guard = workflow.index("Recheck physical RAM/disk")
        fit = workflow.index("Run physical V9 CSharp on all 24")
        self.assertLess(gen, sql)
        self.assertLess(sql, pin)
        self.assertLess(pin, guard)
        self.assertLess(guard, fit)
        self.assertIn('--github-env "$GITHUB_ENV"', shell)
        self.assertIn("--previous \"$root/capacity-before-sql.json\"", workflow)
        self.assertIn("--v9-calibrate", workflow)
        self.assertIn("results/calibration-resource-budget.json", workflow)


if __name__ == "__main__":
    unittest.main()
