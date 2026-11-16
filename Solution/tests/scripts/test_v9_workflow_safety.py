"""Static safety gates for V9 corpus PREPARE integration.

These checks protect against accidentally widening the scientific 100k dispatch.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKFLOWS = ROOT / ".github" / "workflows"

class V9WorkflowSafetyTests(unittest.TestCase):
    def test_scientific_master_and_owner_gates_remain(self):
        text = (WORKFLOWS / "v9-v3-four-name-100k.yml").read_text()
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', text)
        self.assertIn('test "$GITHUB_EVENT_NAME" = "workflow_dispatch"', text)
        self.assertIn('test "$GITHUB_ACTOR" = "$GITHUB_REPOSITORY_OWNER"', text)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', text)
        self.assertIn("physical-100k:", text)
        self.assertIn("prepare-test-100k", text)

    def test_crossrunner_pr_probe_uses_only_two_rows_and_never_dispatches_train(self):
        flow = (WORKFLOWS / "v9-corpus-crossrunner-pr-smoke.yml").read_text()
        self.assertIn("probe-prepare:", flow)
        self.assertIn("probe-consumer:", flow)
        self.assertIn("needs: [probe-prepare]", flow)
        self.assertIn("GITHUB_RUN_ATTEMPT", flow)
        self.assertIn('test "$(git -C .. rev-parse HEAD)" = "$EXPECTED_SHA"', flow)
        self.assertIn("github.event.pull_request.head.sha", flow)
        self.assertIn("github.actor == github.repository_owner", flow)
        self.assertIn("v9_corpus_crossrunner_fixture.py", flow)
        self.assertIn("V9_CROSSRUNNER_2_ROWS_PROBE_ONLY", flow)
        self.assertIn("v9-corpus-handoff-verify.py", flow)
        self.assertIn("v9-corpus-crossrunner-probe-${{ github.run_id }}", flow)
        self.assertNotIn("physical-100k:", flow)
        self.assertNotIn("dotnet run", flow)
        self.assertNotIn("100000 --seed", flow)
        self.assertNotIn("secrets.", flow)
        self.assertNotIn("workflow_dispatch:", flow)

    def test_corpus_prepare_cannot_dispatch_scientific_train(self):
        text = (WORKFLOWS / "v9-corpus-prepare-opt-in.yml").read_text()
        self.assertIn("github.actor == github.repository_owner", text)
        self.assertIn('test "$GITHUB_SHA" = "$EXPECTED_SHA"', text)
        self.assertIn("refs/heads/feature/v9-persistent-synthetic-corpus-20261115", text)
        self.assertNotIn("workflow_call:", text)
        self.assertNotIn("physical-100k:", text)
        self.assertIn("v9-corpus-prepare.py", text)
        self.assertIn("mother_corruption_rate:", text)
        self.assertIn("$MOTHER_CORRUPTION_RATE", text)
        self.assertIn("V9_MATERNAL_RATE_EXPERIMENTAL_NOT_IMPLEMENTED", text)
        self.assertNotIn('maternal_args=(--mother-corruption-rate', text)
        self.assertNotIn('            --mother-corruption-rate "$MOTHER_CORRUPTION_RATE"', text)
        self.assertIn("train-handoff-probe:", text)
        self.assertIn("needs: [prepare-corpus]", text)
        self.assertIn("v9-corpus-handoff-verify.py", text)
        self.assertIn("download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093", text)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', text)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/feature/v9-persistent-synthetic-corpus-20261115"', text)
        self.assertIn("TRANSFER_PROBE_ONLY", text)
        self.assertIn("--expected-config .local/v9-handoff-received/expected-config.json", text)
        self.assertIn('config = json.loads(Path(".local/v9-handoff-received/expected-config.json").read_text())', text)
        self.assertIn('--expected-fingerprint "$expected_fingerprint"', text)
        self.assertIn("--generator-source src/Jornada.Linkage.SyntheticCorpus", text)
        self.assertIn("--birth-daily data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json", text)
        self.assertNotIn("v9-v3-four-name-100k.yml@", text)

if __name__ == "__main__":
    unittest.main()
