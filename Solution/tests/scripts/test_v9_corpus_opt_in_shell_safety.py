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


if __name__ == "__main__":
    unittest.main()
