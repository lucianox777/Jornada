#!/usr/bin/env python3
"""Reproduce the historic V2 calibration using only its frozen, Git-versioned plan.

This is a preflight/analysis CLI, NOT a calibration engine. The single C#
Calibrador reads the returned plan verbatim with --v9-adaptive-study. Never
loads TEST, changes model status, performs SQL, or starts a 30k run itself.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

SCHEMA = "V9_V2_FROZEN_CONFIGURED_REPLAY_V1"
COMPARISON_SCHEMA = "V9_V2_CONFIGURED_REPLAY_REGRESSION_V1"
MODEL_KEY = "V2_ONE_MF_original"
PIN_KEYS = ("PinnedIbgeReferenceVersionId", "PinnedIbgePublishedSha256",
            "PinnedIbgeMaterializedFrequencySha256", "PinnedIbgeBootstrap")


def digest(path):
    hasher = hashlib.sha256()
    with Path(path).open("rb") as source:
        for data in iter(lambda: source.read(1 << 20), b""):
            hasher.update(data)
    return hasher.hexdigest()


def fail(msg):
    raise ValueError(msg)


def check(value, msg):
    if not value:
        fail(msg)


def read_json(path):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                fail("Duplicate JSON key: " + key)
            result[key] = value
        return result
    return json.loads(Path(path).read_text(encoding="utf-8"),
                      object_pairs_hook=pairs,
                      parse_constant=lambda value: fail("Invalid nonfinite: " + value))


def same_fingerprint(a, b):
    return isinstance(a, str) and isinstance(b, str) and a.lower() == b.lower()


def source_identity(references, plan_path):
    doc = read_json(references)
    check(doc.get("schema_version") == "V9_FROZEN_ARTIFACT_REFERENCES_V1",
          "Unknown external source reference")
    art = doc["artifacts"]["seed42-source"]
    files = {x["path"]: x["sha256"] for x in art["files"]}
    check(digest(plan_path) == files["plan-tf.json"],
          "Versioned user calibration parameters differ from frozen V2")
    selected = doc["models"][MODEL_KEY]
    check(files["study-tf/" + selected + ".fitted.json"] == selected,
          "V2 original fitted snapshot missing in reference")
    check(type(art.get("run_id")) is int and len(art.get("head_sha", "")) == 40,
          "Original run or code identity invalid")
    return doc, art, selected, files


def inspect_plan(plan, model_sha, **unused):
    check(isinstance(plan, dict) and isinstance(plan.get("ModelOptions"), list)
          and plan["ModelOptions"], "Missing declared exploration options")
    check(isinstance(plan.get("ConflictMargins"), list) and plan["ConflictMargins"],
          "Missing exploration margins")
    check(isinstance(plan.get("BaselineRuleSets"), list) and plan["BaselineRuleSets"],
          "Missing declared blocking")
    for f in ("StudyFingerprintSha256", "Budget", "SelectionPolicy",
              "Discovery", "Execution", "ObservationCapDeclaration",
              "MaximumComparedConfigurations", "PreviouslyComparedValidationConfigurations",
              "MaximumAcceptedPerBaseIdentity"):
        check(f in plan, "Missing calibration user parameter: " + f)
    for f in PIN_KEYS:
        check(f in plan, "Missing frozen reference field: " + f)
    check(isinstance(plan["PinnedIbgeBootstrap"], dict),
          "Missing pinned IBGE bootstrap object")
    options = plan["ModelOptions"]
    ids = [x.get("OptionId") for x in options if isinstance(x, dict)]
    check(len(ids) == len(options) and len(set(ids)) == len(ids),
          "Duplicate or invalid option identifiers")
    check(any(x.get("EligibleForSelection") is True for x in options),
          "No selectable option")
    check(all(isinstance(m, (float, int)) and not isinstance(m, bool)
              and math.isfinite(m) and m > 0 for m in plan["ConflictMargins"]),
          "Invalid declared conflict margin")
    check(all(isinstance(x, dict) and
              all(f in x for f in ("OptionId", "DecisionThreshold",
                                   "SmoothingAlpha", "TfWeight")) for x in options),
          "Missing explored FS option attributes")
    check(all(0 < x["DecisionThreshold"] < 1
              and x["SmoothingAlpha"] > 0 for x in options),
          "Invalid exploration option")
    check(model_sha and len(model_sha) == 64, "Original V2 snapshot pin missing")
    return {"modelOptions": len(options), "conflictMargins": len(plan["ConflictMargins"]),
            "exploredConfigurations": plan["MaximumComparedConfigurations"],
            "hasTF": any(bool(x.get("UnitTfOptions")) for x in options)}


def compare_pins(plan, actual):
    check(isinstance(actual, dict) and set(actual) == set(PIN_KEYS),
          "Regenerated SQL IBGE pin has unexpected fields")
    for key in PIN_KEYS:
        old, new = plan[key], actual[key]
        if key in ("PinnedIbgePublishedSha256", "PinnedIbgeMaterializedFrequencySha256"):
            check(same_fingerprint(old, new), "IBGE digest differs from frozen V2: " + key)
        else:
            check(old == new, "IBGE bootstrap/provenance differs from frozen V2: " + key)


# Only field NAMES live in source. The exact values of every formerly implicit
# runtime option live in the Git-versioned plan-effective.json. This list
# comes from record defaults at the original run's exact C# commit.
ALLOWED_EXPLICIT_DEFAULT_FIELDS = {
    (): {"MaximumParallelRuleSets", "RiskCeilings", "SampleDesign",
         "CollapseEquivalentCandidateFrames", "ShardIndex", "ShardCount"},
    ("Discovery",): {"PositivePairCap", "PairSamplingAttemptCap"},
    ("Discovery", "SearchOptions"): {"RequireExhaustivePrimitivePool"},
    ("Execution",): {"TotalComparedConfigurations"},
    ("ModelOptions", "*"): {"TfSide", "TfMinimumFrequency",
                            "TfMinimumIndependentSupport", "JointSupportPolicy",
                            "UnitTfOptions", "SharedSurnameMode",
                            "UnpublishedTermPolicy"},
}


def validate_effective_plan(original, effective):
    """Only materialize omitted C# defaults. Never modify an original value."""
    additions = {}

    def walk(old, new, path):
        if isinstance(old, dict):
            check(isinstance(new, dict), "Changed original plan object: " + str(path))
            check(set(old).issubset(new), "Original calibration parameter removed: " + str(path))
            allowed = ALLOWED_EXPLICIT_DEFAULT_FIELDS.get(path, set())
            extra = set(new) - set(old)
            check(extra == allowed - set(old),
                  "Missing or unapproved explicit default at " + str(path))
            if extra:
                additions[".".join(path)] = sorted(extra)
            for k in old:
                walk(old[k], new[k], path + (k,))
        elif isinstance(old, list):
            check(isinstance(new, list) and len(old) == len(new),
                  "Original calibration option or order changed: " + str(path))
            for before, after in zip(old, new):
                walk(before, after, path + ("*",))
        else:
            check(type(old) is type(new) and old == new,
                  "Original V2 calibrated value was modified at " + str(path))

    walk(original, effective, ())
    return additions


