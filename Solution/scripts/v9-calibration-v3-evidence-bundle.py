#!/usr/bin/env python3
"""V3 adaptation of the existing V2 evidence bundle contract, never a second calibrator.

Same five files as the V2 bundle: plan.json, result.json, model.fitted.json,
report.json, manifest.json. Reuses V2 hashing/strict JSON/role mapping exactly.
Only reads an already completed C# --v9-calibrate TRAIN output; no SQL, TF fit,
REF, TEST, Gold, registry schema, Terraform or operational promotion.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import shutil
import sys
import tempfile

_V2_PATH = Path(__file__).with_name("v9-calibration-evidence-bundle.py")
_spec = importlib.util.spec_from_file_location("v9_v2_evidence_contract", _V2_PATH)
v2 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(v2)

FILE_MAP = v2.FILE_MAP
sha = v2.sha
strict = v2.strict
valid_sha = v2.valid_sha
require = v2.require
fail = v2.fail

VERSION = "JORNADA_CALIBRATION_EVIDENCE_BUNDLE_V2_V3"
REPORT_VERSION = "JORNADA_CALIBRATION_EVIDENCE_REPORT_V2_V3"
SOURCE_ANALYSIS_SCHEMA = "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
SOURCE_SCOPE = "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
SOURCE_REF_SCHEMA = "V9_V3_CALIBRATION_SOURCE_PINS_V1"
MODEL_METHOD = "CLASSWISE_SUPPORT_BACKOFF"
ROLES = frozenset(FILE_MAP)


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True,
                      separators=(",", ":"), allow_nan=False).encode("utf-8")


def check_sha(value, label, size=64):
    require(valid_sha(value, size), "Invalid SHA for " + label)
    return value.lower()


def read_source(root, selected_sha):
    require(root.is_dir() and not root.is_symlink(), "C# results directory missing/unsafe")
    check_sha(selected_sha, "selected fitted snapshot")
    selected_sha = selected_sha.lower()
    paths = {
        "plan": root / "plan.json",
        "fitted_model": root / (selected_sha + ".fitted.json"),
    }
    for role, path in paths.items():
        require(path.is_file() and not path.is_symlink(), role + " missing or symlink")

    plan = strict(paths["plan"])
    require(isinstance(plan, dict) and plan.get("Purpose") == "BOOTSTRAP_CANDIDATE_SELECTION",
            "Not a V9 TRAIN calibration plan")
    methods = plan.get("MuEstimationMethods")
    require(isinstance(methods, list) and len(methods) == 1
            and isinstance(methods[0], dict)
            and methods[0].get("Method") == MODEL_METHOD,
            "V3-only plan required; historical V2/DEFAULT cannot be bundled")
    grid = methods[0].get("Parameters")
    dims = ("ClassMassCoverage", "MinimumObservedMPerCell",
            "MinimumObservedUPerCell", "RetainSupportedTail")
    require(isinstance(grid, dict) and set(grid) == set(dims)
            and all(isinstance(grid[k], list) and bool(grid[k]) for k in dims),
            "V3 parameter grid incomplete")
    variant_count = math.prod(len(grid[k]) for k in dims)

    reference_path = root / "selection-reference.json"
    analysis_path = root / "mu-statistical-analysis.json"
    for path in (reference_path, analysis_path):
        require(path.is_file() and not path.is_symlink(),
                "Missing source statistical analysis or selection reference")
    reference = strict(reference_path)
    analysis = strict(analysis_path)
    envelope_sha = check_sha(reference.get("envelopeSha256"), "selection envelope")
    paths["result"] = root / (envelope_sha + ".selection.json")
    require(paths["result"].is_file() and not paths["result"].is_symlink()
            and sha(paths["result"]) == envelope_sha,
            "Result selection envelope changed or absent")
    envelope = strict(paths["result"])
    result = envelope.get("result")
    require(isinstance(result, dict), "C# selection result missing")
    require(analysis.get("schema") == SOURCE_ANALYSIS_SCHEMA
            and analysis.get("scope") == SOURCE_SCOPE
            and analysis.get("originalMethodsAndParameterArrays") == methods
            and analysis.get("declaredVariantCount") == variant_count
            and analysis.get("testUsed") is False
            and analysis.get("activated") is False
            and analysis.get("conditionalFdrBoundsNotCertified") is True,
            "Statistical analysis is not an unchanged V3 TRAIN report")
    require(isinstance(analysis.get("rows"), list) and analysis["rows"]
            and analysis.get("evaluations") == len(analysis["rows"])
            and all(isinstance(row, dict) and row.get("MuEstimationMethod") == MODEL_METHOD
                    for row in analysis["rows"]),
            "V3 M/U statistical rows missing or mixed with V2")
    require(analysis.get("PlanSha256") == result.get("PlanSha256")
            and reference.get("StopReason") == result.get("StopReason")
            and reference.get("FinalistSnapshotShas") == result.get("FinalistSnapshotShas")
            and analysis.get("finalistSnapshotShas") == result.get("FinalistSnapshotShas")
            and reference.get("externalValidationUsed") is False
            and reference.get("testUsed") is False
            and reference.get("activated") is False
            and result.get("ExternalValidationUsed") is False
            and result.get("TestUsed") is False
            and result.get("Activated") is False
            and envelope.get("statisticalCertification") is False
            and envelope.get("operationalActivation") is False,
            "Analysis, selection, plan or nonoperational status disagree")
    source_csv_sha = check_sha(envelope.get("originalObservationsSha256"),
                               "original CSV")
    # This is the input CSV SHA, NOT a recomputable hash of a normalized C# plan.
    declaration = plan.get("Calibration", {}).get("ObservationCapDeclaration")
    require(isinstance(declaration, dict)
            and str(declaration.get("ObservationsSha256", "")).lower() == source_csv_sha,
            "CSV source SHA differs from frozen plan observation declaration")

    snapshot = strict(paths["fitted_model"])
    require(sha(paths["fitted_model"]) == selected_sha,
            "Fitted snapshot bytes differ from C# SHA")
    require(isinstance(snapshot, dict)
            and snapshot.get("SchemaVersion") == "V9_FITTED_SNAPSHOT_V3"
            and isinstance(snapshot.get("OptionId"), str)
            and isinstance(snapshot.get("Parameters"), dict)
            and isinstance(snapshot.get("BlockingPasses"), list)
            and snapshot.get("BlockingPasses"),
            "Selected snapshot is not complete V3 fitted evidence")
    cells = snapshot.get("JointCells")
    require(isinstance(cells, list) and len(cells) == 81,
            "Fitted V3 must contain all 81 joint M/U cells")
    require(len({(str(c.get("Person")), str(c.get("Mother"))) for c in cells
                 if isinstance(c, dict)}) == 81, "Duplicate/malformed joint cell")
    for cls in ("M", "U"):
        values = [c.get(cls) for c in cells]
        require(all(type(v) in (int, float) and math.isfinite(v)
                    and 0 < v <= 1 for v in values)
                and abs(sum(values) - 1) < 1e-6,
                "Unnormalized or missing M/U probabilities in selected snapshot")

    candidates = [row for row in analysis["rows"]
                  if str(row.get("fitSnapshotSha256", "")).lower() == selected_sha]
    require(len(candidates) >= 1
            and all(row.get("OptionId") == snapshot["OptionId"]
                    and row.get("outcome", "").startswith("MEASURED_")
                    and row.get("estimatorJointSupport") is not None
                    and len(row["estimatorJointSupport"].get("Cells", [])) == 81
                    for row in candidates),
            "Selected fit not represented by measured V3 81-cell report rows")
    # A fitted snapshot may appear in more than one evaluation (same fit, different margin).
    # The caller chooses the exact scored evidence using --margin, not a presumed winner.
    return paths, plan, envelope, snapshot, analysis, reference, candidates, {
        "selectionReference": sha(reference_path),
        "statisticalAnalysis": sha(analysis_path),
        "sourceCsv": source_csv_sha,
        "producerManifest": check_sha(envelope.get("producerManifestSha256"),
                                      "generation manifest"),
    }


def choose_row(rows, margin):
    exact = [r for r in rows if r.get("ConflictMargin") == margin]
    require(len(exact) == 1, "Exactly one row must match fitted snapshot and declared margin")
    return exact[0]


def make_report(row, run_id, commit, paths, plan, envelope, snapshot,
                analysis, reference, source_links):
    selected_sha = sha(paths["fitted_model"])
    return {
        "SchemaVersion": REPORT_VERSION,
        "Nature": "READ_ONLY_STATISTICAL_RECONSTRUCTION_FROM_V3_CSHARP_TRAIN",
        "CalibrationRunId": str(run_id),
        "SourceCommitSha": commit,
        "SourceStudySha256": sha(paths["result"]),
        "SourceSnapshotSha256": selected_sha,
        "SourceStatisticalAnalysisSha256": source_links["statisticalAnalysis"],
        "SourceSelectionReferenceSha256": source_links["selectionReference"],
        "SourceInputPlanSha256": sha(paths["plan"]),
        "ModelOptionId": snapshot["OptionId"],
        "ConflictMargin": row["ConflictMargin"],
        "CalibrationPlanNormalizedCSharpSha256": analysis["PlanSha256"],
        "SourceMetrics": {
            k: row.get(k) for k in (
                "trueAccepted", "falseAccepted", "eligibleSources",
                "recall", "fdrObserved", "conditionalClusterCpFdrUpper",
                "clusterIidIndependentlyValidated", "conflicts",
                "unresolved", "p95Milliseconds", "outcome", "Rejections",
            )
        },
        "PolicyAndMethod": {
            "method": MODEL_METHOD,
            "muPolicy": row["MuPolicy"],
            "smoothingAlpha": row.get("smoothingAlpha"),
            "threshold": row.get("threshold"),
            "retainedTail": row["MuPolicy"].get("SupportedTailRule") is not None,
            "declaredGrid": plan["MuEstimationMethods"][0]["Parameters"],
            "tfPublishedReferenceVersionId": plan.get("PinnedIbgeReferenceVersionId"),
            "tfPublishedReferenceSha256": plan.get("PinnedIbgePublishedSha256"),
        },
        "StatisticalAnalysis": analysis,  # verbatim values; includes all 81-cell audits
        "SelectionReference": reference,
        "SourceFrame": {
            "originalObservationsSha256": source_links["sourceCsv"],
            "producerManifestSha256": source_links["producerManifest"],
            "trainCorpusSha256": snapshot.get("TrainCorpusSha256"),
            "validationCorpusSha256": snapshot.get("ValidationCorpusSha256"),
            "trainIdentityGroupsSha256": snapshot.get("TrainIdentityGroupsSha256"),
            "validationIdentityGroupsSha256": snapshot.get("ValidationIdentityGroupsSha256"),
            "blockingFrameSha256": snapshot.get("BlockingFrameFingerprintSha256"),
        },
        "Status": {
            "TestEvaluated": False, "ExternalValidationUsed": False,
            "OperationallyCertified": False, "Dt15Authorized": False,
            "OperationalModelCreated": False,
            "IndependentClusterAssumptionsValidated":
                row.get("clusterIidIndependentlyValidated") is True,
        },
        "Limits": [
            "TRAIN_SELECTION only; neither TEST nor independent VALIDATION",
            "81-cell and M/U audits are statistical evidence, not certification",
            "Conditional CP confidence bounds require independent cluster assumptions",
            "No historical V2 comparability or superior-performance claim",
            "This bundle does not itself publish or activate a model",
        ],
    }


def source_pins(references, run_id, commit, artifact_name, paths, links):
    if references is None:
        return
    require(references.is_file() and not references.is_symlink(),
            "Trusted source pin file absent or symbolic link")
    record = strict(references)
    require(isinstance(record, dict) and record.get("schema_version") == SOURCE_REF_SCHEMA
            and record.get("run_id") == str(run_id)
            and record.get("head_sha") == commit
            and record.get("artifact_name") == artifact_name,
            "Externally pinned V3 execution identity differs")
    expected = record.get("files")
    actual = {
        "plan.json": sha(paths["plan"]),
        sha(paths["result"]) + ".selection.json": sha(paths["result"]),
        sha(paths["fitted_model"]) + ".fitted.json": sha(paths["fitted_model"]),
        "mu-statistical-analysis.json": links["statisticalAnalysis"],
        "selection-reference.json": links["selectionReference"],
    }
    require(isinstance(expected, dict) and actual == expected,
            "Source files differ from externally pinned original run hashes")


def build(source_dir, out_dir, selected_sha, margin, run_id, source_commit,
          source_artifact_name, references=None):
    require(type(run_id) is int and run_id > 0, "Positive GitHub Actions run ID required")
    check_sha(source_commit, "Git commit SHA", size=40)
    require(isinstance(source_artifact_name, str)
            and source_artifact_name.strip()
            and "\n" not in source_artifact_name,
            "Source workflow artifact name required")
    source_dir, out_dir = Path(source_dir), Path(out_dir)
    require(not out_dir.exists() and not out_dir.is_symlink(),
            "Refusing to overwrite V3 calibration evidence bundle")
    paths, plan, envelope, snapshot, analysis, ref, rows, links = read_source(
        source_dir, selected_sha)
    row = choose_row(rows, margin)
    source_pins(references, run_id, source_commit,
                source_artifact_name, paths, links)
    report = make_report(row, run_id, source_commit, paths, plan, envelope,
                         snapshot, analysis, ref, links)
    out_dir.parent.mkdir(parents=True, exist_ok=True)
    tmp = Path(tempfile.mkdtemp(prefix=".v9-calibration-bundle-", dir=out_dir.parent))
    try:
        for role in ("plan", "result", "fitted_model"):
            shutil.copyfile(paths[role], tmp / FILE_MAP[role])
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
            "ModelKey": selected_sha.lower(),
            "CalibrationRunId": str(run_id),
            "SourceArtifactName": source_artifact_name,
            "SourceCommitSha": source_commit,
            "PinnedReferenceSha256": sha(references) if references else None,
            "Components": components,
            "CrossLinks": {
                "NormalizedCSharpStudyPlanSha256": analysis["PlanSha256"],
                "OriginalObservationsSha256": links["sourceCsv"],
                "ProducerManifestSha256": links["producerManifest"],
                "SourceStatisticalAnalysisSha256": links["statisticalAnalysis"],
                "SourceSelectionReferenceSha256": links["selectionReference"],
                "SelectionEnvelopeSha256": sha(paths["result"]),
                "FittedSnapshotSha256": sha(paths["fitted_model"]),
                "FittedTrainCorpusSha256": snapshot.get("TrainCorpusSha256"),
                "FittedValidationCorpusSha256": snapshot.get("ValidationCorpusSha256"),
                "FittedTrainIdentityGroupsSha256": snapshot.get("TrainIdentityGroupsSha256"),
                "FittedValidationIdentityGroupsSha256": snapshot.get("ValidationIdentityGroupsSha256"),
                "FittedBlockingFrameSha256": snapshot.get("BlockingFrameFingerprintSha256"),
            },
            "TestUsed": False, "OperationallyCertified": False,
            "Dt15Authorized": False,
        }
        (tmp / "manifest.json").write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8")
        verify(tmp, references)
        os.rename(tmp, out_dir)
        return manifest
    finally:
        if tmp.exists():
            shutil.rmtree(tmp)


def verify(bundle_dir, references=None):
    bundle_dir = Path(bundle_dir)
    require(bundle_dir.is_dir() and not bundle_dir.is_symlink(),
            "Evidence bundle directory absent/unsafe")
    manifest_path = bundle_dir / "manifest.json"
    require(manifest_path.is_file() and not manifest_path.is_symlink(),
            "Manifest absent or symlink")
    manifest = strict(manifest_path)
    require(manifest.get("SchemaVersion") == VERSION
            and manifest.get("BundleKind") == "CALIBRATION_EVIDENCE_NOT_OPERATIONAL_MODEL"
            and manifest.get("TestUsed") is False
            and manifest.get("OperationallyCertified") is False
            and manifest.get("Dt15Authorized") is False,
            "Unexpected V3 calibration bundle schema/status")
    require(str(manifest.get("CalibrationRunId", "")).isdigit()
            and int(manifest["CalibrationRunId"]) > 0
            and valid_sha(manifest.get("SourceCommitSha"), 40)
            and isinstance(manifest.get("SourceArtifactName"), str)
            and bool(manifest["SourceArtifactName"]),
            "Source run, commit or artifact identity missing")
    components = manifest.get("Components")
    require(isinstance(components, dict) and set(components) == ROLES,
            "Bundle must have the same four V2 component roles")
    require({p.name for p in bundle_dir.iterdir()} ==
            set(FILE_MAP.values()) | {"manifest.json"},
            "Extra/untracked file in immutable bundle")
    for role, filename in FILE_MAP.items():
        entry = components[role]
        file = bundle_dir / filename
        require(isinstance(entry, dict)
                and entry.get("file") == filename
                and valid_sha(entry.get("sha256"))
                and type(entry.get("bytes")) is int
                and file.is_file() and not file.is_symlink()
                and file.stat().st_size == entry["bytes"]
                and sha(file) == entry["sha256"],
                "Tampered evidence component: " + role)
    plan = strict(bundle_dir / FILE_MAP["plan"])
    envelope = strict(bundle_dir / FILE_MAP["result"])
    snapshot = strict(bundle_dir / FILE_MAP["fitted_model"])
    report = strict(bundle_dir / FILE_MAP["report"])
    analysis = report.get("StatisticalAnalysis")
    selection = report.get("SelectionReference")
    require(isinstance(analysis, dict) and isinstance(selection, dict),
            "Statistical analysis or source selection reference missing")
    require(report.get("SchemaVersion") == REPORT_VERSION
            and report.get("CalibrationRunId") == manifest["CalibrationRunId"]
            and report.get("SourceCommitSha") == manifest["SourceCommitSha"]
            and report.get("SourceStudySha256") == components["result"]["sha256"]
            and report.get("SourceSnapshotSha256") == components["fitted_model"]["sha256"]
            and report.get("SourceInputPlanSha256") == components["plan"]["sha256"]
            and report.get("ModelOptionId") == snapshot.get("OptionId")
            and report.get("ConflictMargin") is not None,
            "Reconstructed report not bound to source run/fit/plan")
    require(analysis.get("schema") == SOURCE_ANALYSIS_SCHEMA
            and analysis.get("scope") == SOURCE_SCOPE
            and analysis.get("originalMethodsAndParameterArrays") == plan.get("MuEstimationMethods")
            and analysis.get("testUsed") is False
            and analysis.get("activated") is False
            and analysis.get("PlanSha256") == envelope.get("result", {}).get("PlanSha256")
            and selection.get("FinalistSnapshotShas") == analysis.get("finalistSnapshotShas")
            and selection.get("envelopeSha256") == components["result"]["sha256"],
            "Statistical analysis and C# selection diverge")
    check_sha(components["fitted_model"]["sha256"], "fitted component")
    require(manifest.get("ModelKey") == components["fitted_model"]["sha256"],
            "ModelKey must be fitted snapshot SHA")
    rows = [r for r in analysis.get("rows", [])
            if r.get("fitSnapshotSha256") == manifest["ModelKey"]
            and r.get("ConflictMargin") == report["ConflictMargin"]]
    require(len(rows) == 1 and rows[0].get("OptionId") == snapshot.get("OptionId")
            and rows[0].get("MuEstimationMethod") == MODEL_METHOD,
            "Selected fit/margin differs from statistical analysis")
    links = manifest.get("CrossLinks")
    require(isinstance(links, dict)
            and links.get("NormalizedCSharpStudyPlanSha256") == analysis["PlanSha256"]
            and links.get("OriginalObservationsSha256") == envelope.get("originalObservationsSha256")
            and links.get("ProducerManifestSha256") == envelope.get("producerManifestSha256")
            and links.get("SourceSelectionReferenceSha256") == report.get("SourceSelectionReferenceSha256")
            and links.get("SourceStatisticalAnalysisSha256") == report.get("SourceStatisticalAnalysisSha256")
            and links.get("SelectionEnvelopeSha256") == components["result"]["sha256"]
            and links.get("FittedSnapshotSha256") == components["fitted_model"]["sha256"]
            and links.get("FittedTrainCorpusSha256") == snapshot.get("TrainCorpusSha256")
            and links.get("FittedValidationCorpusSha256") == snapshot.get("ValidationCorpusSha256")
            and links.get("FittedTrainIdentityGroupsSha256") == snapshot.get("TrainIdentityGroupsSha256")
            and links.get("FittedValidationIdentityGroupsSha256") == snapshot.get("ValidationIdentityGroupsSha256")
            and links.get("FittedBlockingFrameSha256") == snapshot.get("BlockingFrameFingerprintSha256"),
            "Broken V2-style manifest hash links")
    require(report["SourceMetrics"]["trueAccepted"] == rows[0].get("trueAccepted")
            and report["SourceMetrics"]["falseAccepted"] == rows[0].get("falseAccepted")
            and report["SourceMetrics"]["fdrObserved"] == rows[0].get("fdrObserved")
            and report["PolicyAndMethod"]["muPolicy"] == rows[0].get("MuPolicy"),
            "Reconstructed statistical report changed after sourcing")
    if references is not None:
        require(manifest.get("PinnedReferenceSha256") == sha(references),
                "Trusted external source reference SHA changed")
        paths = {
            "plan": bundle_dir / FILE_MAP["plan"],
            "result": bundle_dir / FILE_MAP["result"],
            "fitted_model": bundle_dir / FILE_MAP["fitted_model"],
        }
        links2 = {
            "statisticalAnalysis": report["SourceStatisticalAnalysisSha256"],
            "selectionReference": report["SourceSelectionReferenceSha256"]
        }
        source_pins(references, int(manifest["CalibrationRunId"]),
                    manifest["SourceCommitSha"], manifest["SourceArtifactName"], paths, links2)
    return manifest, report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    b = sub.add_parser("build", help="V3 TRAIN: copy V2 bundle layout with SHA manifest")
    b.add_argument("--source-dir", type=Path, required=True)
    b.add_argument("--out-dir", type=Path, required=True)
    b.add_argument("--fitted-sha", required=True)
    b.add_argument("--margin", type=float, required=True)
    b.add_argument("--run-id", type=int, required=True)
    b.add_argument("--source-commit-sha", required=True)
    b.add_argument("--source-artifact-name", required=True)
    b.add_argument("--references", type=Path, default=None)
    v = sub.add_parser("verify", help="Verify V2-style flat V3 bundle without SQL")
    v.add_argument("--bundle-dir", type=Path, required=True)
    v.add_argument("--references", type=Path)
    args = parser.parse_args(argv)
    if args.command == "build":
        manifest = build(args.source_dir, args.out_dir, args.fitted_sha,
                         args.margin, args.run_id, args.source_commit_sha,
                         args.source_artifact_name, args.references)
        directory = args.out_dir
    else:
        manifest, _ = verify(args.bundle_dir, args.references)
        directory = args.bundle_dir
    print(json.dumps({
        "Status": "V3_CALIBRATION_EVIDENCE_INTEGRITY_ONLY_NOT_CERTIFIED",
        "Directory": str(directory),
        "Run": manifest["CalibrationRunId"],
        "ManifestSha256": sha(Path(directory) / "manifest.json"),
        "FittedSnapshotSha256": manifest["ModelKey"],
        "TestUsed": False, "Dt15Authorized": False
    }, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, OSError, OverflowError) as exc:
        print("FAIL_CLOSED:", exc, file=sys.stderr)
        sys.exit(2)
