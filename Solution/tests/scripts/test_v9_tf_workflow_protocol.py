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
        script = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("inputs.mode == 'preflight'", script)
        self.assertIn("--mode preflight", script)
        self.assertIn("--mode execute_30k", script)
        self.assertIn("--expected-sha", script)
        self.assertIn("JornadaHarness", script)
        self.assertIn("--people 30000", script)
        self.assertIn("git-rechecked.json", script)
        self.assertIn("tf-shared-paired-diagnostics.json", script)
        self.assertIn("DEFERRED_NOT_CERTIFIED", script)


if __name__ == "__main__":
    unittest.main()