def prepare(plan_path, references, observations, generation_manifest,
            ibge_pin, destination, receipt, effective_plan_path=None):
    check(not destination.exists() and not receipt.exists(),
          "Refusing to overwrite existing parameter plan or receipt")
    doc, art, selected, files = source_identity(references, plan_path)
    plan = read_json(plan_path)
    attr = inspect_plan(plan, selected)
    effective = read_json(effective_plan_path) if effective_plan_path else None
    additions = validate_effective_plan(plan, effective) if effective is not None else {}
    effective_sha = digest(effective_plan_path) if effective_plan_path else None
    observation_sha = digest(observations)
    generation_sha = digest(generation_manifest)
    declared = plan["ObservationCapDeclaration"]
    check(same_fingerprint(observation_sha, declared.get("ObservationsSha256")),
          "Regenerated corpus does not match frozen V2 observations: refuse calibration")
    check(same_fingerprint(generation_sha, declared.get("ManifestSha256")),
          "Regenerated generation-manifest differs from frozen V2: refuse calibration")
    compare_pins(plan, read_json(ibge_pin))
    # Source of all numerical values is the declared plan file. Exact-original
    # mode preserves original bytes; explicit mode uses independently versioned
    # historical defaults without rewriting the original archived plan.
    destination.parent.mkdir(parents=True, exist_ok=True)
    receipt.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("xb") as handle:
        handle.write((effective_plan_path or plan_path).read_bytes())
    check(digest(destination) == (effective_sha or files["plan-tf.json"]),
          "Copied user configuration differs from Git-versioned source")
    record = {
        "SchemaVersion": SCHEMA,
        "OriginalRunId": art["run_id"],
        "OriginalCodeSha": art["head_sha"],
        "SourceArtifactName": art["name"],
        "PlanSha256": digest(destination),
        "OriginalPlanSha256": files["plan-tf.json"],
        "EffectivePlanSha256": effective_sha,
        "DefaultFieldsMaterialized": additions,
        "SourceStudySha256": files["study-tf/study.json"],
        "OriginalFittedSnapshotSha256": selected,
        "OriginalObservationsSha256": observation_sha,
        "OriginalGenerationManifestSha256": generation_sha,
        "PinnedIbgeReferenceVersionId": plan["PinnedIbgeReferenceVersionId"],
        "ExplorationDeclaredInPlan": attr,
        "ConfigurationSource": ("GIT_VERSIONED_V2_EXPLICIT_HISTORICAL_DEFAULTS"
                                if effective_plan_path else
                                "GIT_VERSIONED_FROZEN_V2_PLAN_EXACT_BYTES"),
        "TestUsed": False,
        "Calibrated": False, "Certified": False, "Activated": False,
    }
    with receipt.open("x", encoding="utf-8") as out:
        json.dump(record, out, indent=2)
        out.write("\n")
    return record


