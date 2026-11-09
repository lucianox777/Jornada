#!/usr/bin/env python3
"""Research-only conditional extension of the existing M/U statistical report.

Never executes blocking, fits, calibration, selection or activation. Reads
measurements already produced by normal calibration and annotates a COPY.
"""
import argparse
import hashlib
import json
from pathlib import Path

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

def read_json(raw):
    def unique(pairs):
        out = {}
        for key, value in pairs:
            if key in out:
                raise ValueError("Duplicate JSON key: " + key)
            out[key] = value
        return out
    def finite(value):
        raise ValueError("Non-finite JSON number: " + value)
    return json.loads(raw, object_pairs_hook=unique, parse_constant=finite)

def extend(report_path, output_path, *, enabled=False, parameters_path=None):
    source, target = Path(report_path), Path(output_path)
    if source.resolve() == target.resolve():
        raise ValueError("Output must differ from source")
    if target.exists():
        raise FileExistsError(target)
    original = source.read_bytes()
    if not enabled:
        if parameters_path is not None:
            raise ValueError("Exploratory parameters require --enabled")
        # Byte-for-byte regression guarantee, no parsing or changes.
        target.write_bytes(original)
        return {"enabled": False, "identical": target.read_bytes() == original,
                "sha256": digest(original)}
    if parameters_path is None:
        # Report-only exploration: derive identifiers from existing physical rows.
        # Never invent an option, fit a model or change calibration inputs.
        source_report = read_json(original)
        if not isinstance(source_report, dict) or source_report.get("schema") != "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1":
            raise ValueError("Unexpected statistical report schema")
        rows = source_report.get("rows")
        if not isinstance(rows, list):
            raise ValueError("Expected normal report rows array")
        option_ids = sorted({r["OptionId"] for r in rows if isinstance(r, dict)
                             and isinstance(r.get("OptionId"), str) and r.get("fitSnapshotSha256")})
        if not option_ids:
            raise ValueError("No measured normal options for report-only exploration")
        params = {"ExploratoryParameters": [{"Name": "OptionId", "Values": option_ids}]}
        params_raw = json.dumps(params, sort_keys=True, separators=(",", ":")).encode()
    else:
        params_raw = Path(parameters_path).read_bytes()
        params = read_json(params_raw)
    if not isinstance(params, dict) or set(params) != {"ExploratoryParameters"}:
        raise ValueError("Expected ExploratoryParameters only")
    values = params["ExploratoryParameters"]
    if not isinstance(values, list) or not values:
        raise ValueError("Nonempty ExploratoryParameters required")
    if any(not isinstance(x, dict) or set(x) != {"Name", "Values"}
           or not isinstance(x["Name"], str) or not x["Name"]
           or not isinstance(x["Values"], list) or not x["Values"]
           for x in values):
        raise ValueError("Invalid exploratory parameter declaration")
    names = [x["Name"] for x in values]
    if len(names) != len(set(names)):
        raise ValueError("Duplicate exploratory parameter")
    report = read_json(original)
    if not isinstance(report, dict) or report.get("schema") != "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1":
        raise ValueError("Unexpected statistical report schema")
    if "exploratoryAnalysis" in report:
        raise ValueError("Exploratory analysis already present")
    # Analyze ONLY already-measured rows from the normal calibration.
    # A same-frame comparison requires identical SQL candidate-pair fingerprints.
    # Never infer an unmeasured option's performance from a different option.
    if not isinstance(report.get("rows"), list):
        raise ValueError("Expected normal report rows array")
    measured = [row for row in report["rows"] if isinstance(row, dict)
                and row.get("fitSnapshotSha256") is not None]
    comparisons = []
    for declaration in values:
        if declaration["Name"] != "OptionId":
            continue
        for option_id in declaration["Values"]:
            if not isinstance(option_id, str):
                raise ValueError("OptionId values must be strings")
            matches = [row for row in measured if row.get("OptionId") == option_id]
            for row in matches:
                frame = row.get("CandidatePairsSha256") or (row.get("candidatePriorTrainingAudit") or {}).get("PhysicalCandidatePairsSha256")
                if not isinstance(frame, str) or not frame:
                    continue
                peers = [other for other in measured if other is not row
                         and (other.get("CandidatePairsSha256") or (other.get("candidatePriorTrainingAudit") or {}).get("PhysicalCandidatePairsSha256")) == frame
                         and other.get("Batch") == row.get("Batch")
                         and other.get("ConflictMargin") == row.get("ConflictMargin")
                         and other.get("blockingPasses") == row.get("blockingPasses")
                         and other.get("MuPolicy") == row.get("MuPolicy")]
                for peer in peers:
                    comparisons.append({
                        "OptionId": option_id,
                        "BaselineOptionId": peer.get("OptionId"),
                        "CandidatePairsSha256": frame,
                        "Batch": row.get("Batch"),
                        "ConflictMargin": row.get("ConflictMargin"),
                        "MeasuredInNormalCalibration": True,
                        "IndependentIdentityUncertaintyEstimated": False,
                        "MultiplicityAdjusted": False,
                        "ComparisonIsDescriptiveOnly": True,
                        "ExploratoryOnly": True,
                        "Recall": row.get("recall"),
                        "BaselineRecall": peer.get("recall"),
                        "FdrObserved": row.get("fdrObserved"),
                        "BaselineFdrObserved": peer.get("fdrObserved"),
                        "RecallDelta": row["recall"] - peer["recall"]
                            if all(type(x.get("recall")) in (float, int) for x in (row, peer)) else None,
                        "FdrDelta": row["fdrObserved"] - peer["fdrObserved"]
                            if all(type(x.get("fdrObserved")) in (float, int) for x in (row, peer)) else None,
                        "StatisticalCertification": False
                    })
    comparisons.sort(key=lambda x: (x["OptionId"], str(x["BaselineOptionId"]),
                                     str(x["Batch"]), str(x["ConflictMargin"])))
    # Existing passes/results are referenced, never recomputed or reinterpreted.
    report["exploratoryAnalysis"] = {
        "ExploratoryOnly": True, "AutoApplied": False,
        "SelectionChanged": False, "OperationalParametersChanged": False,
        "RefChanged": False, "TestUsed": False, "Certified": False,
        "Status": "REUSED_EXISTING_PHYSICAL_MEASUREMENTS" if comparisons else "NO_COMPARABLE_EXISTING_MEASUREMENTS",
        "Parameters": values,
        "NormalReportSha256": digest(original),
        "ParameterDeclarationSha256": digest(params_raw),
        "ReusedNormalPasses": "SAME_CANDIDATE_FRAME_EXISTING_ROWS_NO_NEW_SQL",
        "ExistingEvaluationCount": report.get("evaluations"),
        "ExistingRowsCount": len(report["rows"]) if isinstance(report.get("rows"), list) else None,
        "AdditionalMeasurements": comparisons,
        "Note": "Comparisons use only already measured normal-calibration rows on identical candidate frames; no new parameter combinations fitted."
    }
    data = (json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode()
    with target.open("xb") as stream:
        stream.write(data)
    return {"enabled": True, "sourceSha256": digest(original), "outputSha256": digest(data),
            "activated": False, "evaluated": bool(comparisons)}

if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("normal_report")
    p.add_argument("output_report")
    p.add_argument("--enabled", "--exploration", action="store_true", dest="enabled")
    p.add_argument("--parameters")
    a = p.parse_args()
    print(json.dumps(extend(a.normal_report, a.output_report,
                            enabled=a.enabled, parameters_path=a.parameters)))
