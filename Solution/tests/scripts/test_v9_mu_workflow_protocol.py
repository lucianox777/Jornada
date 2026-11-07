"""Static fail-closed guard for the seed42-only technical M/U physical workflow."""
from pathlib import Path
import unittest

WORKFLOW = Path(__file__).resolve().parents[3] / ".github/workflows/v9-mu-seed42-technical-regression.yml"


class TechnicalMuWorkflowProtocolTests(unittest.TestCase):
    def test_only_known_seed42_master_push_and_no_new_scientific_campaign(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches:\n      - master", text)
        self.assertNotIn("  pull_request:\n", text)
        self.assertNotIn("pull_request_target:", text)
        self.assertNotIn("workflow_dispatch:", text)
        self.assertIn("github.event_name == 'push' && github.ref == 'refs/heads/master'", text)
        self.assertIn("ref: ${{ github.sha }}", text)
        self.assertIn('test "$(git rev-parse HEAD)" = "$MU_SHA"', text)
        self.assertIn("--mode auto_30k", text)
        self.assertIn("v9-tf-ref-provenance-gate.py", text)
        self.assertIn("data/reference/v9-synthetic-seed42-30000/manifest.json", text)
        self.assertIn("v9-mu-historical-plan.py", text)
        self.assertIn("--v9-calibrate", text)
        self.assertIn("mu-statistical-analysis.json", text)
        self.assertIn("results/interrupted.json", text)
        self.assertIn('"testUsed": False, "certified": False, "activated": False', text)
        self.assertNotIn("V9_TF_TARGET_SEED=43", text)
        self.assertNotIn("--v9-validate-finalists", text)
        self.assertNotIn("DT15_ACTIVATE", text)
        self.assertNotIn("persist-credentials: true", text)
        self.assertNotIn("contents: write", text)
        self.assertIn("docker rm -f", text)
        self.assertNotIn("people-30k/observacoes.csv\n", text.split("Preserve technical report, SHA pins")[1])
        self.assertNotIn("people-30k/gabarito.json\n", text.split("Preserve technical report, SHA pins")[1])


if __name__ == "__main__":
    unittest.main()
