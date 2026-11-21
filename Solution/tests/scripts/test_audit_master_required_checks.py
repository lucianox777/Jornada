#!/usr/bin/env python3
"""Offline negative cases for the read-only master protection audit."""
import importlib.util
import io
import urllib.error
import unittest
from unittest.mock import patch
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / "scripts/audit-master-required-checks.py"
spec = importlib.util.spec_from_file_location("master_checks_audit", SOURCE)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class MasterRequiredChecksAuditTests(unittest.TestCase):
    def test_http_error_reports_only_endpoint_and_status(self):
        for failed_endpoint, label in (
            ("/branches/master/protection", "branch_protection"),
            ("/rulesets", "repository_rulesets"),
        ):
            with self.subTest(endpoint=failed_endpoint):
                def fake_request(repo, endpoint, token):
                    if endpoint == failed_endpoint:
                        raise urllib.error.HTTPError(
                            "https://api.github.com/private?token=SECRET",
                            403, "SECRET", {}, io.BytesIO(b"SECRET"))
                    return {"required_status_checks": {"contexts": []}}
                with patch.object(audit, "request_json", side_effect=fake_request):
                    with patch.dict(audit.os.environ, {"GITHUB_TOKEN": "SECRET"}):
                        with patch("sys.argv", ["audit-master-required-checks.py"]):
                            with self.assertRaises(SystemExit) as caught:
                                audit.main()
                message = str(caught.exception)
                self.assertEqual(message, f"UNKNOWN: {label} API returned HTTP 403")
                self.assertNotIn("SECRET", message)
                self.assertNotIn("https:", message)

    def test_non_object_required_checks_are_unverified_not_crash(self):
        for invalid in ([], ["unit-runtime"], "unit-runtime", 42):
            with self.subTest(invalid=invalid):
                result = audit.evaluate({"required_status_checks": invalid}, [])
                self.assertFalse(result["verified"])
                self.assertEqual(result["missing_classic_contexts"], sorted(audit.REQUIRED))
                self.assertIsNone(result["classic_strict_setting"])

    def test_missing_protection_never_passes(self):
        self.assertFalse(audit.evaluate({}, [])["verified"])

    def test_partial_contexts_do_not_pass(self):
        p = {"required_status_checks": {"strict": True, "contexts": ["unit-runtime"]}}
        self.assertFalse(audit.evaluate(p, [])["verified"])

    def test_full_strict_contexts_pass(self):
        p = {"required_status_checks": {"strict": True, "contexts": sorted(audit.REQUIRED)}}
        self.assertTrue(audit.evaluate(p, [])["verified"])

    def test_split_legacy_and_app_bound_checks_are_combined(self):
        required = sorted(audit.REQUIRED)
        p = {"required_status_checks": {
            "strict": True,
            "contexts": required[:2],
            "checks": [{"context": name, "app_id": 123} for name in required[2:]],
        }}
        self.assertTrue(audit.evaluate(p, [])["verified"])

    def test_missing_check_across_both_lists_is_not_verified(self):
        required = sorted(audit.REQUIRED)
        p = {"required_status_checks": {
            "strict": True,
            "contexts": required[:1],
            "checks": [{"context": name, "app_id": 123} for name in required[1:-1]],
        }}
        self.assertFalse(audit.evaluate(p, [])["verified"])

    def test_inactive_or_unscoped_rulesets_do_not_prove_master(self):
        rules = [{"name": "master protection", "enforcement": "active"}]
        self.assertFalse(audit.evaluate({}, rules)["verified"])
        self.assertTrue(audit.evaluate({}, rules)["rulesets_require_manual_scope_review"])

    def test_non_strict_check_configuration_matches_current_policy(self):
        p = {"required_status_checks": {"strict": False, "contexts": sorted(audit.REQUIRED)}}
        self.assertTrue(audit.evaluate(p, [])["verified"])
        self.assertIs(audit.evaluate(p, [])["classic_strict_setting"], False)

    def test_malformed_api_lists_are_not_mistaken_for_required_contexts(self):
        p = {"required_status_checks": {
            "contexts": "unit-runtime",
            "checks": {"context": "security-guards"},
        }}
        result = audit.evaluate(p, [None, {"enforcement": "active"}])
        self.assertFalse(result["verified"])
        self.assertEqual(result["observed_classic_contexts"], [])
        self.assertEqual(result["missing_classic_contexts"], sorted(audit.REQUIRED))
        self.assertTrue(result["rulesets_require_manual_scope_review"])

    def test_missing_classic_contexts_are_reported(self):
        p = {"required_status_checks": {"contexts": ["unit-runtime"], "checks": []}}
        result = audit.evaluate(p, [])
        self.assertFalse(result["verified"])
        self.assertEqual(result["missing_classic_contexts"],
                         sorted(audit.REQUIRED - {"unit-runtime"}))

    def test_required_fast_emits_read_only_protection_observation(self):
        root = Path(__file__).resolve().parents[3]
        workflow = (root / ".github/workflows/jornada-pr-required-fast.yml").read_text(
            encoding="utf-8")
        self.assertIn("Observe master required-check protection (read-only, nonblocking)", workflow)
        self.assertIn("python3 Solution/scripts/audit-master-required-checks.py", workflow)
        self.assertIn("continue-on-error: true", workflow)
        self.assertIn("actions: read", workflow)
        self.assertIn("GITHUB_STEP_SUMMARY", workflow)
        self.assertNotIn("actions: write", workflow)

    def test_classify_impact_is_indirect_not_fourth_direct_check(self):
        self.assertNotIn("classify-impact", audit.REQUIRED)


if __name__ == "__main__":
    unittest.main()
