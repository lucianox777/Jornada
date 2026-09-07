#!/usr/bin/env python3
"""Regression tests for the SQL Server/shared Linkage decision gate.

Only the target AST statements are executed against synthetic source files.
No database, model activation, or identity publication is performed.
"""
from __future__ import annotations

import ast
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
GATE = ROOT / "scripts" / "technical-closure-gate.py"
SQL_REL = "src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs"
POLICY_REL = "src/Jornada.Linkage.Runner/ProbabilisticLinkagePolicy.cs"
DELEGATE = "return ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);"
INVARIANTS = (
    "internal static class ProbabilisticLinkageDecisions",
    "decimal? margin =",
    "best.Score - secondScore.Value",
    "best.Score < model.Threshold",
    "margin!.Value < model.ConflictMargin",
    "ThenBy(x => x.PessoaUuid)",
)


class GateFailure(Exception):
    pass


def extract_gate():
    tree = ast.parse(GATE.read_text(encoding="utf-8"))
    main = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "main")
    for index, node in enumerate(main.body):
        if isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id == "sql_linkage" for target in node.targets):
            decision = main.body[index + 1]
            if not isinstance(decision, ast.If):
                raise GateFailure("Expected decision gate immediately after SQL source loading")
            return compile(ast.fix_missing_locations(ast.Module(body=[node, decision], type_ignores=[])), str(GATE), "exec")
    raise GateFailure("Shared Linkage decision gate is missing")


class LinkageSharedPolicyGateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.gate = extract_gate()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="jornada-linkage-gate-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write(SQL_REL, DELEGATE)
        self.write(POLICY_REL, "\n".join(INVARIANTS))

    def write(self, relative, content):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def check(self):
        def require(text, snippets, context):
            for snippet in snippets:
                if snippet not in text:
                    raise GateFailure(f"{context}: {snippet}")

        def fail(message):
            raise GateFailure(message)

        exec(self.gate, {"ROOT": self.root, "require": require, "fail": fail})

    def test_shared_policy_accepts_complete_invariants(self):
        self.check()

    def test_legacy_margin_still_passes_without_shared_policy(self):
        self.write(SQL_REL, "decimal? margin = secondScore is null ? null : best.Score - secondScore.Value;")
        (self.root / POLICY_REL).unlink()
        self.check()

    def test_missing_delegation_fails_closed(self):
        self.write(SQL_REL, "return other.Resolve(model, observation, candidates);")
        with self.assertRaises(GateFailure):
            self.check()

    def test_missing_shared_policy_fails_closed(self):
        (self.root / POLICY_REL).unlink()
        with self.assertRaises(GateFailure):
            self.check()

    def test_each_shared_invariant_is_required(self):
        for missing in INVARIANTS:
            with self.subTest(missing=missing):
                self.write(POLICY_REL, "\n".join(x for x in INVARIANTS if x != missing))
                with self.assertRaises(GateFailure):
                    self.check()

    def test_no_margin_declaration_does_not_pass_from_other_tokens(self):
        self.write(SQL_REL, DELEGATE)
        self.write(POLICY_REL, "\n".join(x for x in INVARIANTS if x != "decimal? margin ="))
        with self.assertRaises(GateFailure):
            self.check()


if __name__ == "__main__":
    unittest.main(verbosity=2)
