"""Regression of docs-only PR gating: preserve required names and security."""
import unittest
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]

class DocsOnlyFastGateTests(unittest.TestCase):
    def test_classifier_keeps_required_checks_for_docs_only(self):
        import importlib.util
        path = ROOT / "Solution/scripts/ci-impact-classifier.py"
        spec = importlib.util.spec_from_file_location("ci_docs_only", path)
        classifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(classifier)
        docs = classifier.classify(["Solution/docs/Indice_Decisoes_Vigentes.md"])
        self.assertTrue(docs["docs_only"])
        self.assertFalse(docs["run_full"])
        for changed in (
            [".github/workflows/jornada-pr-required-fast.yml"],
            ["Solution/tests/scripts/test_ci_docs_only_fast_gate.py"],
            ["Solution/docs/Indice_Decisoes_Vigentes.md", "Solution/src/Jornada.Api/Program.cs"],
        ):
            result = classifier.classify(changed)
            self.assertFalse(result["docs_only"])
            self.assertTrue(result["run_full"])

    def test_full_unit_gate_matches_master_and_reuses_build(self):
        s = (ROOT / ".github/workflows/jornada-pr-required-fast.yml").read_text()
        master = (ROOT / ".github/workflows/ci.yml").read_text()
        self.assertIn("run_unit_full: ${{ steps.classify.outputs.run_unit_full }}", s)
        gate = s.split("name: Full C# unit suite before merge", 1)[1].split(
            "name: Upload full PR unit evidence", 1)[0]
        selection = '--filter "TestCategory!=Integration&TestCategory!=SQLSmoke"'
        self.assertIn(selection, master)
        self.assertIn(selection, gate)
        self.assertIn("--configuration Release --no-build", gate)
        self.assertIn("--forbid-skipped --minimum-tests 1", gate)
        self.assertIn("PR_FULL_UNIT_ELAPSED_SECONDS", gate)
        self.assertNotIn("continue-on-error", gate)
        self.assertIn("needs.classify-impact.outputs.run_unit_full != 'false'", s)
        self.assertIn("pr-full-unit-evidence", s)
        self.assertIn("if-no-files-found: error", s)

    def test_classification_and_safe_skip(self):
        s = (ROOT / ".github/workflows/jornada-pr-required-fast.yml").read_text()
        self.assertIn("  classify-impact:", s)
        self.assertIn("ci-impact-classifier.py --github-event", s)
        self.assertNotIn("git ls-files", s)
        self.assertNotIn("paths-ignore:", s)
        self.assertIn("fetch-depth: 0", s)
        for name in ("unit-runtime", "deterministic-build"):
            part = re.split(r"(?m)^  [a-z][a-z0-9-]*:\s*$", s.split("  " + name + ":\n", 1)[1], maxsplit=1)[0]
            self.assertIn("needs: classify-impact", part)
            self.assertNotIn("\n    if: needs.classify-impact.outputs.docs_only", part)
            self.assertIn("if: needs.classify-impact.outputs.docs_only != 'true'", part)
            self.assertNotIn("name: Upload startup probe logs\n        if: always()\n", part)
        guard = s.split("  security-guards:\n", 1)[1]
        self.assertIn("if: always()", guard)
        self.assertIn("if: needs.classify-impact.result != 'success'", guard)
        self.assertIn("run: exit 1", guard)
        self.assertIn("source-sanity-gate.py --self-test", guard)

if __name__ == "__main__":
    unittest.main()
