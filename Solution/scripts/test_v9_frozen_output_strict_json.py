#!/usr/bin/env python3
"""Regression: frozen V9 TRAIN artifacts must never contain non-JSON numbers."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("v9-nested-calibration-study.py")
spec = importlib.util.spec_from_file_location("v9_nested_study", SCRIPT)
study = importlib.util.module_from_spec(spec)
spec.loader.exec_module(study)


class FrozenJsonTests(unittest.TestCase):
    def test_nonfinite_plan_is_rejected_and_removed(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "plan.json"
            with self.assertRaises(ValueError):
                study._write_frozen_outputs(path, {"Calibration": {"Score": float("nan")}})
            self.assertFalse(path.exists())

    def test_nonfinite_budget_is_rejected_and_both_outputs_removed(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "plan.json"
            budget = {"study_fingerprint_sha256": "fingerprint", "risk": float("inf")}
            plan = {"Calibration": {"StudyFingerprintSha256": "fingerprint"}}
            with self.assertRaises(ValueError):
                study._write_frozen_outputs(path, plan, budget)
            self.assertFalse(path.exists())
            self.assertFalse(path.with_suffix(".tf-budget.json").exists())

    def test_finite_plan_and_budget_roundtrip(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "plan.json"
            plan = {"Calibration": {"StudyFingerprintSha256": "fingerprint", "score": 0.5}}
            budget = {"study_fingerprint_sha256": "fingerprint", "risk": 0.01}
            study._write_frozen_outputs(path, plan, budget)
            self.assertEqual(json.loads(path.read_text()), plan)
            self.assertEqual(json.loads(path.with_suffix(".tf-budget.json").read_text()), budget)


if __name__ == "__main__":
    unittest.main()
