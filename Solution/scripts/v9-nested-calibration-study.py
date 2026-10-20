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


def prepare(corpus: Path, destination: Path, fixture: bool = False):
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
    maximum = 24 if fixture else 1000000
    calibration = dict(StudyFingerprintSha256=study,
        Discovery=dict(MaximumPrefixLength=4), ModelOptions=models, ConflictMargins=[.5, 2],
        Budget=dict(Version="ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1", StudyFingerprintSha256=study,
            MinimumGlobalAcceptedRecall=0 if fixture else .95, MaximumObservedAcceptedFdrPoint=.01,
            MaximumBlockingFalseDegreeP99=10000 if fixture else 100, MinimumValidationIdentityGroups=2 if fixture else 1000),
        SelectionPolicy=dict(MinimumGlobalRecall=0 if fixture else .95, MaximumAcceptedFdrUpper=.01,
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
        MinimumObservedMPerCell=0 if fixture else 1, MinimumObservedUPerCell=0 if fixture else 1,
        InitialThresholdStep=.025, InitialMarginStep=.25, AdjustmentFraction=.7,
        MinimumRecallImprovement=.0001, MinimumFdrImprovement=.00001,
        Purpose="PLUMBING_FIXTURE_NOT_CERTIFICATION" if fixture else "BOOTSTRAP_CANDIDATE_SELECTION")
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(plan, stream, indent=2)
        stream.write("\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("corpus", type=Path)
    parser.add_argument("new_plan", type=Path)
    parser.add_argument("--plumbing-fixture", action="store_true")
    args = parser.parse_args()
    prepare(args.corpus, args.new_plan, args.plumbing_fixture)
