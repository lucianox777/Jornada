#!/usr/bin/env python3
"""Content-addressed V9 calibration evidence bundle (read-only frozen V2 input).

A calibration EVIDENCE bundle is distinct from the operational model bundle
and from config/release/configuration-bundle.json. No SQL, TEST, refit or DT-15.
Three primary files: plan.json, result.json, report.json; the fitted model is
an exact-byte attachment to the result, and manifest.json binds their hashes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import sys
import tempfile
from pathlib import Path

VERSION = "JORNADA_CALIBRATION_EVIDENCE_BUNDLE_V1"
REPORT_VERSION = "JORNADA_CALIBRATION_EVIDENCE_REPORT_V1"
STUDY_VERSION = "V9_ADAPTIVE_PHYSICAL_TRAIN_VALIDATION_PARETO_V1"
SHA64 = re.compile(r"[a-f0-9]{64}\Z")
SHA40 = re.compile(r"[a-f0-9]{40}\Z")
RESULT_FIELDS = ("PlanSha256", "TrainInputSha256", "ValidationInputSha256")
SNAPSHOT_FIELDS = (
    "SchemaVersion", "NominalContractVersion", "ProjectionSchemaVersion",
    "ProjectionFingerprintSha256", "FitFingerprintSha256",
    "TrainCorpusSha256", "ValidationCorpusSha256", "TrainIdentityGroupsSha256",
    "ValidationIdentityGroupsSha256", "AdapterVersion", "OptionId",
    "Threshold", "ConflictMargin", "BlockingPasses", "JointCells", "Parameters",
)
PLAN_FIELDS = (
    "StudyFingerprintSha256", "ModelOptions", "ConflictMargins",
    "BaselineRuleSets", "Discovery", "SelectionPolicy", "Budget", "Execution",
    "MaximumComparedConfigurations", "PreviouslyComparedValidationConfigurations",
    "MaximumAcceptedPerBaseIdentity", "ObservationCapDeclaration",
    "PinnedIbgeReferenceVersionId", "PinnedIbgePublishedSha256",
    "PinnedIbgeMaterializedFrequencySha256", "PinnedIbgeBootstrap",
)
FILE_MAP = {
    "plan": "plan.json", "result": "result.json",
    "fitted_model": "model.fitted.json", "report": "report.json",
}


def fail(message):
    raise ValueError(message)


def sha(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def strict(path):
    def pairs(items):
        output = {}
        for k, v in items:
            if k in output:
                fail("JSON duplicate key: " + k)
            output[k] = v
        return output
    return json.loads(Path(path).read_text(encoding="utf-8"),
                      object_pairs_hook=pairs,
                      parse_constant=lambda v: fail("Nonfinite JSON: " + v))


def valid_sha(value, size=64):
    regex = SHA64 if size == 64 else SHA40
    return isinstance(value, str) and regex.fullmatch(value) is not None


def require(value, message):
    if not value:
        fail(message)


def number(value, name, positive=False):
    require(type(value) in (int, float) and math.isfinite(value)
            and (value > 0 if positive else value >= 0), "Invalid " + name)
    return value


def bare_option(option):
    require(isinstance(option, str) and bool(option), "OptionId required")
    if len(option) > 65 and valid_sha(option[:64]) and option[64] == ":":
        return option[65:]
    return option


def audit_plan(plan, selected):
    require(isinstance(plan, dict), "Plan must be a JSON object")
    missing = sorted(set(PLAN_FIELDS) - set(plan))
    require(not missing, "Missing calibration-plan fields: " + ", ".join(missing))
    require(isinstance(plan["ModelOptions"], list) and bool(plan["ModelOptions"]),
            "Missing model search options")
    option_ids = [o.get("OptionId") for o in plan["ModelOptions"] if isinstance(o, dict)]
    require(len(option_ids) == len(plan["ModelOptions"])
            and len(set(option_ids)) == len(option_ids), "Invalid/duplicate plan options")
    require(selected in option_ids, "Selected V2 option missing from plan")
    options = [x for x in plan["ModelOptions"] if x["OptionId"] == selected]
    option = options[0]
    for field in ("SmoothingAlpha", "DecisionThreshold", "TfWeight"):
        require(field in option, "Selected option missing " + field)
        number(option[field], field)
    require(0 < option["DecisionThreshold"] < 1, "DecisionThreshold out of range")
    require(isinstance(plan["ConflictMargins"], list) and plan["ConflictMargins"],
            "ConflictMargins must be explicit")
    require(isinstance(plan["BaselineRuleSets"], list) and plan["BaselineRuleSets"],
            "Blocking rules must be declared")
    policy = plan["SelectionPolicy"]
    budget = plan["Budget"]
    execution = plan["Execution"]
    for obj, fields, context in (
        (policy, ("MinimumGlobalRecall", "MaximumAcceptedFdrUpper",
                  "MinimumIndependentIdentityGroups", "MaximumP95Milliseconds"), "SelectionPolicy"),
        (budget, ("MinimumGlobalAcceptedRecall", "MaximumObservedAcceptedFdrPoint",
                  "MinimumValidationIdentityGroups"), "Budget"),
        (execution, ("EndToEndRepetitions", "FsBlindGroupingOnly"), "Execution"),
        (plan["ObservationCapDeclaration"],
         ("MaximumObservationsPerBaseIdentity", "ManifestSha256", "ObservationsSha256"),
         "ObservationCapDeclaration"),
        (plan["PinnedIbgeBootstrap"],
         ("GeneratorSeed", "GeneratorVersion", "CorpusFingerprint",
          "GenerationHypothesesFingerprint"), "PinnedIbgeBootstrap"),
    ):
        require(isinstance(obj, dict), context + " must be an object")
        require(not set(fields) - set(obj), context + " missing " +
                ", ".join(sorted(set(fields) - set(obj))))
    for field in ("PinnedIbgePublishedSha256",
                  "PinnedIbgeMaterializedFrequencySha256"):
        require(valid_sha(plan[field]), "Invalid " + field)
    require(option["DecisionThreshold"] > 0 and isinstance(plan["StudyFingerprintSha256"], str),
            "Study fingerprint/threshold absent")
    for margin in plan["ConflictMargins"]:
        number(margin, "ConflictMargin", positive=True)
    if selected.startswith("TF_"):
        slots = option.get("UnitTfOptions")
        require(isinstance(slots, list) and len(slots) == 4,
                "Positive TF requires four declared unit slots in plan")
        keys = {(x.get("Side"), x.get("Unit")) for x in slots if isinstance(x, dict)}
        require(keys == {(0, 2), (0, 3), (1, 2), (1, 3)},
                "TF unit grid is incomplete")
        require(any(number(x.get("Weight"), "TF.Weight") > 0 for x in slots),
                "Selected TF arm has no positive weight")
    return option


def audit_study(study, snapshot_sha, option_id, margin):
    require(isinstance(study, dict) and study.get("Version") == STUDY_VERSION,
            "Unsupported study version")
    for f in RESULT_FIELDS:
        require(valid_sha(study.get(f)), "Missing/incomplete study " + f)
    sel = study.get("SourceSelection")
    require(isinstance(sel, dict) and isinstance(sel.get("SourceReport"), dict),
            "Missing selection report")
    require(sel.get("TestUsed") is False
            and sel["SourceReport"].get("TestUsed") is False
            and sel["SourceReport"].get("OperationalModelCreated") is False
            and sel["SourceReport"].get("AutomaticActivationAllowed") is False,
            "Study is not provably TEST-blind/non-operational")
    rows = study.get("SourceMeasurements")
    configs = study.get("FrozenConfigurations")
    require(isinstance(rows, list) and bool(rows)
            and isinstance(configs, list) and len(rows) == len(configs),
            "Missing/incoherent physical measurements and fits")
    keys = set()
    found = []
    for row in rows:
        require(isinstance(row, dict), "Invalid source measurement")
        key = (row.get("OptionId"), row.get("ConflictMargin"))
        require(key not in keys, "Duplicate metric key")
        keys.add(key)
        if bare_option(key[0]) == option_id and key[1] == margin:
            found.append(row)
    fit_keys = {(x.get("OptionId"), x.get("ConflictMargin"))
                for x in configs if isinstance(x, dict)}
    require(len(fit_keys) == len(configs) and fit_keys == keys,
            "Metric/snapshot coverage mismatch")
    require(len(found) == 1, "Exactly one selected source metric required")
    matching = [x for x in configs
                if bare_option(x["OptionId"]) == option_id and x["ConflictMargin"] == margin]
    require(len(matching) == 1
            and isinstance(matching[0].get("Snapshot"), dict)
            and matching[0]["Snapshot"].get("ContentSha256") == snapshot_sha,
            "Selected model has no matching physical fit receipt")
    row = found[0]
    tp = row.get("TrueAcceptedSourceObservations")
    fp = row.get("FalseAcceptedSourceObservations")
    truth = row.get("EligibleSourceObservationsBeforeBlocking")
    require(all(type(x) is int and x >= 0 for x in (tp, fp, truth))
            and truth > 0 and tp <= truth and tp + fp > 0,
            "Invalid TP/FP/eligible source counts")
    recall = number(row.get("GlobalSourceRecall"), "recall")
    fdr = number(row.get("FdrAfterResolve"), "FDR")
    require(recall <= 1 and fdr <= 1
            and abs(tp / truth - recall) < 1e-10
            and abs(fp / (tp + fp) - fdr) < 1e-10,
            "Reported recall/FDR disagrees with accepted source counts")
    require(row.get("CandidateGrouping") == "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            and row.get("SyntheticGoldOracleGrouping") is False,
            "Invalid blinded physical grouping")
    return row


def audit_snapshot(snap, model_sha, option, plan):
    require(isinstance(snap, dict), "Invalid fitted model")
    missing = sorted(set(SNAPSHOT_FIELDS) - set(snap))
    require(not missing, "Missing fitted-model fields: " + ", ".join(missing))
    require(snap["SchemaVersion"] in ("V9_FITTED_SNAPSHOT_V2",
                                     "V9_FITTED_SNAPSHOT_V3"),
            "Unsupported fitted snapshot version")
    require(bare_option(snap["OptionId"]) == option["OptionId"],
            "Fitted model option differs from plan")
    require(float(snap["Threshold"]) == float(option["DecisionThreshold"]),
            "Fitted threshold differs from declared plan")
    require(snap["ConflictMargin"] in plan["ConflictMargins"],
            "Fitted margin absent from plan")
    require(isinstance(snap["JointCells"], list) and len(snap["JointCells"]) == 81,
            "81 nominal cells missing")
    require(isinstance(snap["Parameters"], dict) and snap["Parameters"],
            "FS parameters absent")
    require(isinstance(snap["BlockingPasses"], list) and snap["BlockingPasses"],
            "Fitted blocking absent")
    require(snap["BlockingPasses"] in plan["BaselineRuleSets"],
            "Fitted blocking differs from predeclared baseline")
    for f in ("ProjectionFingerprintSha256", "FitFingerprintSha256",
              "TrainCorpusSha256", "ValidationCorpusSha256",
              "TrainIdentityGroupsSha256", "ValidationIdentityGroupsSha256"):
        require(valid_sha(snap.get(f)), "Missing fitted-model " + f)
    if option["OptionId"].startswith("TF_"):
        units = snap.get("UnitTfCalibrations")
        require(isinstance(units, list) and units, "Fitted TF-unit states missing")
        require(any(isinstance(u, dict) and u.get("State") in (0, "Active")
                    and isinstance(u.get("Calibration"), dict)
                    for u in units), "No active TF calibration in fitted model")
    require(model_sha == sha(Path(snap["__path__"])), "Snapshot bytes changed")


def summary_report(row, run_id, source_sha, study_sha, model_sha, plan, snapshot):
    # No inference about statistical independence; this is an archival report.
    return {
        "SchemaVersion": REPORT_VERSION,
        "Nature": "READ_ONLY_RECONSTRUCTION_FROM_ORIGINAL_STUDY",
        "CalibrationRunId": str(run_id),
        "SourceCommitSha": source_sha,
        "SourceStudySha256": study_sha,
        "SourceSnapshotSha256": model_sha,
        "ModelOptionId": bare_option(snapshot["OptionId"]),
        "ConflictMargin": snapshot["ConflictMargin"],
        "SourceMetrics": {
            k: row.get(k) for k in (
                "EligibleSourceObservationsBeforeBlocking",
                "TrueAcceptedSourceObservations", "FalseAcceptedSourceObservations",
                "GlobalSourceRecall", "FdrAfterResolve",
                "ConditionalClusterCpFdrUpper", "HoeffdingUpperBySourceBaseIdentity",
                "ClusterIidIndependentlyValidated", "FalseDegreeP99",
                "ConflictSourceObservations", "PendingSourceObservations",
            )
        },
        "ControlAndPolicy": {
            "HistoricalMinimumGlobalRecall": plan["SelectionPolicy"]["MinimumGlobalRecall"],
            "HistoricalMaximumFdrUpper": plan["SelectionPolicy"]["MaximumAcceptedFdrUpper"],
            "HistoricalMinimumIndependentGroups": plan["SelectionPolicy"]["MinimumIndependentIdentityGroups"],
            "HistoricalMaximumP95Milliseconds": plan["SelectionPolicy"]["MaximumP95Milliseconds"],
            "PredeclaredComparedConfigurations": plan["MaximumComparedConfigurations"],
            "EndToEndRepetitionsPlanned": plan["Execution"]["EndToEndRepetitions"],
        },
        "Status": {
            "IndependentClusterAssumptionsValidated":
                row.get("ClusterIidIndependentlyValidated") is True,
            "EndToEndP95ForSelectedModel": "NOT_ATTESTED_IN_THIS_REPORT",
            "OperationallyCertified": False,
            "Dt15Authorized": False,
            "TestEvaluated": False,
        },
        "Limits": [
            "Historic synthetic TRAIN/VALIDATION evidence, not a municipal estimate",
            "Conditional CP is not certified without validated identity independence",
            "Historical thresholds are recorded, not adopted as universal new policy",
            "This reconstructed management/statistical summary is not the original signed decision dossier",
        ],
    }


def reference_entry(reference, model_key):
    require(reference.get("schema_version") == "V9_FROZEN_ARTIFACT_REFERENCES_V1",
            "Unsupported archived source reference")
    source = reference["artifacts"]["seed42-source"]
    model_sha = reference["models"][model_key]
    items = {x["path"]: x["sha256"] for x in source["files"]}
    require("study-tf/" + model_sha + ".fitted.json" in items,
            "Model not present in source campaign")
    return source, items, model_sha


def validate_sources(source_dir, references, model_key):
    reference = strict(references)
    source, pins, model_sha = reference_entry(reference, model_key)
    paths = {
        "plan": source_dir / "plan-tf.json",
        "result": source_dir / "study-tf" / "study.json",
        "fitted_model": source_dir / "study-tf" / (model_sha + ".fitted.json"),
    }
    for role, path in paths.items():
        require(path.is_file(), role + " missing: " + str(path))
        expected = pins["plan-tf.json"] if role == "plan" else (
            pins["study-tf/study.json"] if role == "result" else model_sha)
        require(sha(path) == expected, role + " differs from pinned SHA-256")
    plan, study, snap = (strict(paths[key]) for key in ("plan", "result", "fitted_model"))
    require(valid_sha(source["head_sha"], 40) and type(source["run_id"]) is int
            and source["run_id"] > 0, "Invalid pinned workflow identity")
    option = audit_plan(plan, bare_option(snap.get("OptionId")))
    snap_for_check = dict(snap, __path__=str(paths["fitted_model"]))
    audit_snapshot(snap_for_check, model_sha, option, plan)
    row = audit_study(study, model_sha, option["OptionId"], snap["ConflictMargin"])
    # PlanSha256 in study is a NORMALIZED C# plan digest, NOT the raw plan-file SHA.
    # The two independently pinned domains are intentionally not equated.
    report = summary_report(row, source["run_id"], source["head_sha"],
                            sha(paths["result"]), model_sha, plan, snap)
    return source, paths, report, study, snap


def build(source_dir, references, out_dir, model_key):
    require(not out_dir.exists(), "Refusing to overwrite existing evidence bundle")
    source, paths, report, study, snap = validate_sources(source_dir, references, model_key)
    out_dir.parent.mkdir(parents=True, exist_ok=True)
    tmp = Path(tempfile.mkdtemp(prefix=".v9-calibration-bundle-", dir=out_dir.parent))
    try:
        for role, path in paths.items():
            shutil.copyfile(path, tmp / FILE_MAP[role])
        (tmp / FILE_MAP["report"]).write_text(
            json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
            encoding="utf-8")
        components = {
            role: {"file": filename, "sha256": sha(tmp / filename),
                   "bytes": (tmp / filename).stat().st_size}
            for role, filename in FILE_MAP.items()
        }
        manifest = {
            "SchemaVersion": VERSION,
            "BundleKind": "CALIBRATION_EVIDENCE_NOT_OPERATIONAL_MODEL",
            "ModelKey": model_key,
            "CalibrationRunId": str(source["run_id"]),
            "SourceArtifactName": source["name"],
            "SourceCommitSha": source["head_sha"],
            "Components": components,
            "CrossLinks": {
                "NormalizedCSharpStudyPlanSha256": study["PlanSha256"],
                "TrainInputSha256": study["TrainInputSha256"],
                "ValidationInputSha256": study["ValidationInputSha256"],
                "FittedTrainCorpusSha256": snap["TrainCorpusSha256"],
                "FittedValidationCorpusSha256": snap["ValidationCorpusSha256"],
                "FittedTrainIdentityGroupsSha256": snap["TrainIdentityGroupsSha256"],
                "FittedValidationIdentityGroupsSha256": snap["ValidationIdentityGroupsSha256"],
            },
            "TestUsed": False, "OperationallyCertified": False,
            "Dt15Authorized": False,
        }
        (tmp / "manifest.json").write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        verify(tmp)
        os.rename(tmp, out_dir)
        return manifest
    finally:
        if tmp.exists():
            shutil.rmtree(tmp)


def verify(directory):
    manifest_path = directory / "manifest.json"
    require(manifest_path.is_file(), "Bundle manifest missing")
    manifest = strict(manifest_path)
    require(manifest.get("SchemaVersion") == VERSION
            and manifest.get("BundleKind") == "CALIBRATION_EVIDENCE_NOT_OPERATIONAL_MODEL",
            "Unexpected bundle schema/kind")
    require(manifest.get("TestUsed") is False
            and manifest.get("OperationallyCertified") is False
            and manifest.get("Dt15Authorized") is False,
            "Bundle asserts forbidden state")
    require(valid_sha(manifest.get("SourceCommitSha"), 40)
            and isinstance(manifest.get("CalibrationRunId"), str)
            and manifest["CalibrationRunId"].isdigit(), "Missing source execution identity")
    components = manifest.get("Components")
    require(isinstance(components, dict) and set(components) == set(FILE_MAP),
            "Component set mismatch")
    for role, filename in FILE_MAP.items():
        entry = components[role]
        require(entry.get("file") == filename and valid_sha(entry.get("sha256")),
                "Unsafe or incomplete component entry: " + role)
        path = directory / filename
        require(path.is_file() and not path.is_symlink()
                and path.stat().st_size == entry.get("bytes")
                and sha(path) == entry["sha256"], "Tampered component: " + role)
    require(set(x.name for x in directory.iterdir()) == set(FILE_MAP.values()) | {"manifest.json"},
            "Untracked file in bundle")
    report = strict(directory / FILE_MAP["report"])
    plan = strict(directory / FILE_MAP["plan"])
    study = strict(directory / FILE_MAP["result"])
    snap = strict(directory / FILE_MAP["fitted_model"])
    require(report.get("SchemaVersion") == REPORT_VERSION
            and report.get("SourceStudySha256") == components["result"]["sha256"]
            and report.get("SourceSnapshotSha256") == components["fitted_model"]["sha256"]
            and report.get("SourceCommitSha") == manifest["SourceCommitSha"]
            and report.get("CalibrationRunId") == manifest["CalibrationRunId"],
            "Report not bound to this calibration run")
    require(report.get("ModelOptionId") == bare_option(snap.get("OptionId"))
            and report.get("ConflictMargin") == snap.get("ConflictMargin"),
            "Report points to a different fitted model")
    option = audit_plan(plan, report["ModelOptionId"])
    snap_checked = dict(snap, __path__=str(directory / FILE_MAP["fitted_model"]))
    audit_snapshot(snap_checked, components["fitted_model"]["sha256"], option, plan)
    row = audit_study(study, components["fitted_model"]["sha256"],
                      report["ModelOptionId"], report["ConflictMargin"])
    require(report["SourceMetrics"]["GlobalSourceRecall"] == row["GlobalSourceRecall"]
            and report["SourceMetrics"]["FdrAfterResolve"] == row["FdrAfterResolve"],
            "Reported metrics differ from source study")
    cross = manifest.get("CrossLinks")
    require(isinstance(cross, dict)
            and cross.get("NormalizedCSharpStudyPlanSha256") == study["PlanSha256"]
            and cross.get("TrainInputSha256") == study["TrainInputSha256"]
            and cross.get("ValidationInputSha256") == study["ValidationInputSha256"]
            and cross.get("FittedTrainCorpusSha256") == snap["TrainCorpusSha256"]
            and cross.get("FittedValidationCorpusSha256") == snap["ValidationCorpusSha256"]
            and cross.get("FittedTrainIdentityGroupsSha256") == snap["TrainIdentityGroupsSha256"]
            and cross.get("FittedValidationIdentityGroupsSha256") == snap["ValidationIdentityGroupsSha256"],
            "Broken study/snapshot cross-links")
    return manifest, report


def compare(left, right):
    ml, rl = verify(left)
    mr, rr = verify(right)
    cl, cr = ml["CrossLinks"], mr["CrossLinks"]
    same = {f: cl[f] == cr[f] for f in (
        "TrainInputSha256", "ValidationInputSha256",
        "FittedTrainIdentityGroupsSha256", "FittedValidationIdentityGroupsSha256")}
    # Same source input cohorts and independent identity partitions required
    # before comparing descriptive recall/FDR; even so, no causal inference
    # without checking SQL candidate frames, selection and code version.
    controlled = all(same.values())
    return {
        "SchemaVersion": "JORNADA_CALIBRATION_EVIDENCE_COMPARISON_V1",
        "LeftManifestSha256": sha(left / "manifest.json"),
        "RightManifestSha256": sha(right / "manifest.json"),
        "InputIdentityAgreement": same,
        "ControlledSourceComparison": controlled,
        "SameCodeSha": ml["SourceCommitSha"] == mr["SourceCommitSha"],
        "SamePlanSha": (ml["Components"]["plan"]["sha256"]
                        == mr["Components"]["plan"]["sha256"]),
        "SameFittedBlocking": strict(left / FILE_MAP["fitted_model"])["BlockingPasses"]
                              == strict(right / FILE_MAP["fitted_model"])["BlockingPasses"],
        "RecallDelta": (rr["SourceMetrics"]["GlobalSourceRecall"]
                        - rl["SourceMetrics"]["GlobalSourceRecall"]) if controlled else None,
        "FdrDelta": (rr["SourceMetrics"]["FdrAfterResolve"]
                     - rl["SourceMetrics"]["FdrAfterResolve"]) if controlled else None,
        "RegressionCertified": False,
        "Caveat": "Aggregate deltas are descriptive; require paired source IDs, same candidate frame, "
                  "predeclared statistical method and independent TEST for causal/certified comparison.",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    b = sub.add_parser("build", help="Read pinned V2 archive, create immutable evidence bundle")
    b.add_argument("--source-dir", type=Path, required=True)
    b.add_argument("--references", type=Path, required=True)
    b.add_argument("--out-dir", type=Path, required=True)
    b.add_argument("--model-key", default="V2_ONE_MF_original")
    v = sub.add_parser("verify", help="Fail-closed offline verification")
    v.add_argument("--bundle-dir", type=Path, required=True)
    c = sub.add_parser("compare", help="Descriptive regression preflight, never certification")
    c.add_argument("--left", type=Path, required=True)
    c.add_argument("--right", type=Path, required=True)
    args = parser.parse_args(argv)
    if args.command == "build":
        result = build(args.source_dir, args.references, args.out_dir, args.model_key)
        print(json.dumps({"Status": "BUNDLE_CREATED_READ_ONLY", "Directory": str(args.out_dir),
                          "Run": result["CalibrationRunId"],
                          "ManifestSha256": sha(args.out_dir / "manifest.json")}))
    elif args.command == "verify":
        result, _ = verify(args.bundle_dir)
        print(json.dumps({"Status": "BUNDLE_INTEGRITY_OK_NOT_CERTIFIED",
                          "Run": result["CalibrationRunId"],
                          "ManifestSha256": sha(args.bundle_dir / "manifest.json")}))
    else:
        print(json.dumps(compare(args.left, args.right), indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError) as exc:
        print("FAIL_CLOSED:", str(exc), file=sys.stderr)
        sys.exit(2)
