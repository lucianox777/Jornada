"""Prevent malformed Bash continuations in the opt-in PREPARE consumer."""
from pathlib import Path
import unittest

WORKFLOW = Path(__file__).resolve().parents[3] / ".github" / "workflows" / "v9-corpus-prepare-opt-in.yml"


class CorpusOptInShellSafetyTests(unittest.TestCase):
    def test_consumer_receipt_command_has_single_backslash_continuations(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        consumer = text.split("      - name: Verify exact config/manifest SHA and complete file hashes", 1)[1]
        self.assertIn("python3 scripts/v9-corpus-input-receipt.py " + chr(92) + "\n", consumer)
        for line in consumer.splitlines():
            if line.rstrip().endswith(chr(92)):
                trailing = len(line) - len(line.rstrip(chr(92)))
                self.assertEqual(trailing, 1, "Double Bash continuation: " + line.strip())
        for flag in ("--generator-source", "--ibge-names", "--birth-daily"):
            self.assertIn(flag, consumer)


    def test_verified_prepared_corpus_is_bound_to_v3_plan_without_physical_train(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        consumer = text.split("  train-handoff-probe:", 1)[1]
        self.assertIn("scripts/v9-corpus-handoff-verify.py", consumer)
        self.assertIn("--train-destination .local/v9-handoff-received/verified-train-input", consumer)
        self.assertIn("scripts/v9-scale-100k-technical-plan.py", consumer)
        self.assertIn("--corpus-root .local/v9-handoff-received/verified-train-input", consumer)
        self.assertIn("scripts/v9-v3-four-name-100k-plan.py", consumer)
        self.assertIn("--expected-sha256 5bdbce7ffd5fed55fc085fde42faf9793cb39e6e79c3db4c69990a818bf330d7", consumer)
        self.assertIn("GH_TOKEN: ${{ github.token }}", consumer)
        self.assertNotIn("--v9-calibrate", consumer)
        self.assertNotIn("v9-tf4-sealed-test-preparation.py", consumer)

if __name__ == "__main__":
    unittest.main()
