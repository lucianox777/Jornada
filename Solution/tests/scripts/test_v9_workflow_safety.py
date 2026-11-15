"""Static safety gates for V9 corpus PREPARE integration.

These checks protect against accidentally widening the scientific 100k dispatch.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKFLOWS = ROOT.parent / ".github" / "workflows"

class V9WorkflowSafetyTests(unittest.TestCase):
    def test_scientific_master_and_owner_gates_remain(self):
        text = (WORKFLOWS / "v9-v3-four-name-100k.yml").read_text()
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', text)
        self.assertIn('test "$GITHUB_EVENT_NAME" = "workflow_dispatch"', text)
        self.assertIn('test "$GITHUB_ACTOR" = "$GITHUB_REPOSITORY_OWNER"', text)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', text)
        self.assertIn("physical-100k:", text)
        self.assertIn("prepare-test-100k", text)

    def test_corpus_prepare_cannot_dispatch_scientific_train(self):
        text = (WORKFLOWS / "v9-corpus-prepare-opt-in.yml").read_text()
        self.assertIn("github.actor == github.repository_owner", text)
        self.assertIn('test "$GITHUB_SHA" = "$EXPECTED_SHA"', text)
        self.assertIn("refs/heads/feature/v9-persistent-synthetic-corpus-20261115", text)
        self.assertNotIn("workflow_call:", text)
        self.assertNotIn("physical-100k:", text)
        self.assertIn("v9-corpus-prepare.py", text)

if __name__ == "__main__":
    unittest.main()
