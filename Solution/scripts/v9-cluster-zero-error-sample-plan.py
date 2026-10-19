"""Scenario planning for the existing CP/Hoeffding bound; never certification.

The accepted mean is held fixed at the supplied observed value and future
identity errors are assumed zero. Neither assumption is a power guarantee.
No corpus, holdout labels, model selection or operational state is accessed.
"""

import argparse
import json
import math


def upper(n: int, comparisons: int, alpha: float, normalized_mean: float):
    log_inverse_component_alpha = math.log(2) + math.log(comparisons) - math.log(alpha)
    lower = normalized_mean - math.sqrt(log_inverse_component_alpha / (2 * n))
    if lower <= 0:
        return None
    cp_zero_errors = -math.expm1(-log_inverse_component_alpha / n)
    return min(1.0, cp_zero_errors / lower)


def required(comparisons: int, alpha: float, mean: float, target: float):
    def meets(n):
        value = upper(n, comparisons, alpha, mean)
        return value is not None and value <= target

    # IndependentGroups is an Int32 in the current C# evidence contract.
    maximum = 2_147_483_647
    if not meets(maximum):
        return None
    low, high = 2, maximum
    while low < high:
        middle = (low + high) // 2
        if meets(middle):
            high = middle
        else:
            low = middle + 1
    assert meets(low) and (low == 2 or not meets(low - 1))
    return low


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--identities", type=int, default=5556)
    parser.add_argument("--accepted", type=int, default=7505)
    parser.add_argument("--cap", type=int, default=16)
    parser.add_argument("--base-people", type=int, default=30000)
    parser.add_argument("--alpha", type=float, default=.05)
    parser.add_argument("--target", type=float, default=.01)
    parser.add_argument("--comparisons", type=int, nargs="+", default=[1, 36, 512, 4096, 65536, 1000000])
    args = parser.parse_args()
    if (not 2 <= args.identities <= 2_147_483_647 or args.cap < 1
            or not 0 < args.accepted <= args.identities * args.cap
            or args.base_people < args.identities
            or not 0 < args.alpha < 1 or not 0 < args.target < 1
            or any(not 1 <= count <= 2_147_483_647 for count in args.comparisons)):
        parser.error("Invalid counts, probability, cap or Int32 configuration count")
    mean = args.accepted / (args.identities * args.cap)
    rows = []
    for count in args.comparisons:
        n = required(count, args.alpha, mean, args.target)
        rows.append({
            "compared_configurations": count,
            "conditional_upper_at_observed_n_assuming_zero_fp": upper(args.identities, count, args.alpha, mean),
            "required_independent_identities_scenario": n,
            "deficit_from_observed_count": max(0, n - args.identities) if n is not None else None,
            "base_people_at_same_retained_validation_fraction_scenario":
                (n * args.base_people + args.identities - 1) // args.identities if n is not None else None,
        })
    print(json.dumps({
        "status": "SCENARIO_ONLY_NOT_POWER_GUARANTEE_OR_IID_EVIDENCE",
        "inputs": vars(args),
        "normalized_accepted_mean": mean,
        "assumed_future_false_acceptances": 0,
        "iid_independently_validated": False,
        "test_labels_used": False,
        "rows": rows,
    }, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
