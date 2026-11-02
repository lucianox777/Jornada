#!/usr/bin/env python3
"""Predeclared TRAIN/VALIDATION-only grid on the historical 30k corpus. Never TEST/certification."""
import argparse
import hashlib
import json
import math
import importlib.util
from pathlib import Path


def _tf_helper():
    spec = importlib.util.spec_from_file_location(
        "v9_tf_option_grid", Path(__file__).with_name("v9-tf-option-grid.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prepare(corpus: Path, plan_path: Path, cap_four: bool = False,
            tf_search: bool = False, pinned_ibge: Path | None = None) -> None:
    if pinned_ibge is not None and not tf_search:
        raise ValueError("Pinned IBGE reference requires TF search")
    manifest = json.loads((corpus.parent / "generation-manifest.json").read_bytes())
    # Assertions disappear under python -O: provenance gates must not.
    if (not isinstance(manifest, dict) or type(manifest.get("seed")) is not int
            or manifest["seed"] != 42
            or manifest.get("population_profile") != "DEMOGRAPHIC_PRIMARY_V1"
            or manifest.get("error_profile") != "clean"):
        raise ValueError("Invalid V9 TRAIN generation manifest")
    input_sha = hashlib.sha256(corpus.read_bytes()).hexdigest()
    study = hashlib.sha256(("V9_ADAPTIVE_30K_TRAIN_ONLY_PROPOSALS_WITH_BASELINE_GRID_V2|" + input_sha).encode()).hexdigest()
    plan = {
        "StudyFingerprintSha256": study,
        "Discovery": {"MaximumPrefixLength": 4, "NonMatchSampleSize": 100000,
                      "NonMatchSampleSeed": 20261019,
                      "SearchOptions": {"MaxFieldsPerPass": 2, "MaxPasses": 2,
                                        "PrimitivePoolSize": 16, "MinimumTrueMatchRecall": 0.95,
                                        "RequireObservedNonMatchSupport": True,
                                        "CandidateRuleSetCap": 100000}},
        "ModelOptions": [{"OptionId": f"T{threshold}_TF0", "SmoothingAlpha": 0.5,
                          "DecisionThreshold": threshold, "TfWeight": 0}
                         for threshold in [0.90, 0.95, 0.99]],
        "ConflictMargins": [0.5, 2, 4],
        "Budget": {"Version": "ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1", "StudyFingerprintSha256": study,
                   "MinimumGlobalAcceptedRecall": 0.95, "MaximumObservedAcceptedFdrPoint": 0.01,
                   "MaximumBlockingFalseDegreeP99": 100, "MinimumValidationIdentityGroups": 1000},
        "SelectionPolicy": {"MinimumGlobalRecall": 0.95, "MaximumAcceptedFdrUpper": 0.01,
                            "MinimumIndependentIdentityGroups": 1000, "MaximumP95Milliseconds": 60000},
        "MaximumPhysicalRuleSets": 137, "MaximumComparedConfigurations": 2493,
        "BaselineRuleSets": [[
            {"PassId": "P_PHON_M_LAST", "Fields": ["name_phonetic_ptbr", "mother_name_last"]},
            {"PassId": "P_LAST_M_PHON", "Fields": ["name_last", "mother_name_phonetic_ptbr"]},
            {"PassId": "P_FIRST_BIRTH_YEAR", "Fields": ["name_first", "birth_year"]}
        ]],
        "PreviouslyComparedValidationConfigurations": 1260,
        "Execution": {"MaximumPrefixLength": 4, "EndToEndRepetitions": 0,
                      "FsBlindGroupingOnly": True, "MaximumBlindClusterSize": 16,
                      "MaximumCandidatePairIncidences": 1000000, "MaximumProjectedSignatures": 1000000},
        "MinimumGroupingPosterior": 0.99, "MaximumAcceptedPerBaseIdentity": 16
    }
    if cap_four:
        multiplicity = manifest["observation_multiplicity"]
        if (not isinstance(multiplicity, dict)
                or manifest.get("generator_version") != "JORNADA_SYNTH_CORPUS_CSHARP_V1"
                or multiplicity.get("contract_version") != "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1"
                or type(multiplicity.get("maximum_observations_per_base_identity")) is not int
                or multiplicity["maximum_observations_per_base_identity"] != 4
                or multiplicity.get("scope") != "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES"):
            raise ValueError("Invalid V9 TRAIN cap-four multiplicity manifest")
        manifest_sha = hashlib.sha256((corpus.parent / "generation-manifest.json").read_bytes()).hexdigest()
        study = hashlib.sha256(("V9_ADAPTIVE_30K_BASE_CAP4_V3|" + input_sha + "|" + manifest_sha).encode()).hexdigest()
        plan["StudyFingerprintSha256"] = plan["Budget"]["StudyFingerprintSha256"] = study
        # Carry the entire previous family; do not reset multiplicity on a new bound protocol.
        plan["PreviouslyComparedValidationConfigurations"] = 2493
        plan["MaximumComparedConfigurations"] = 3726
        plan["MaximumAcceptedPerBaseIdentity"] = 4
        plan["ObservationCapDeclaration"] = {
            "ContractVersion": multiplicity["contract_version"],
            "GeneratorVersion": manifest["generator_version"],
            "MaximumObservationsPerBaseIdentity": 4,
            "ManifestSha256": manifest_sha, "ObservationsSha256": input_sha
        }
        # Source population cap differs from the blind candidate size guard (16).
    if tf_search:
        if not cap_four or pinned_ibge is None:
            raise ValueError("TF physical study requires cap4 and --pinned-ibge")
        helper = _tf_helper()
        full_grid = helper.option_grid(alpha=.5, threshold=.95)
        full_grid[0]["OptionId"] = "T0.95_TF0"  # historical TF0 control ID
        diagnostic = [x for x in full_grid if x["OptionId"].startswith("TF_SHARED_PL_ML_")]
        grid = [x for x in full_grid if not x["OptionId"].startswith("TF_SHARED_PL_ML_")]
        if len(grid) != 41 or len(diagnostic) != 4:
            raise ValueError("Unexpected adaptive TF diagnostic partition")
        by_id = {x["OptionId"]: x for x in grid}
        diagnostic_pairs = []
        for item in diagnostic:
            baseline_id = item["OptionId"].replace("TF_SHARED_PL_ML_", "TF_TWO_PL_ML_", 1)
            baseline = by_id.get(baseline_id)
            if baseline is None or (item["UnitTfOptions"] != baseline["UnitTfOptions"]
                    or item["SmoothingAlpha"] != baseline["SmoothingAlpha"]
                    or item["DecisionThreshold"] != baseline["DecisionThreshold"]
                    or item.get("SharedSurnameMode") != 1
                    or baseline.get("SharedSurnameMode", 0) != 0):
                raise ValueError("Shared diagnostic must match bilateral TF parameters")
            diagnostic_pairs.append({"DiagnosticOptionId": item["OptionId"],
                                     "BilateralOptionId": baseline_id,
                                     "EligibleForAutomaticSelection": False,
                                     "RequiresSameCandidatePairs": True})
        plan["ModelOptions"] = grid
        # The exploratory search must not pre-exclude a risk region before
        # TRAIN evidence: Pareto/frontier is descriptive, gates come later.
        plan["Budget"]["MinimumGlobalAcceptedRecall"] = 0
        plan["Budget"]["MaximumObservedAcceptedFdrPoint"] = 1
        plan["SelectionPolicy"]["MinimumGlobalRecall"] = 0
        plan["SelectionPolicy"]["MaximumAcceptedFdrUpper"] = 1
        # Isolate the TF comparison to ONE predeclared, unchanged 3-pass
        # SQL blocking control; no 137 × 41 grid or TEST-driven pruning.
        base_passes = plan["BaselineRuleSets"][0]
        plan["DeclaredSearchSpace"] = {
            "Catalogue": base_passes, "MinimumPasses": len(base_passes),
            "MaximumPasses": len(base_passes)
        }
        plan["MaximumPhysicalRuleSets"] = 1
        plan["PreviouslyComparedValidationConfigurations"] = 3726
        cost = helper.budget_estimate(grid, rulesets=1,
            margins=len(plan["ConflictMargins"]), previous=3726)
        plan["MaximumComparedConfigurations"] = cost["predeclared_comparisons"]
        cost["diagnostic_comparison_only"] = diagnostic_pairs
        cost["diagnostic_models_excluded_from_selection"] = len(diagnostic)
        cost["initial_grid_total"] = len(full_grid)
        cost["diagnostic_physically_executed"] = False
        cost["shared_surname_candidate_pairs"] = None
        plan.update(helper.pinned_reference(json.loads(pinned_ibge.read_bytes())))
        sidecar = plan_path.with_suffix(".tf-budget.json")
        if sidecar.exists():
            raise FileExistsError(sidecar)
    with plan_path.open("x", encoding="utf-8") as stream:
        json.dump(plan, stream, indent=2)
        stream.write("\n")
    if tf_search:
        with sidecar.open("x", encoding="utf-8") as stream:
            json.dump(cost, stream, indent=2)
            stream.write("\n")


def verify(root: Path) -> None:
    # This historical diagnostic verifier uses assertions throughout. Refuse
    # optimized Python rather than silently skipping every evidence gate.
    if not __debug__:
        raise RuntimeError("V9 evidence verification disabled by optimized Python")
    result = json.loads((root / "study.json").read_bytes())
    plan = json.loads((root / "plan.json").read_bytes())
    comparisons = plan["MaximumComparedConfigurations"]
    assert (plan["MaximumAcceptedPerBaseIdentity"], comparisons) in [(16, 2493), (4, 3726)]
    if plan["MaximumAcceptedPerBaseIdentity"] == 4:
        assert plan["ObservationCapDeclaration"]["MaximumObservationsPerBaseIdentity"] == 4
    assert result["PredeclaredComparedConfigurations"] == comparisons
    assert result["TotalCandidateRuleSets"] == 137
    assert result["Discovery"]["EnumeratedUnionCount"] == 136
    assert result["Discovery"]["Options"]["NonMatchSampleSize"] == 100000
    assert len(result["Pareto"]["Table"]) == 1233
    assert result["Pareto"]["InitialSuggestedOptionId"] is None
    assert all(not row["Eligible"] for row in result["Pareto"]["Table"])
    assert all(row["ExclusionReasons"] for row in result["Pareto"]["Table"])
    assert result["EndToEndMeasurements"] == []
    rows = result["SourceMeasurements"]
    assert rows and all(row["BonferroniComparedConfigurations"] == comparisons for row in rows)
    assert all(row["CandidateGrouping"] == "BLIND_FS_COMPLETE_LINK_CANDIDATES" for row in rows)
    assert all(not row["ClusterIidIndependentlyValidated"] and not row["OperationallyCertified"] for row in rows)
    assert result["SourceSelection"]["TestUsed"] is False
    assert len(rows) + sum(len(item["ConfigurationIds"]) for item in result["Rejections"]) == 1233
    controls = [fit["OptionId"] for fit in result["FrozenConfigurations"]
                if {p["PassId"] for p in fit["Snapshot"]["Payload"]["BlockingPasses"]}
                == {"P_PHON_M_LAST", "P_LAST_M_PHON", "P_FIRST_BIRTH_YEAR"}]
    assert controls, "The known reference ruleset was silently excluded from the candidate family"
    reference = json.loads((root.parent / "physical-fs-resolve-diagnostic.json").read_bytes())["source_resolve_options"]
    control_rows = [row for row in rows if row["OptionId"] in controls]
    assert len(control_rows) == 9
    if plan["MaximumAcceptedPerBaseIdentity"] == 4:
        assert any(item["Snapshot"]["ContentSha256"] == "613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d"
                   for item in result["FrozenConfigurations"]), "Cap-only protocol must preserve the frozen v0 fit"
    for row in control_rows:
        if row["ConflictMargin"] not in [0.5, 2]:
            continue
        old = next(item for item in reference if item["Threshold"] == row["Threshold"]
                   and item["ConflictMargin"] == row["ConflictMargin"]
                   and item["CandidateGrouping"] == row["CandidateGrouping"])
        for field in ["AllValidationObservations", "EligibleSourceObservationsBeforeBlocking",
                      "EligibleSourceObservationsWithTrueSqlCandidate", "TrueAcceptedSourceObservations",
                      "FalseAcceptedSourceObservations", "Conflicts", "Unresolved", "ReasonsByResolveStatus"]:
            assert row[field] == old[field], f"Reference control drift: {field}"
    assert all(row["TrueAcceptedSourceObservations"] + row["FalseAcceptedSourceObservations"]
               + row["Conflicts"] + row["Unresolved"] == row["AllValidationObservations"] for row in rows)
    assert all(abs(row["GlobalSourceRecall"] - row["TrueAcceptedSourceObservations"]
                   / row["EligibleSourceObservationsBeforeBlocking"]) < 1e-12 for row in rows)
    for row in rows:
        accepted = row["TrueAcceptedSourceObservations"] + row["FalseAcceptedSourceObservations"]
        assert (row["FdrAfterResolve"] is None) == (accepted == 0)
        if accepted:
            assert abs(row["FdrAfterResolve"] - row["FalseAcceptedSourceObservations"] / accepted) < 1e-12
        if row["FalseAcceptedSourceObservations"] == 0:
            n = row["PreBlockingBaseIdentities"]
            cap = plan["MaximumAcceptedPerBaseIdentity"]
            component_alpha = .05 / (2 * comparisons)
            cp = -math.expm1(math.log(component_alpha) / n)
            denominator = accepted / n - cap * math.sqrt(math.log(1 / component_alpha) / (2 * n))
            expected = min(1, cap * cp / denominator) if denominator > 0 else None
            assert abs(row["ClopperPearsonUpperAnyFpByIdentity"] - cp) < 1e-12
            if expected is None:
                assert row["ConditionalClusterCpFdrUpper"] is None
            else:
                assert abs(row["ConditionalClusterCpFdrUpper"] - expected) < 1e-12, "Reported cap was not applied to bound"
    for fit in result["FrozenConfigurations"]:
        snapshot = fit["Snapshot"]
        assert hashlib.sha256((root / (snapshot["ContentSha256"] + ".fitted.json")).read_bytes()).hexdigest() == snapshot["ContentSha256"]
    summary = {
        "scope": "HISTORICAL_TRAIN_VALIDATION_EXPLORATORY_NOT_CERTIFICATION",
        "plan_sha256": result["PlanSha256"], "configurations": 1233,
        "evaluated_source_rows": len(rows), "rejected_rows": 1233 - len(rows),
        "multiplicity_including_previous_known_family": comparisons,
        "predeclared_population_cap": plan["MaximumAcceptedPerBaseIdentity"],
        "reference_control_included": True,
        "historical_validation_reused": True, "complete_historical_look_count_verified": False,
        "p95_measured": False, "test_used": False, "certified": False, "activated": False,
        "maximum_observed_global_source_recall": max(row["GlobalSourceRecall"] for row in rows),
        "minimum_observed_accepted_fdr": min((row["FdrAfterResolve"] for row in rows
                                               if row["FdrAfterResolve"] is not None), default=None)
    }
    with (root / "diagnostic-summary.json").open("x", encoding="utf-8") as stream:
        json.dump(summary, stream, indent=2)
    print(json.dumps(summary))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="mode", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("corpus", type=Path)
    prep.add_argument("plan", type=Path)
    prep_cap = commands.add_parser("prepare-cap4")
    prep_cap.add_argument("corpus", type=Path)
    prep_cap.add_argument("plan", type=Path)
    prep_tf = commands.add_parser("prepare-tf")
    prep_tf.add_argument("corpus", type=Path)
    prep_tf.add_argument("plan", type=Path)
    prep_tf.add_argument("--pinned-ibge", type=Path, required=True)
    check = commands.add_parser("verify")
    check.add_argument("result", type=Path)
    args = parser.parse_args()
    if args.mode in ("prepare", "prepare-cap4", "prepare-tf"):
        prepare(args.corpus, args.plan,
                cap_four=args.mode in ("prepare-cap4", "prepare-tf"),
                tf_search=args.mode == "prepare-tf",
                pinned_ibge=getattr(args, "pinned_ibge", None))
    else:
        verify(args.result)
