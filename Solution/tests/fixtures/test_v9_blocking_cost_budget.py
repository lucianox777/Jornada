"""Versioned, observed-only V9 cost budget: no population-scale certification."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[2] / "scripts" / "v9-evaluate-blocking-cost-budget.py"
spec = importlib.util.spec_from_file_location("v9_cost_budget", MODULE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
SHA = "f" * 64

def fixture():
    stats = {
        "M": 96, "U": 10, "all_no_cpf_truth_M": 100,
        "global_recall_target_met": True, "u_degree": {
            "mean": 1.0, "p95": 3, "p99": 5, "max": 8}}
    ci = {
        "method": "NONPARAMETRIC_IDENTITY_CLUSTER_BOOTSTRAP_V1",
        "population_or_fdr_certification": False,
        "point_recall": 0.96, "lower_one_sided_95": 0.951,
        "statistically_supported_global_95": True}
    return {
        "engine": "sqlite3_memory_real_indexed_sql",
        "canonical_observations_sha256": SHA,
        "diagnostic_only": True, "may_promote_policy": False,
        "TEST_evaluated": False, "fdr_measured": False,
        "sql_server_p95_measured": False, "selection_split": "TRAIN_ONLY",
        "results": {
            split: {"all_no_cpf_truth_M": 100,
                    "name_eligible_truth_M": 98,
                    "name_eligible_observations": 20}
            for split in ("TRAIN", "VALIDATION")
        },
        "minimum_U_95pct_diagnostic": {
            "trained_on": "TRAIN_ONLY", "validation_refit": False,
            "target_is_diagnostic_not_promotion": True,
            "plans": {
                name: {"selected_cells": ["P_PHON_M_LAST"],
                       "measured": {"TRAIN": dict(stats), "VALIDATION": dict(stats)}}
                for name in ("greedy_pruned", "exact_targeted")
            }
        },
        "independent_base_identity_recall_confidence": {
            split: {name: dict(ci) for name in ("greedy_pruned","exact_targeted")}
            for split in ("TRAIN", "VALIDATION")
        },
    }

def budget():
    return {
        "version": m.VERSION, "source_sha256": SHA,
        "minimum_global_M_recall": 0.95,
        "max_u_degree_mean": 2.0,
        "max_u_degree_p95": 4, "max_u_degree_p99": 7,
    }

class CostBudgetTests(unittest.TestCase):
    def test_observed_budget_met_is_still_not_approved_to_promote(self):
        value = m.assess(fixture(), budget())
        for name in m.PLAN_NAMES:
            self.assertEqual(value["plans"][name]["status"],
                             "OBSERVED_DIAGNOSTIC_BUDGET_MET")
            self.assertEqual(value["plans"][name]["splits"]["TRAIN"]["U"], 10)
            self.assertEqual(value["plans"][name]["splits"]["TRAIN"]["u_degree_mean"], 1.0)
        self.assertFalse(value["may_promote_policy"])
        self.assertFalse(value["city_scale_certified"])
        self.assertFalse(value["fdr_measured"])
        self.assertFalse(value["TEST_evaluated"])

    def test_point_recall_96_cannot_mask_cluster_lower_below_95(self):
        report = fixture()
        report["independent_base_identity_recall_confidence"]["TRAIN"][
            "greedy_pruned"]["lower_one_sided_95"] = 0.944
        report["independent_base_identity_recall_confidence"]["TRAIN"][
            "greedy_pruned"]["statistically_supported_global_95"] = False
        result = m.assess(report, budget())
        self.assertEqual(result["plans"]["greedy_pruned"]["status"],
                         "OBSERVED_DIAGNOSTIC_BUDGET_UNMET")
        self.assertIn("CLUSTER_LOWER_RECALL_BELOW_POLICY",
                      result["plans"]["greedy_pruned"]["splits"]["TRAIN"]["reasons"])

    def test_absent_recall_floor_is_diagnostic_not_a_universal_95_gate(self):
        report = fixture()
        # Remove the optional recall target and prove 95% is not silently
        # reinstated as a policy constraint, while preserving its metrics.
        policy = budget()
        del policy["minimum_global_M_recall"]
        report["minimum_U_95pct_diagnostic"]["plans"]["greedy_pruned"]["measured"]["TRAIN"]["M"] = 92
        report["minimum_U_95pct_diagnostic"]["plans"]["greedy_pruned"]["measured"]["TRAIN"]["global_recall_target_met"] = False
        report["independent_base_identity_recall_confidence"]["TRAIN"]["greedy_pruned"]["point_recall"] = 0.92
        report["independent_base_identity_recall_confidence"]["TRAIN"]["greedy_pruned"]["lower_one_sided_95"] = 0.89
        report["independent_base_identity_recall_confidence"]["TRAIN"]["greedy_pruned"]["statistically_supported_global_95"] = False
        result = m.assess(report, policy)
        self.assertEqual(result["plans"]["greedy_pruned"]["status"],
                         "OBSERVED_DIAGNOSTIC_BUDGET_MET")
        self.assertEqual(result["optional_recall_floor"], None)
        self.assertEqual(result["plans"]["greedy_pruned"]["splits"]["TRAIN"]["global_M_recall"], .92)
        policy["minimum_global_M_recall"] = .91
        result = m.assess(report, policy)
        self.assertIn("CLUSTER_LOWER_RECALL_BELOW_POLICY",
                      result["plans"]["greedy_pruned"]["splits"]["TRAIN"]["reasons"])

    def test_optional_recall_floor_has_fail_closed_domain(self):
        for invalid in (True, -0.01, 1.01, float("nan"), float("inf"), "0.95"):
            policy = budget()
            policy["minimum_global_M_recall"] = invalid
            with self.subTest(invalid=invalid), self.assertRaisesRegex(
                    ValueError, "explicit versioned budget"):
                m.assess(fixture(), policy)

    def test_p95_p99_and_mean_are_separate_constraints(self):
        policy = budget()
        policy["max_u_degree_p99"] = 4
        outcome = m.assess(fixture(), policy)
        self.assertIn("MAX_U_DEGREE_P99_EXCEEDED",
                      outcome["plans"]["exact_targeted"]["splits"]["VALIDATION"]["reasons"])
        policy["max_u_degree_p99"] = 7
        policy["max_u_degree_mean"] = 0.7
        outcome = m.assess(fixture(), policy)
        self.assertIn("MAX_U_DEGREE_MEAN_EXCEEDED",
                      outcome["plans"]["exact_targeted"]["splits"]["TRAIN"]["reasons"])

    def test_u_mean_is_two_endpoints_and_zero_degree_records_in_denominator(self):
        report = fixture()
        report["minimum_U_95pct_diagnostic"]["plans"]["greedy_pruned"][
            "measured"]["TRAIN"]["u_degree"]["mean"] = 0.5  # wrong U / N
        with self.assertRaisesRegex(ValueError, "BOTH endpoints"):
            m.assess(report, budget())

    def test_global_denominator_leakage_and_missing_governance_rejected(self):
        report = fixture()
        policy = budget()
        policy["source_sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "canonical SHA"):
            m.assess(report, policy)
        policy = budget()
        del policy["max_u_degree_p95"]
        with self.assertRaisesRegex(ValueError, "budget fields"):
            m.assess(report, policy)
        report["TEST_evaluated"] = True
        with self.assertRaisesRegex(ValueError, "TEST-blind"):
            m.assess(report, budget())

    def test_no_overwrite_and_no_sensitive_data_in_report(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            source, policy, output = (root / n for n in ("report.json","budget.json","out.json"))
            source.write_text(json.dumps(fixture()), encoding="utf-8")
            policy.write_text(json.dumps(budget()), encoding="utf-8")
            result = m.run(source,policy,output)
            self.assertEqual(result["source_sha256"], SHA)
            self.assertEqual(json.loads(output.read_text())["version"], m.VERSION)
            with self.assertRaisesRegex(ValueError,"never overwrite"):
                m.run(source,policy,output)
            self.assertNotIn("nome_normalizado", output.read_text())

if __name__ == "__main__":
    unittest.main()
