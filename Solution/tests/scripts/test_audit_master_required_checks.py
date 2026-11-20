#!/usr/bin/env python3
"""Offline negative cases for the read-only master protection audit."""
import importlib.util
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / "scripts/audit-master-required-checks.py"
spec = importlib.util.spec_from_file_location("master_checks_audit", SOURCE)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class MasterRequiredChecksAuditTests(unittest.TestCase):
    def test_missing_protection_never_passes(self):
        self.assertFalse(audit.evaluate({}, [])["verified"])

    def test_partial_contexts_do_not_pass(self):
        p = {"required_status_checks": {"strict": True, "contexts": ["unit-runtime"]}}
        self.assertFalse(audit.evaluate(p, [])["verified"])

    def test_full_strict_contexts_pass(self):
        p = {"required_status_checks": {"strict": True, "contexts": sorted(audit.REQUIRED)}}
        self.assertTrue(audit.evaluate(p, [])["verified"])

    def test_inactive_or_unscoped_rulesets_do_not_prove_master(self):
        rules = [{"name": "master protection", "enforcement": "active"}]
        self.assertFalse(audit.evaluate({}, rules)["verified"])
        self.assertTrue(audit.evaluate({}, rules)["rulesets_require_manual_scope_review"])

    def test_non_strict_check_configuration_not_certified(self):
        p = {"required_status_checks": {"strict": False, "contexts": sorted(audit.REQUIRED)}}
        self.assertFalse(audit.evaluate(p, [])["verified"])


if __name__ == "__main__":
    unittest.main()
