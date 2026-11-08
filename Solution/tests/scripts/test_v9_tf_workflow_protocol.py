#!/usr/bin/env python3
"""Static contract for push-only technical 30k, without manual execution."""
import unittest
from pathlib import Path

WORKFLOW = Path(__file__).resolve().parents[3] / ".github" / "workflows" / "v9-physical-seed42-probe.yml"


class TfWorkflowProtocolTests(unittest.TestCase):
    def test_automatic_master_push_only_physical_job_uses_single_csharp_calibrator(self):
        script = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("--v9-adaptive-study", script)
        self.assertNotIn("--v9-calibrate", script)
        self.assertNotIn("workflow_dispatch", script)
        # PO 07/11: quick checks on PR; physical 30k only on master push.
        self.assertIn("  push:\n    branches:\n      - master", script)
        self.assertNotIn("  pull_request:\n", script)
        self.assertIn("github.event_name == 'push'", script)
        self.assertNotIn("  workflow_dispatch:", script)
        self.assertIn("ref: ${{ github.sha }}", script)
        self.assertIn("--mode auto_30k", script)
        self.assertIn("SOURCE_REPO", script)
        self.assertIn('test "$SOURCE_REPO" = "$GITHUB_REPOSITORY"', script)
        self.assertIn("--pinned-ibge", script)
        self.assertNotIn("pinned_ibge_json:", script)
        self.assertNotIn("inputs.expected_sha", script)

    def test_master_push_probes_30k_without_manual_preflight(self):
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
        self.assertNotIn("inputs.mode", script)
        self.assertNotIn("--mode preflight", script)
        self.assertNotIn("--mode execute_30k", script)
        self.assertIn("--mode auto_30k", script)
        self.assertIn("--corpus-root", script)
        self.assertIn("v9-tf-ref-provenance-gate.py", script)
        self.assertIn("--expected-sha", script)
        self.assertIn("JornadaHarness", script)
        self.assertIn("--people 30000", script)
        self.assertIn("git-rechecked.json", script)
        self.assertIn("tf-shared-paired-diagnostics.json", script)
        self.assertIn("DEFERRED_NOT_CERTIFIED", script)
        self.assertIn(
            "if: ${{ always() }}",
            script,
        )  # Classifier decides mandatory physical execution on every push.
        self.assertIn(
            "if: needs.impact.outputs.run_physical_30k == 'true' && github.event_name == 'push'",
            script,
        )  # Impacted master push runs real physical 30k, no manual fallback.
        self.assertIn('"$PHYSICAL_RESULT" == "success"', script)
        self.assertNotIn("python3 Solution/scripts/v9_manual_probe_evidence.py", script)


if __name__ == "__main__":
    unittest.main()
