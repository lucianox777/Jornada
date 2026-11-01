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
    study = hashlib.sha256(("V9_INTERNAL_TRAIN_PARETO_V1|" + source_sha + "|" + str(fixture)).encode()).hexdigest()
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
        models = helper.option_grid(alpha=.5, threshold=.95)
        pinned = helper.pinned_reference(json.loads(pinned_ibge.read_bytes()))
    else:
        pinned = None
    # Predeclared stage 1+2 and shared surname: 45 options × two margins ×
    # two initial rule sets (19-pass union and baseline) = 180 comparisons.
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
                        help="45 predeclared TF unit combinations (manual 30k only)")
    parser.add_argument("--pinned-ibge", type=Path,
                        help="Existing pinned SQL IBGE version, digests and bootstrap JSON")
    args = parser.parse_args()
    if args.pinned_ibge is not None and not args.tf_search:
        parser.error("--pinned-ibge requires --tf-search")
    prepare(args.corpus, args.new_plan, args.plumbing_fixture,
            tf_search=args.tf_search, pinned_ibge=args.pinned_ibge)
