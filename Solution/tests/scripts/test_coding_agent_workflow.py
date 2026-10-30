#!/usr/bin/env python3
"""Hermetic regression tests for the manually dispatched GitHub Models coding agent.

No inference endpoint, GitHub API, SQL engine, or model activation is accessed.
The exact Python embedded in the GitHub Actions workflow is compiled and its
patch parser and TRX gate are exercised with adversarial synthetic inputs.
"""
import ast
import json
import os
from pathlib import Path
import tempfile
import textwrap
import unittest
import xml.etree.ElementTree as ET

WORKFLOW = Path(__file__).resolve().parents[3] / ".github/workflows/jornada-coding-agent.yml"


def embedded_python_blocks():
    lines = WORKFLOW.read_text(encoding="utf-8").splitlines()
    blocks = []
    at = 0
    while at < len(lines):
        if "python3 - <<'PY'" not in lines[at]:
            at += 1
            continue
        start = at + 1
        at = start
        while at < len(lines) and lines[at].strip() != "PY":
            at += 1
        if at >= len(lines):
            raise AssertionError("Unclosed embedded Python heredoc")
        blocks.append(textwrap.dedent("\n".join(lines[start:at])))
        at += 1
    return blocks


class CodingAgentWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.saved_mode = os.environ.get('MODE')
        os.environ['MODE'] = 'apply'

    def tearDown(self):
        if self.saved_mode is None:
            os.environ.pop('MODE', None)
        else:
            os.environ['MODE'] = self.saved_mode

    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.blocks = embedded_python_blocks()
        if len(cls.blocks) != 2:
            raise AssertionError("Expected exactly inference/patch and TRX gate Python blocks")
        for block in cls.blocks:
            ast.parse(block)

        patch = cls.blocks[0]
        start = patch.index("response=response.strip()")
        end_marker = "target.write_text(original.replace(old,new,1))"
        end = patch.index(end_marker, start) + len(end_marker)
        cls.patch_fragment = patch[start:end]

    def test_workflow_executes_inference_only_on_manual_dispatch(self):
        self.assertIn("pull_request:", self.workflow)
        self.assertIn("if: github.event_name == 'pull_request'", self.workflow)
        self.assertIn("if: github.event_name == 'workflow_dispatch'", self.workflow)
        self.assertIn("dotnet-version: '10.0.x'", self.workflow)
        self.assertIn("default: diagnostic", self.workflow)
        self.assertIn("MODE: ${{ inputs.mode }}", self.workflow)
        self.assertIn("contents: read", self.workflow)
        self.assertIn("contents: write", self.workflow)
        self.assertIn("if str(p).startswith('Solution/src/')", self.workflow)
        self.assertIn('git add -- "$CHANGED_FILE"', self.workflow)
        self.assertIn('git diff --check', self.workflow)
        self.assertIn("RESULTS_DIR=", self.workflow)

    def attempt_patch(self, response, initial="A"):
        with tempfile.TemporaryDirectory() as temporary:
            target = Path(temporary) / "safe.cs"
            target.write_text(initial, encoding="utf-8")
            variables = {"json": json, "os": os, "pathlib": __import__("pathlib"),
                         "paths": [target], "response": response}
            try:
                exec(compile(self.patch_fragment, str(WORKFLOW), "exec"), variables)
                rejected = False
            except SystemExit as exc:
                self.assertEqual(exc.code, 0, "Malformed patch must fail closed without push")
                rejected = True
            return rejected, target.read_text(encoding="utf-8")

    @staticmethod
    def proposal(path, old, new):
        return json.dumps({"path": str(path), "old": old, "new": new})

    def test_json_patch_and_fenced_json_patch(self):
        with tempfile.TemporaryDirectory() as temporary:
            # Check actual policy without relying on a previously hardcoded path.
            target = Path(temporary) / "safe.cs"
            for fenced in (False, True):
                target.write_text("A", encoding="utf-8")
                payload = self.proposal(target, "A", "B")
                if fenced:
                    payload = "&#96;&#96;&#96;json\n" + payload + "\n&#96;&#96;&#96;"
                payload = payload.replace("&#96;", "\x60")
                scope = {"json": json, "os": os, "pathlib": __import__("pathlib"),
                         "paths": [target], "response": payload}
                exec(compile(self.patch_fragment, str(WORKFLOW), "exec"), scope)
                self.assertEqual(target.read_text(encoding="utf-8"), "B")

    def test_diagnostic_mode_validates_proposal_without_modifying_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            target = Path(temporary) / 'safe.cs'
            target.write_text('A', encoding='utf-8')
            os.environ['MODE'] = 'diagnostic'
            payload = self.proposal(target, 'A', 'B')
            with self.assertRaises(SystemExit) as result:
                exec(compile(self.patch_fragment, str(WORKFLOW), 'exec'),
                     {'json': json, 'os': os, 'pathlib': __import__('pathlib'),
                      'paths': [target], 'response': payload})
            self.assertEqual(result.exception.code, 0)
            self.assertEqual(target.read_text(encoding='utf-8'), 'A')

    def test_patch_rejects_invalid_types_unapproved_path_and_nonunique_old(self):
        for payload, source in (
            (json.dumps({"path": ["bad"], "old": "A", "new": "B"}), "A"),
            (json.dumps({"path": "", "old": "A", "new": "B"}), "A"),
            (json.dumps({"path": "bad.cs", "old": "A", "new": "B"}), "A"),
            (json.dumps({"path": "", "old": None, "new": "B"}), "A"),
            ("not valid json", "A"),
            ("&#96;&#96;&#96;json\n{}\nno closing fence", "A"),
        ):
            rejected, remaining = self.attempt_patch(payload.replace("&#96;", "\x60"), source)
            self.assertTrue(rejected)
            self.assertEqual(remaining, source)
        with tempfile.TemporaryDirectory() as temporary:
            target = Path(temporary) / "safe.cs"
            target.write_text("A A", encoding="utf-8")
            payload = self.proposal(target, "A", "B")
            with self.assertRaises(SystemExit) as result:
                exec(compile(self.patch_fragment, str(WORKFLOW), "exec"),
                     {"json": json, "os": os, "pathlib": __import__("pathlib"),
                      "paths": [target], "response": payload})
            self.assertEqual(result.exception.code, 0)
            self.assertEqual(target.read_text(encoding="utf-8"), "A A")

    def execute_trx_gate(self, trx_text):
        with tempfile.TemporaryDirectory() as temporary:
            if trx_text is not None:
                (Path(temporary) / "coding-agent.trx").write_text(trx_text, encoding="utf-8")
            old = os.environ.get("RESULTS_DIR")
            os.environ["RESULTS_DIR"] = temporary
            try:
                try:
                    exec(compile(self.blocks[1], str(WORKFLOW), "exec"),
                         {"os": os, "pathlib": __import__("pathlib"), "ET": ET})
                    return True
                except SystemExit:
                    return False
            finally:
                if old is None:
                    os.environ.pop("RESULTS_DIR", None)
                else:
                    os.environ["RESULTS_DIR"] = old

    def test_trx_gate_requires_nonempty_successful_tests(self):
        def trx(total, passed, failed, executed=None, errors=0, timeouts=0, aborted=0):
            if executed is None:
                executed = passed + failed
            return (f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                    f'<ResultSummary><Counters total="{total}" executed="{executed}" '
                    f'passed="{passed}" failed="{failed}" error="{errors}" '
                    f'timeout="{timeouts}" aborted="{aborted}" />'
                    '</ResultSummary></TestRun>')
        self.assertTrue(self.execute_trx_gate(trx(5, 5, 0)))
        self.assertFalse(self.execute_trx_gate(None))
        self.assertFalse(self.execute_trx_gate(trx(0, 0, 0)))
        self.assertFalse(self.execute_trx_gate(trx(5, 0, 0, executed=0)))
        self.assertFalse(self.execute_trx_gate(trx(5, 4, 1)))
        self.assertFalse(self.execute_trx_gate(trx(5, 4, 0, errors=1)))
        self.assertFalse(self.execute_trx_gate(trx(5, 4, 0, timeouts=1)))
        self.assertFalse(self.execute_trx_gate(trx(5, 4, 0, aborted=1)))
        self.assertFalse(self.execute_trx_gate('<TestRun />'))


if __name__ == "__main__":
    unittest.main()
