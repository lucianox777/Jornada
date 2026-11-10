"""Static safety contract for the isolated serial V9-Lite experiment."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-lite-serial-100k-experiment.yml"
CLI = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs"
ENTRY = ROOT / "src/Jornada.Linkage.Parameters.Worker/Program.cs"
ORIGINAL = ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml"


class LiteExperimentContractTests(unittest.TestCase):
    def test_manual_security_and_sha_gates_preserved(self):
        w = WORKFLOW.read_text()
        for required in (
            "workflow_dispatch:", "security_preflight_approved",
            "github.actor == github.repository_owner", "EXACT_SHA",
            'test "$GITHUB_REF" = "refs/heads/master"',
            'test "$GITHUB_EVENT_NAME" = "workflow_dispatch"',
            "persist-credentials: false",
            "v9-tf-start-disposable-sql.sh",
            "v9-scale-100k-technical-audit.py",
            "interrupted.json",
            "v9-lite-serial-100k-",
        ):
            self.assertIn(required, w)
        self.assertNotIn("  pull_request:", w)
        self.assertNotIn("  push:", w)

    def test_resource_budget_python_not_executed(self):
        w = WORKFLOW.read_text()
        self.assertNotIn("python3 scripts/v9-100k-execution-resource-budget.py", w)
        self.assertIn('V9_SQL_MEMORY_LIMIT_MB: "2048"', w)
        self.assertIn('V9_SQL_CONTAINER_MEMORY_MB: "3072"', w)
        self.assertIn("MemAvailable:", w)
        self.assertIn("test \"$disk_kib\" -ge 6291456", w)
        # Scientific plan preprocessing and independent audit Python are allowed.
        self.assertIn("v9-scale-100k-technical-plan.py", w)
        self.assertIn("--v9-lite-benchmark", w)

    def test_explicit_experiment_does_not_change_existing_entrypoints(self):
        cli = CLI.read_text()
        entry = ENTRY.read_text()
        self.assertIn('"--v9-lite-benchmark"', cli)
        self.assertIn('"--v9-lite-benchmark"', entry)
        self.assertIn('!= "EXPLICIT_SYNTHETIC_TRAIN_ONLY"', cli)
        self.assertIn('"FIXED_SINGLE_WORKER"', cli)
        self.assertIn('V9CalibrationRuntimeScheduler? resourceScheduler = lite ? null', cli)
        self.assertIn('executionMaximumParallelRuleSets: resources.EffectiveParallelism', cli)
        self.assertIn('resourceScheduler?.Receipts ?? Array.Empty<V9CalibrationRuntimeSchedulingReceipt>()', cli)
        self.assertIn('"--v9-calibrate"', cli)
        # Preexisting governed 100k workflow remains available for comparison.
        self.assertIn("v9-100k-execution-resource-budget.py", ORIGINAL.read_text())

    def test_1k_pr_regression_uses_primary_v9_auto_vs_lite_serial(self):
        w = (ROOT.parent / ".github/workflows/v9-lite-serial-1k-physical-parity.yml").read_text()
        for required in (
            "MAIN_BENCHMARK_REQUIRES_V9_AUTO_PLAN",
            "MAIN_ARCHITECTURES_NOT_V9_AUTO_VS_LITE_SERIAL",
            "V9 GOVERNED AUTO and Lite SERIAL",
            "SCIENTIFIC_EVALUATION_ROWS_DIFFER",
            "SCIENTIFIC_FIT_SHA_MISMATCH",
            "--v9-calibrate", "--v9-lite-benchmark",
        ):
            self.assertIn(required, w)
        self.assertNotIn("plan-auto.json", w)
        self.assertNotIn('source["Calibration"]["MaximumParallelRuleSets"]=1', w)

    def test_30k_workflow_compares_v9_auto_against_lite_serial(self):
        w = (ROOT.parent / ".github/workflows/v9-lite-serial-30k-matched-benchmark.yml").read_text()
        for needle in (
            "workflow_dispatch:", "security_preflight_approved",
            "github.actor == github.repository_owner",
            'test "$EXPECTED_SHA" = "$GITHUB_SHA"',
            'test "$GITHUB_REF" = refs/heads/master',
            'V9_SQL_MEMORY_LIMIT_MB: "2048"',
            'V9_SQL_CONTAINER_MEMORY_MB: "3072"',
            "V9_NORMAL_AUTO_PARALLELISM_REQUIRED",
            "WRONG_ARCHITECTURE_OR_NONISOLATED_LITE",
            "V9_BENCHMARK_CPU_V1",
            "userCpuSeconds",
            "systemCpuSeconds",
            "logicalOutputDirectoryKiB",
            "SCIENTIFIC_FIT_SHA_MISMATCH",
            "V9 governed AUTO then Lite serial",
            "--v9-calibrate", "--v9-lite-benchmark",
            "EXPECTED_ALL_160_EVALUATIONS",
            "SCIENTIFIC_EVALUATION_ROWS_DIFFER",
            "SCIENTIFIC_FIT_SHA_MISMATCH",
            "testUsed", "activated",
        ):
            self.assertIn(needle, w)
        self.assertNotIn("  push:", w)
        self.assertNotIn("  pull_request:", w)
        self.assertNotIn("python3 scripts/v9-100k-execution-resource-budget.py", w)
        self.assertNotIn("plan-pre-serial.json", w)
        self.assertNotIn("plan['Calibration']['MaximumParallelRuleSets']=1", w)
        self.assertIn('"$cli" "$root/plan.json"', w)

    def test_no_scientific_plan_rewrite_or_activation(self):
        w = WORKFLOW.read_text()
        self.assertIn("--people 100000 --seed 42", w)
        self.assertIn("--artifact-id 13654549483", w)
        self.assertNotIn("ACTIVATE_MODEL", w)
        self.assertNotIn("publish=true", w)
        self.assertIn("JORNADA_V9_LITE_EXPERIMENT: EXPLICIT_SYNTHETIC_TRAIN_ONLY", w)


if __name__ == "__main__":
    unittest.main()
