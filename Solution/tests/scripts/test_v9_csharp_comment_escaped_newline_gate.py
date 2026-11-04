#!/usr/bin/env python3
"""Minimal regression for the V9 SelectionVersion accidental commenting incident."""

import importlib.util
import unittest
from pathlib import Path

GATE_PATH = Path(__file__).resolve().parents[2] / "scripts/v9-csharp-comment-escaped-newline-gate.py"
spec = importlib.util.spec_from_file_location("v9_comment_newline_gate", GATE_PATH)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class V9CsharpCommentNewlineTests(unittest.TestCase):
    def test_literal_newline_after_comment_swallows_real_declaration(self):
        source = '// Pareto V3 note\\n // a second note\\npublic const string SelectionVersion = "V3";\n'
        self.assertEqual(gate.suspicious_lines(source), [1])

    def test_inline_comment_that_swallows_a_declaration(self):
        source = 'var x = 1; // note\\ninternal static void Run() {}\n'
        self.assertEqual(gate.suspicious_lines(source), [1])

    def test_normal_comment_and_newline_is_legal(self):
        source = '// Pareto V3 note\npublic const string SelectionVersion = "V3";\n'
        self.assertEqual(gate.suspicious_lines(source), [])

    def test_escaped_newline_in_strings_and_descriptive_comments_is_legal(self):
        source = (
            'var x = "\\\\npublic const string Foo = bar";\n'
            'var url = "https://example.org/\\\\npublic const";\n'
            '// A literal \\n separates fields in the input format.\n'
        )
        self.assertEqual(gate.suspicious_lines(source), [])


if __name__ == "__main__":
    unittest.main()
