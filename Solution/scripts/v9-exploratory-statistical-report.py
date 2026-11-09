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
        raise ValueError("Enabled exploration requires parameter declaration")
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
    # Existing passes/results are referenced, never recomputed or reinterpreted.
    report["exploratoryAnalysis"] = {
        "ExploratoryOnly": True, "AutoApplied": False,
        "SelectionChanged": False, "OperationalParametersChanged": False,
        "RefChanged": False, "TestUsed": False, "Certified": False,
        "Status": "DECLARED_NOT_PHYSICALLY_EVALUATED",
        "Parameters": values,
        "NormalReportSha256": digest(original),
        "ParameterDeclarationSha256": digest(params_raw),
        "ReusedNormalPasses": "REFERENCE_ONLY_NO_NEW_SQL",
        "ExistingEvaluationCount": report.get("evaluations"),
        "ExistingRowsCount": len(report["rows"]) if isinstance(report.get("rows"), list) else None,
        "AdditionalMeasurements": [],
        "Note": "No exploratory metrics claimed; additional physical evaluation is not implemented."
    }
    data = (json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode()
    with target.open("xb") as stream:
        stream.write(data)
    return {"enabled": True, "sourceSha256": digest(original), "outputSha256": digest(data),
            "activated": False, "evaluated": False}

if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("normal_report")
    p.add_argument("output_report")
    p.add_argument("--enabled", action="store_true")
    p.add_argument("--parameters")
    a = p.parse_args()
    print(json.dumps(extend(a.normal_report, a.output_report,
                            enabled=a.enabled, parameters_path=a.parameters)))
