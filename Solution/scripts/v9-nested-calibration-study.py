#!/usr/bin/env python3
"""Prepare manual V9 TRAIN-adjust/selection search; no external holdout is evaluated.

The whole 19-pass union and the historical baseline are INITIAL controls.
Adaptive neighbours may add/remove passes; this is not exhaustive optimality.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

def _tf_helper():
    spec = importlib.util.spec_from_file_location(
        "v9_tf_option_grid", Path(__file__).with_name("v9-tf-option-grid.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prepare(corpus: Path, destination: Path, fixture: bool = False,
            tf_search: bool = False, pinned_ibge: Path | None = None):
    if destination.exists():
        raise FileExistsError(destination)
    manifest_path = corpus.parent / "generation-manifest.json"
    manifest = json.loads(manifest_path.read_bytes())
    if manifest["error_profile"] != "clean" or manifest["population_profile"] != "DEMOGRAPHIC_PRIMARY_V1":
        raise ValueError("Accepted provisional clean demographic bootstrap required")
    source_sha = hashlib.sha256(corpus.read_bytes()).hexdigest()
    manifest_sha = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
    spec = importlib.util.spec_from_file_location("catalogue", Path(__file__).with_name("v9-integral-search-study.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    # Distinct calibration families must never share a frozen study identity.
    # Include the generation manifest as well as the source and search mode.
    study = hashlib.sha256(("V9_INTERNAL_TRAIN_PARETO_V2|" + source_sha + "|"
                            + manifest_sha + "|" + str(fixture) + "|"
                            + str(tf_search)).encode()).hexdigest()
    catalogue = [{"PassId": key, "Fields": fields} for key, fields in sorted(module.CATALOGUE.items())]
    control = [p for p in catalogue if p["PassId"] in ["P_PHON_M_LAST", "P_LAST_M_PHON", "P_FIRST_BIRTH_YEAR"]]
    if fixture:
        catalogue = [{"PassId": "PLUMBING_PREFIX1", "Fields": ["name_prefix_1"]}]
    models = [{"OptionId": f"A{alpha}_T{threshold}_TF0", "SmoothingAlpha": alpha,
        "DecisionThreshold": threshold, "TfWeight": 0}
        for alpha in ([.5] if fixture else [.1, .5, 1]) for threshold in [.90, .95, .99]]
    if tf_search:
        if fixture:
            raise ValueError("TF search requires the real 30k study, not plumbing fixture")
        if pinned_ibge is None:
            raise ValueError("TF search requires --pinned-ibge with exact reference/digests")
        helper = _tf_helper()
        full_grid = helper.option_grid(alpha=.5, threshold=.95)
        # PO decision 12: shared surname alternatives are diagnostic only.
        diagnostic = [x for x in full_grid if x["OptionId"].startswith("TF_SHARED_PL_ML_")]
        models = [x for x in full_grid if not x["OptionId"].startswith("TF_SHARED_PL_ML_")]
        if len(models) != 41 or len(diagnostic) != 4:
            raise ValueError("Unexpected TF diagnostic partition")
        eligible = {x["OptionId"] for x in models}
        diagnostic_ids = {x["OptionId"] for x in diagnostic}
        if (len(eligible) != len(models) or len(diagnostic_ids) != len(diagnostic)
                or eligible.intersection(diagnostic_ids)):
            raise ValueError("Duplicate or overlapping TF option identifiers")
        by_id = {x["OptionId"]: x for x in models}
        diagnostic_pairs = []
        for item in diagnostic:
            baseline_id = item["OptionId"].replace("TF_SHARED_PL_ML_", "TF_TWO_PL_ML_", 1)
            if baseline_id not in eligible:
                raise ValueError("Missing bilateral reference")
            baseline = by_id[baseline_id]
            if (item["UnitTfOptions"] != baseline["UnitTfOptions"]
                    or item["SmoothingAlpha"] != baseline["SmoothingAlpha"]
                    or item["DecisionThreshold"] != baseline["DecisionThreshold"]):
                raise ValueError("Diagnostic and bilateral TF configurations must match")
            if item.get("SharedSurnameMode") != 1 or baseline.get("SharedSurnameMode", 0) != 0:
                raise ValueError("Diagnostic and bilateral TF modes must differ only by shared surname")
            # Both options must evaluate the identical candidate set; a
            # separate physical executor must enforce and attest that fact.
            diagnostic_pairs.append({"DiagnosticOptionId": item["OptionId"],
                                     "BilateralOptionId": baseline_id,
                                     "EligibleForAutomaticSelection": False,
                                     "RequiresSameCandidatePairs": True})
        if len({pair["BilateralOptionId"] for pair in diagnostic_pairs}) != 4:
            raise ValueError("Diagnostic TF options do not have unique bilateral controls")
        pinned = helper.pinned_reference(json.loads(pinned_ibge.read_bytes()))
        # The published IBGE version, both digests and the bootstrap provenance
        # influence the TRAIN TF fit. Never reuse a frozen study identity when
        # any of these inputs change, even if the synthetic corpus is identical.
        # Canonical JSON avoids accidental changes from manifest key order.
        pinned_identity = json.dumps(pinned, sort_keys=True, separators=(",", ":"))
        study = hashlib.sha256(("V9_INTERNAL_TRAIN_PARETO_V3|" + study + "|"
                                + pinned_identity).encode()).hexdigest()
    else:
        pinned = None
    # PO decision 12: 41 eligible models x two margins x two initial rule
    # sets = 164 initial selection comparisons. Four shared-surname variants
    # are diagnostics only, excluded from ModelOptions and this budget.
    # Further adaptive refinement is bounded to 512 including initial set.
    maximum = 24 if fixture else (512 if tf_search else 1000000)
    calibration = dict(StudyFingerprintSha256=study,
        Discovery=dict(MaximumPrefixLength=4), ModelOptions=models, ConflictMargins=[.5, 2],
        Budget=dict(Version="ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1", StudyFingerprintSha256=study,
            MinimumGlobalAcceptedRecall=0, MaximumObservedAcceptedFdrPoint=.01 if fixture else 1,
            MaximumBlockingFalseDegreeP99=10000 if fixture else 100, MinimumValidationIdentityGroups=2 if fixture else 1000),
        SelectionPolicy=dict(MinimumGlobalRecall=0, MaximumAcceptedFdrUpper=.01 if fixture else 1,
            MinimumIndependentIdentityGroups=2 if fixture else 1000, MaximumP95Milliseconds=60000),
        MaximumPhysicalRuleSets=1000000, MaximumComparedConfigurations=maximum,
        Execution=dict(MaximumPrefixLength=4, EndToEndRepetitions=20, FsBlindGroupingOnly=True,
            MaximumBlindClusterSize=16, MaximumCandidatePairIncidences=20000000, MaximumProjectedSignatures=5000000),
        DeclaredSearchSpace=dict(Catalogue=catalogue, MinimumPasses=len(catalogue), MaximumPasses=len(catalogue)),
        BaselineRuleSets=[] if fixture else [control], MaximumParallelRuleSets=2,
        MaximumAcceptedPerBaseIdentity=4, MinimumGroupingPosterior=.99,
        RiskCeilings=[.001, .0025, .005, .01, .02, .05],
        ObservationCapDeclaration=dict(ContractVersion=manifest["observation_multiplicity"]["contract_version"],
            GeneratorVersion=manifest["generator_version"], MaximumObservationsPerBaseIdentity=4,
            ManifestSha256=manifest_sha, ObservationsSha256=source_sha))
    plan = dict(Calibration=calibration, SplitSeed="JORNADA_V9_INTERNAL_70_30_20261019_V1",
        MaximumBatches=3 if fixture else 32, MaximumSearchConfigurations=maximum,
        BootstrapRepetitions=200 if fixture else 2000, BootstrapSeed=20261019,
        MinimumObservedMPerCell=0 if fixture else 5, MinimumObservedUPerCell=0 if fixture else 5,
        ClassMassCoverage=.99, SupportedTailRule="RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2",
        InitialThresholdStep=.025, InitialMarginStep=.25, AdjustmentFraction=.7,
        MinimumRecallImprovement=.0001, MinimumFdrImprovement=.00001,
        Purpose="PLUMBING_FIXTURE_NOT_CERTIFICATION" if fixture else "BOOTSTRAP_CANDIDATE_SELECTION")
    if tf_search:
        plan.update(pinned)
        # No subset selected from VALIDATION. Candidate neighbours continue
        # using the same 19-pass TRAIN catalogue and existing C# Pareto.
        calibration["MaximumComparedConfigurations"] = maximum
        calibration["MaximumPhysicalRuleSets"] = 2
        cost = helper.budget_estimate(models, rulesets=2,
                                     margins=len(calibration["ConflictMargins"]))
        cost["diagnostic_comparison_only"] = diagnostic_pairs
        cost["diagnostic_models_excluded_from_selection"] = len(diagnostic)
        cost["initial_grid_total"] = len(full_grid)
        cost["diagnostic_physically_executed"] = False
        cost["shared_surname_candidate_pairs"] = None
        if cost["predeclared_comparisons"] > maximum:
            raise ValueError("TF initial family exceeds the predeclared compute budget")
        sidecar = destination.with_suffix(".tf-budget.json")
        if sidecar.exists():
            raise FileExistsError(sidecar)
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(plan, stream, indent=2)
        stream.write("\n")
    if tf_search:
        with sidecar.open("x", encoding="utf-8") as stream:
            json.dump(cost, stream, indent=2)
            stream.write("\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("corpus", type=Path)
    parser.add_argument("new_plan", type=Path)
    parser.add_argument("--plumbing-fixture", action="store_true")
    parser.add_argument("--tf-search", action="store_true",
                        help="41 eligible TF configurations; four shared-surname variants are diagnostics only (manual 30k)")
    parser.add_argument("--pinned-ibge", type=Path,
                        help="Existing pinned SQL IBGE version, digests and bootstrap JSON")
    args = parser.parse_args()
    if args.pinned_ibge is not None and not args.tf_search:
        parser.error("--pinned-ibge requires --tf-search")
    prepare(args.corpus, args.new_plan, args.plumbing_fixture,
            tf_search=args.tf_search, pinned_ibge=args.pinned_ibge)