def strip_ruleset(option_id):
    if not isinstance(option_id, str):
        fail("Invalid OptionId")
    return option_id[65:] if len(option_id) > 65 and option_id[64] == ":" else option_id


def key(row):
    check(isinstance(row, dict), "Malformed study option")
    return (row["OptionId"], row["ConflictMargin"])


def source_metrics(study):
    rows = study.get("SourceMeasurements")
    check(isinstance(rows, list) and rows, "Empty physical comparison")
    mapping = {key(x): x for x in rows}
    check(len(mapping) == len(rows), "Duplicate source metrics")
    return mapping


def compare(references, source_study_path, candidate_study_path, receipt_path, out):
    check(not out.exists(), "Refusing to replace prior regression report")
    doc = read_json(references)
    files = {x["path"]: x["sha256"]
             for x in doc["artifacts"]["seed42-source"]["files"]}
    check(digest(source_study_path) == files["study-tf/study.json"],
          "Historical study differs from source run SHA")
    receipt = read_json(receipt_path)
    check(receipt.get("SchemaVersion") == SCHEMA
          and receipt.get("OriginalRunId") == doc["artifacts"]["seed42-source"]["run_id"]
          and receipt.get("SourceStudySha256") == files["study-tf/study.json"],
          "Unbound calibration replay receipt")
    old, new = read_json(source_study_path), read_json(candidate_study_path)
    for field in ("Version", "PlanSha256", "TrainInputSha256",
                  "ValidationInputSha256", "PredeclaredComparedConfigurations"):
        check(old.get(field) == new.get(field),
              "Regressed experiment identity: " + field)
    check(old.get("SourceSelection", {}).get("TestUsed") is False
          and new.get("SourceSelection", {}).get("TestUsed") is False,
          "TEST touched in calibration")
    old_rows, new_rows = source_metrics(old), source_metrics(new)
    check(old_rows.keys() == new_rows.keys(),
          "Replayed configuration grid differs from frozen V2")
    fields = ("EligibleSourceObservationsBeforeBlocking",
              "TrueAcceptedSourceObservations", "FalseAcceptedSourceObservations",
              "GlobalSourceRecall", "FdrAfterResolve",
              "CandidateGrouping", "Conflicts", "Unresolved")
    changes = []
    for k in sorted(old_rows):
        changed = [f for f in fields if old_rows[k].get(f) != new_rows[k].get(f)]
        if changed:
            changes.append({"OptionId": k[0], "ConflictMargin": k[1],
                            "ChangedFields": changed,
                            "PreviousTp": old_rows[k].get("TrueAcceptedSourceObservations"),
                            "CurrentTp": new_rows[k].get("TrueAcceptedSourceObservations"),
                            "PreviousFp": old_rows[k].get("FalseAcceptedSourceObservations"),
                            "CurrentFp": new_rows[k].get("FalseAcceptedSourceObservations")})
    original_fit_sha = doc["models"][MODEL_KEY]
    def fit_arms(study):
        configs = study.get("FrozenConfigurations")
        check(isinstance(configs, list), "No frozen fitted study configurations")
        result = {}
        for config in configs:
            ident = key(config)
            check(ident not in result and isinstance(config.get("Snapshot"), dict),
                  "Duplicate or missing fitted configuration")
            result[ident] = config["Snapshot"].get("ContentSha256")
        return result
    prev_fit, next_fit = fit_arms(old), fit_arms(new)
    check(prev_fit.keys() == next_fit.keys(), "Fit keys changed on configured V2 replay")
    fit_diff = [str(k) for k in sorted(prev_fit) if prev_fit[k] != next_fit[k]]
    original_selected = [k for k, value in prev_fit.items() if value == original_fit_sha]
    check(len(original_selected) == 1, "Original V2 ONE_MF fit not unique")
    selected_ident = original_selected[0]
    result = {
        "SchemaVersion": COMPARISON_SCHEMA,
        "OriginalRunId": receipt["OriginalRunId"],
        "OriginalStudySha256": digest(source_study_path),
        "ReplayStudySha256": digest(candidate_study_path),
        "OriginalPlanSha256": receipt["PlanSha256"],
        "OriginalSnapshotSha256": original_fit_sha,
        "ReplaySelectedSnapshotSha256": next_fit[selected_ident],
        "SelectedSnapshotExactMatch": next_fit[selected_ident] == original_fit_sha,
        "SameConfigurationGrid": True,
        "SameCorpusAndPartitions": True,
        "ComparedSourceConfigurations": len(old_rows),
        "DifferentMetricRows": len(changes),
        "DifferentFitSnapshots": len(fit_diff),
        "Changes": changes[:80],
        "ChangesTruncated": len(changes) > 80,
        "Status": ("REPRODUCED_EXACTLY"
                   if not changes and not fit_diff else "REGRESSION_DETECTED_REVIEW_REQUIRED"),
        "TestUsed": False, "StatisticallyCertified": False,
        "OperationallyActivated": False,
        "Caveat": "Aggregate equality does not establish municipal generalization or causal improvement.",
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("x", encoding="utf-8") as dst:
        json.dump(result, dst, indent=2)
        dst.write("\n")
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="command", required=True)
    a = sub.add_parser("prepare")
    for arg in ("plan", "references", "observations", "generation-manifest",
                "pinned-ibge", "output-plan", "output-receipt"):
        a.add_argument("--" + arg, required=True, type=Path)
    a.add_argument("--effective-plan", type=Path,
                   help="Explicit historical defaults; source plan remains SHA-frozen")
    b = sub.add_parser("compare")
    for arg in ("references", "original-study", "replay-study",
                "receipt", "output"):
        b.add_argument("--" + arg, required=True, type=Path)
    args = p.parse_args()
    try:
        if args.command == "prepare":
            print(json.dumps(prepare(args.plan, args.references, args.observations,
                                     args.generation_manifest, args.pinned_ibge,
                                     args.output_plan, args.output_receipt,
                                     getattr(args, "effective_plan", None))))
        else:
            print(json.dumps(compare(args.references, args.original_study,
                                     args.replay_study, args.receipt, args.output)))
    except (ValueError, KeyError, TypeError, OSError, json.JSONDecodeError) as e:
        p.exit(2, "FAIL_CLOSED: " + str(e) + "\n")


if __name__ == "__main__":
    main()
