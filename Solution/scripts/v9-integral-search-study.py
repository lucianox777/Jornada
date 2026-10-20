#!/usr/bin/env python3
"""Predeclare the COMPLETE historical 19-pass lattice, not a heuristic shortlist.

The clean seed42 corpus is the accepted provisional conservative bootstrap.
This is exploratory TRAIN/VALIDATION; no TEST, publication or activation.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path


CATALOGUE = {
    "P_PHON_M_LAST": ["name_phonetic_ptbr", "mother_name_last"],
    "P_PHON_BIRTH_YEAR": ["name_phonetic_ptbr", "birth_year"],
    "P_LAST_M_PHON": ["name_last", "mother_name_phonetic_ptbr"],
    "P_PHON_M_ANY": ["name_phonetic_ptbr", "mother_name_surnames"],
    "P_ANY_M_PHON": ["name_surnames", "mother_name_phonetic_ptbr"],
    "P_LAST_M_ANY": ["name_last", "mother_name_surnames"],
    "P_FIRST_M_FIRST": ["name_first", "mother_name_first"],
    "P_FIRST_M_LAST": ["name_first", "mother_name_last"],
    "P_FIRST_M_PHON": ["name_first", "mother_name_phonetic_ptbr"],
    "P_PHON_M_FIRST": ["name_phonetic_ptbr", "mother_name_first"],
    "P_FIRST_BIRTH_YEAR": ["name_first", "birth_year"],
    "P_PREFIX3_DOB": ["name_prefix_3", "birth_day", "birth_month", "birth_year"],
    "P_PREFIX3_M_PREFIX3": ["name_prefix_3", "mother_name_prefix_3"],
    "P_PREFIX3_M_LAST": ["name_prefix_3", "mother_name_last"],
    "P_FIRST_DOB_MONTHDAY": ["name_first", "birth_day", "birth_month"],
    "P_ANY_BIRTH_YEAR": ["name_surnames", "birth_year"],
    "P_ANY_DOB_MONTHDAY": ["name_surnames", "birth_day", "birth_month"],
    "P_PREFIX3_BIRTH_YEAR": ["name_prefix_3", "birth_year"],
    "P_LAST_M_FIRST": ["name_last", "mother_name_first"],
}


def prepare(corpus: Path, destination: Path, workers: int = 2, full_union_controls: bool = False) -> None:
    spec = importlib.util.spec_from_file_location("baseline", Path(__file__).with_name("v9-adaptive-30k-study.py"))
    baseline = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(baseline)
    # Reuse verified cap/manifest guards, with no new generator or degradation.
    baseline.prepare(corpus, destination, cap_four=True)
    plan = json.loads(destination.read_bytes())
    full = (1 << len(CATALOGUE)) - 1
    plan["DeclaredSearchSpace"] = {"Catalogue": [{"PassId": key, "Fields": fields}
        for key, fields in sorted(CATALOGUE.items())]}
    plan["CollapseEquivalentCandidateFrames"] = True
    plan["MaximumParallelRuleSets"] = workers
    plan["ModelOptions"] = [{"OptionId": f"A{alpha}_T{threshold}_TF0", "SmoothingAlpha": alpha,
        "DecisionThreshold": threshold, "TfWeight": 0}
        for alpha in [.1, .5, 1] for threshold in [.90, .95, .99]]
    plan["PreviouslyComparedValidationConfigurations"] = 3726
    plan["MaximumPhysicalRuleSets"] = full + len(plan["BaselineRuleSets"])
    plan["MaximumComparedConfigurations"] = (plan["MaximumPhysicalRuleSets"]
        * len(plan["ModelOptions"]) * len(plan["ConflictMargins"]) + 3726)
    plan["Execution"]["MaximumCandidatePairIncidences"] = 20000000
    plan["Execution"]["MaximumProjectedSignatures"] = 5000000
    plan["RiskCeilings"] = [.001, .0025, .005, .01, .02, .05]
    plan["SampleDesign"] = dict(Alpha=.05, Power=.8, RecallNull=.95, RecallAlternative=.97,
        RecallHalfWidth=.01, CellHalfWidth=.02, MinimumCellProbability=.001, Cells=81,
        ComparedConfigurations=plan["MaximumComparedConfigurations"],
        MaximumAcceptedPerIndependentUnit=4, ExpectedAcceptedPerIndependentUnit=1.3,
        ValidationFraction=.2, TestFraction=.2, RetainedIndependentUnitFraction=.9,
        ExpectedEligibleSourcesPerIndependentUnit=1.3)
    if full_union_controls:
        # Explicit CONTROL study: only full union and historical baseline are
        # fitted. Keep the whole family's multiplicity; never claim search done.
        plan["DeclaredSearchSpace"].update(MinimumPasses=19, MaximumPasses=19)
        plan["PreviouslyComparedValidationConfigurations"] = plan["MaximumComparedConfigurations"] - 54
    study = hashlib.sha256(("V9_COMPLETE_19_PASS_LATTICE_RISK_BANDS_V1|"
        + json.dumps(plan, sort_keys=True, separators=(",", ":"))).encode()).hexdigest()
    plan["StudyFingerprintSha256"] = plan["Budget"]["StudyFingerprintSha256"] = study
    # Only this just-created draft is replaced; existing destination is rejected above.
    destination.write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("corpus", type=Path)
    parser.add_argument("plan", type=Path)
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--full-union-controls", action="store_true")
    args = parser.parse_args()
    if args.workers < 1:
        parser.error("workers must be positive")
    prepare(args.corpus, args.plan, args.workers, args.full_union_controls)
