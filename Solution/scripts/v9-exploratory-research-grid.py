#!/usr/bin/env python3
"""Fail-closed research-only grid planner; no calibration or operational writes."""
import argparse
import hashlib
import itertools
import json
import math
from pathlib import Path

ALLOWED = {
    "SmoothingAlpha": (0.000001, 100.0),
    "DecisionThreshold": (-100.0, 100.0),
    "TfWeight": (0.0, 100.0),
    "ConflictMargin": (0.0, 100.0),
}
BUDGET_KEYS = {"MaximumConfigurations", "MaximumMemoryMiB", "MaximumSeconds", "MaximumParallelism"}
def validate(spec):
    if not isinstance(spec, dict) or set(spec) != {"ResearchOnly", "Enabled", "Budget", "Domains", "NormalCandidatePairsSha256"}:
        raise ValueError("Invalid research plan keys")
    if spec["ResearchOnly"] is not True or spec["Enabled"] is not True:
        raise ValueError("Research must be explicitly enabled and non-operational")
    sha = spec["NormalCandidatePairsSha256"]
    if not isinstance(sha, str) or len(sha) != 64 or any(c not in "0123456789abcdef" for c in sha):
        raise ValueError("Normal candidate-pair SHA256 required")
    budget = spec["Budget"]
    if not isinstance(budget, dict) or set(budget) != BUDGET_KEYS:
        raise ValueError("All research budget dimensions are mandatory")
    for key, maximum in (("MaximumConfigurations", 10000), ("MaximumMemoryMiB", 1048576),
                         ("MaximumSeconds", 604800), ("MaximumParallelism", 1024)):
        val = budget[key]
        if type(val) is not int or not 1 <= val <= maximum:
            raise ValueError("Invalid budget " + key)
    domains = spec["Domains"]
    if not isinstance(domains, dict) or not domains or set(domains) - set(ALLOWED):
        raise ValueError("Unknown or empty parameter domains")
    size = 1
    for name, values in domains.items():
        if not isinstance(values, list) or not values:
            raise ValueError("Empty parameter domain")
        low, high = ALLOWED[name]
        if any(type(x) not in (int, float) or not math.isfinite(x) or not low <= x <= high for x in values):
            raise ValueError("Invalid parameter value: " + name)
        if len({json.dumps(x) for x in values}) != len(values):
            raise ValueError("Duplicate parameter value")
        size *= len(values)
        if size > budget["MaximumConfigurations"]:
            raise ValueError("Research grid exceeds configured budget")
    return size

def plan(raw):
    def no_duplicates(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate key " + key)
            result[key] = value
        return result
    spec = json.loads(raw, object_pairs_hook=no_duplicates,
                      parse_constant=lambda x: (_ for _ in ()).throw(ValueError("Nonfinite number")))
    count = validate(spec)
    names = sorted(spec["Domains"])
    proposals = []
    for values in itertools.product(*(spec["Domains"][name] for name in names)):
        params = dict(zip(names, values))
        canonical = json.dumps(params, sort_keys=True, separators=(",", ":"), allow_nan=False)
        proposals.append({"OptionId": "EXP_" + hashlib.sha256(canonical.encode()).hexdigest()[:20].upper(),
                          "Parameters": params, "EligibleForSelection": False,
                          "PhysicallyEvaluated": False, "Certified": False})
    return {"Schema": "V9_RESEARCH_GRID_V1", "ExploratoryOnly": True,
            "AutoApplied": False, "NormalCandidatePairsSha256": spec["NormalCandidatePairsSha256"],
            "Budget": spec["Budget"], "PlanSha256": hashlib.sha256(raw).hexdigest(),
            "ConfigurationCount": count, "Proposals": proposals,
            "Status": "PROPOSALS_NOT_EVALUATED",
            "Note": "Physical scoring and dependent-observation inference require a separate evidence-producing runner."}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("research_plan")
    parser.add_argument("output")
    args = parser.parse_args()
    source, output = Path(args.research_plan), Path(args.output)
    if output.exists() or source.resolve() == output.resolve():
        raise ValueError("Output must be new and separate")
    data = plan(source.read_bytes())
    with output.open("x", encoding="utf-8") as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write("\n")
