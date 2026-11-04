#!/usr/bin/env python3
"""Reproduce aggregate seed43 paired Hoeffding gate; diagnostic, NOT certification.

No TEST, CSV, SQL, model selection, or inferential substitution.
"""
import argparse
import math
import unittest

CAP = 4
ALPHA = 0.025
IDENTITIES = 5492
ELIGIBLE = 7704
DELTA_TP = 35


def hoeffding_lower(n, delta_tp, cap=CAP, alpha=ALPHA):
    if type(n) is not int or n < 1 or type(delta_tp) is not int:
        raise ValueError("n must be positive integer and delta_tp integer")
    if type(cap) is not int or cap < 1 or not 0 < alpha < 1:
        raise ValueError("invalid cap or alpha")
    if abs(delta_tp) > n * cap:
        raise ValueError("difference outside declared per-identity range")
    mean = delta_tp / n
    penalty = 2 * cap * math.sqrt(math.log(1 / alpha) / (2 * n))
    return mean - penalty


def planning_n(effect_per_identity, cap=CAP, alpha=ALPHA):
    """Conditional lower bound n for Hoeffding > 0 at a FIXED effect assumption."""
    if not math.isfinite(effect_per_identity) or not 0 < effect_per_identity <= cap:
        raise ValueError("positive finite effect <= cap required")
    return math.floor(2 * cap * cap * math.log(1 / alpha) / (effect_per_identity ** 2)) + 1


class AggregateAuditTests(unittest.TestCase):
    def test_seed43_predeclared_gate_fails(self):
        self.assertLess(hoeffding_lower(IDENTITIES, DELTA_TP), 0)

    def test_delta_recall_is_descriptive(self):
        self.assertAlmostEqual(DELTA_TP / ELIGIBLE, 0.004543094496365524, places=12)

    def test_planning_requires_millions_under_observed_effect(self):
        n = planning_n(DELTA_TP / IDENTITIES)
        self.assertEqual(n, 2_906_494)
        self.assertLessEqual(hoeffding_lower(n - 1, round((n - 1) * DELTA_TP / IDENTITIES)), 0)
        self.assertGreater(hoeffding_lower(n, round(n * DELTA_TP / IDENTITIES)), 0)

    def test_rejects_invalid_inputs(self):
        for n, d in ((0, 0), (10, 41), (10, 1.5), (True, 1)):
            with self.subTest(n=n, d=d), self.assertRaises(ValueError):
                hoeffding_lower(n, d)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(AggregateAuditTests)
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        raise SystemExit(0 if result.wasSuccessful() else 1)
    effect = DELTA_TP / IDENTITIES
    print("SOURCE: run 41925355648, documented VALIDATION aggregate only")
    print(f"n={IDENTITIES}, eligible={ELIGIBLE}, delta_tp={DELTA_TP}, cap={CAP}, alpha={ALPHA}")
    print(f"delta_recall={DELTA_TP / ELIGIBLE:.9f}")
    print(f"hoeffding_lower_mean_per_identity={hoeffding_lower(IDENTITIES, DELTA_TP):.9f}")
    print(f"conditional_planning_n_at_fixed_observed_effect={planning_n(effect)}")
    print("NOT_CERTIFIED: IID across identities unvalidated; historical seed43 not fresh; Gold incomplete")


if __name__ == "__main__":
    main()
