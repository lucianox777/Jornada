#!/usr/bin/env python3
"""Static no-30k regression for the exact manual TF workflow dispatch protocol."""
import unittest
from pathlib import Path

WORKFLOW = Path(__file__).resolve().parents[3] / ".github" / "workflows" / "v9-physical-seed42-probe.yml"


class TfWorkflowProtocolTests(unittest.TestCase):
    def test_manual_only_physical_job_uses_new_csharp_calibrator(self):
        script = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("--v9-adaptive-study", script)
        self.assertNotIn("--v9-calibrate", script)
        self.assertIn("inputs.mode == 'execute_30k'", script)
        self.assertIn("github.event_name == 'workflow_dispatch'", script)
        self.assertIn("--pinned-ibge", script)
        self.assertIn("pinned_ibge_json:", script)
        self.assertIn("expected_sha:", script)

    def test_preflight_is_not_any_route_to_30k(self):
        # Disposable SQL and corpus generation moved into shared scripts that
        # the workflow invokes; audit the workflow together with them.
        scripts = WORKFLOW.parents[2] / "Solution" / "scripts"
        script = WORKFLOW.read_text(encoding="utf-8") + "".join(
            (scripts / name).read_text(encoding="utf-8")
            for name in ("v9-tf-start-disposable-sql.sh", "v9-tf-prepare-pin.sh"))
        self.assertIn("bash Solution/scripts/v9-tf-start-disposable-sql.sh", script)
        self.assertIn('bash scripts/v9-tf-prepare-pin.sh "$PWD/.local/v9-tf-physical" pinned-ibge.json', script)
        self.assertIn('target_seed="${V9_TF_TARGET_SEED:-42}"', script)
        self.assertNotIn("V9_TF_TARGET_SEED", WORKFLOW.read_text(encoding="utf-8"))
        self.assertIn("inputs.mode == 'preflight'", script)
        self.assertIn("--mode preflight", script)
        self.assertIn("--mode execute_30k", script)
        self.assertIn("--expected-sha", script)
        self.assertIn("JornadaHarness", script)
        self.assertIn("--people 30000", script)
        self.assertIn("git-rechecked.json", script)
        self.assertIn("tf-shared-paired-diagnostics.json", script)
        self.assertIn("DEFERRED_NOT_CERTIFIED", script)
        self.assertIn(
            "if: ${{ always() && !(github.event_name == 'workflow_dispatch' && inputs.mode == 'preflight') }}",
            script,
        )  # Only preflight skips the physical certification check.
        self.assertIn(
            "if: github.event_name == 'workflow_dispatch' && inputs.mode == 'execute_30k' && needs.impact.outputs.run_physical_30k == 'true'",
            script,
        )  # Executing 30k still requires manual mode AND impacted physical gate.


if __name__ == "__main__":
    unittest.main()
