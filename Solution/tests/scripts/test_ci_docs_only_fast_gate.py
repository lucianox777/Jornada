"""Regression of docs-only PR gating: preserve required names and security."""
import unittest
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]

class DocsOnlyFastGateTests(unittest.TestCase):
    def test_classification_and_safe_skip(self):
        s = (ROOT / ".github/workflows/jornada-pr-required-fast.yml").read_text()
        self.assertIn("  classify-impact:", s)
        self.assertIn("git diff --name-only", s)
        self.assertIn("ci-impact-classifier.py --files-from", s)
        self.assertIn("grep -E '^docs_only=' impact.env", s)
        for name in ("unit-runtime", "deterministic-build"):
            part = re.split(r"(?m)^  [a-z][a-z0-9-]*:\s*$", s.split("  " + name + ":\n", 1)[1], maxsplit=1)[0]
            self.assertIn("needs: classify-impact", part)
            self.assertIn("if: needs.classify-impact.outputs.docs_only != 'true'", part)
        guard = s.split("  security-guards:\n", 1)[1]
        self.assertIn("if: always() && needs.classify-impact.result == 'success'", guard)
        self.assertIn("source-sanity-gate.py --self-test", guard)

if __name__ == "__main__":
    unittest.main()
