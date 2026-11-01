#!/usr/bin/env python3
"""Finite, predeclared V9 nominal TF option grid. Zero means a chosen TF0 slot.

The search space is staged, not the full 6^4 Cartesian grid:
1 neutral control + 16 single-slot + 24 two-slot bilateral
+ 4 two-surname shared-contribution variants = 45.
Modes and units use .NET enum numeric values; all four slots are always explicit.
"""
from itertools import combinations

# (side enum, unit enum, stable name): Person=0, Mother=1;
# FirstName=2, LastContentSurname=3 in NominalFrequencyUnitV9.
SLOTS = ((0, 2, "PF"), (0, 3, "PL"), (1, 2, "MF"), (1, 3, "ML"))
WEIGHTS = (0.5, 1.0)
MODES = (0, 1)  # Symmetric, AsymmetricPenaltyOnly


def option_grid(alpha=0.5, threshold=0.95):
    if not (0 < alpha <= 1000 and 0 < threshold < 1):
        raise ValueError("Invalid TF search alpha/threshold")
    def make(label, selected):
        slots = [{"Side": side, "Unit": unit, "Weight": selected.get((side, unit), (0, 0))[0],
                  "Mode": selected.get((side, unit), (0, 0))[1]}
                 for side, unit, _ in SLOTS]
        return {"OptionId": f"TF_{label}_A{alpha:g}_T{threshold:g}",
                "SmoothingAlpha": alpha, "DecisionThreshold": threshold,
                "TfWeight": 0, "UnitTfOptions": slots}
    # Historical TF0 has no new UnitTfOptions: it remains a true baseline
    # rather than a new four-unit fitted model with a distinct provenance.
    results = [{"OptionId": f"A{alpha:g}_T{threshold:g}_TF0",
                "SmoothingAlpha": alpha, "DecisionThreshold": threshold,
                "TfWeight": 0}]
    for side, unit, name in SLOTS:
        for weight in WEIGHTS:
            for mode in MODES:
                results.append(make(f"ONE_{name}_W{weight:g}_M{mode}", {(side, unit): (weight, mode)}))
    for first, second in combinations(SLOTS, 2):
        for weight in WEIGHTS:
            for mode in MODES:
                selected = {(first[0], first[1]): (weight, mode),
                            (second[0], second[1]): (weight, mode)}
                results.append(make(f"TWO_{first[2]}_{second[2]}_W{weight:g}_M{mode}", selected))
    # Decision 12: bilateral is the production candidate. Shared-surname
    # person-side-only is diagnostic evidence, never an automatic finalist.
    # The nested plan excludes these four options from ModelOptions.
    for weight in WEIGHTS:
        for mode in MODES:
            selected = {(0, 3): (weight, mode), (1, 3): (weight, mode)}
            variant = make(f"SHARED_PL_ML_W{weight:g}_M{mode}", selected)
            variant["SharedSurnameMode"] = 1  # PersonSideOnlyWhenShared
            results.append(variant)
    if len(results) != 45 or len({item["OptionId"] for item in results}) != 45:
        raise AssertionError("V9 TF grid does not match its frozen predeclared size")
    for item in results:
        if item is results[0]:
            continue  # original TF0, no individually fitted slots
        slots = item["UnitTfOptions"]
        if len(slots) != 4 or len({(x["Side"], x["Unit"]) for x in slots}) != 4:
            raise AssertionError("Duplicate or missing TF unit")
    return results


def pinned_reference(raw):
    """Do not guess published IBGE SHA/bootstrap from the synthetic corpus."""
    keys = ("PinnedIbgeReferenceVersionId", "PinnedIbgePublishedSha256",
            "PinnedIbgeMaterializedFrequencySha256", "PinnedIbgeBootstrap")
    if set(raw) != set(keys):
        raise ValueError("Pinned IBGE manifest must have exactly the four contract fields")
    if type(raw[keys[0]]) is not int or raw[keys[0]] < 1:
        raise ValueError("Positive published IBGE version id required")
    for name in keys[1:3]:
        value = raw[name]
        if not isinstance(value, str) or len(value) != 64 or any(
            c not in "0123456789abcdefABCDEF" for c in value
        ):
            raise ValueError(f"Invalid pinned reference hash {name}")
    bootstrap = raw[keys[3]]
    needed = {"Provenance", "People", "CorpusFingerprint",
              "GeneratorVersion", "GeneratorSeed", "GenerationHypothesesFingerprint"}
    if not isinstance(bootstrap, dict) or set(bootstrap) != needed:
        raise ValueError("Pinned synthetic provenance must be complete and exact")
    if not bootstrap["Provenance"] or bootstrap["People"] != 30000 or (
        not isinstance(bootstrap["GeneratorSeed"], int)
        or bootstrap["GeneratorSeed"] < 0
        or not bootstrap["GeneratorVersion"]
        or not bootstrap["GenerationHypothesesFingerprint"]
        or not bootstrap["CorpusFingerprint"]
    ):
        raise ValueError("Pinned synthetic bootstrap invalid")
    return {key: raw[key] for key in keys}


def budget_estimate(models, rulesets, margins, previous=0):
    if min(rulesets, margins) < 1 or previous < 0:
        raise ValueError("Invalid declared family size")
    comparisons = rulesets * len(models) * margins + previous
    return {"family": "V9_TF_STAGE_0_1_2_UNITS_V1",
            "tf0": 1, "single_slot": 16, "two_slot": 24,
            "shared_surname_variant": 4,
            "models": len(models), "rule_sets": rulesets,
            "margins": margins, "previously_compared": previous,
            "predeclared_comparisons": comparisons,
            "exhaustive_four_slot_cartesian": False,
            "test_used": False, "physically_executed": False,
            "certified": False}
