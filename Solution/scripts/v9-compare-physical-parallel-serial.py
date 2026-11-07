#!/usr/bin/env python3
"""Fail-closed V9 frozen-seed42 serial versus parallel physical outcome audit.

Reads only aggregate evidence from two existing SHA-bound GitHub artifact ZIPs.
Does NOT read the raw 30k CSV, raw people, TEST, Gold, or individual identities.
A non-identical fit/content, TP/FP/FN, conflicts, pending reasons or status fails.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import zipfile

SHA = re.compile(r"[0-9a-f]{64}\Z")
RULESET = re.compile(r"[0-9a-f]{64}:")


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=True,
                      separators=(",", ":"), allow_nan=False)


def member(archive, suffix):
    matches = [x for x in archive.infolist() if
               x.filename == suffix or x.filename.endswith("/" + suffix)]
    if len(matches) != 1 or matches[0].file_size > 2_000_000_000:
        raise ValueError("Required artifact member missing, ambiguous or too large: " + suffix)
    return matches[0]


def document(archive, suffix):
    with archive.open(member(archive, suffix), "r") as data:
        return json.load(io.TextIOWrapper(data, encoding="utf-8"))


def optkey(option, margin, grouping=None):
    if not isinstance(option, str) or not RULESET.match(option):
        raise ValueError("Missing stable ruleset prefix on OptionId")
    return (option, str(margin), grouping or "")


def compress(archive_path):
    with zipfile.ZipFile(archive_path, "r") as archive:
        if archive.testzip() is not None:
            raise ValueError("Corrupt evidence ZIP")
        plan = document(archive, "study-tf/plan.json")
        study = document(archive, "study-tf/study.json")
        ledger = document(archive, "people-30k/generation-manifest.json")
        # Ignore only the scheduling knob in plan equivalence, never fit inputs.
        science_plan = dict(plan)
        parallelism = science_plan.pop("MaximumParallelRuleSets", 1)
        science_hash = hashlib.sha256(canonical(science_plan).encode()).hexdigest()
        source = study.get("SourceMeasurements")
        frozen = study.get("FrozenConfigurations")
        if not isinstance(source, list) or len(source) != 135:
            raise ValueError("Expected 135 distinct physical source results")
        if not isinstance(frozen, list) or not frozen:
            raise ValueError("Frozen fit evidence absent")
        if len(plan.get("ModelOptions") or []) != 45:
            raise ValueError("Expected the same 45 options")
        fits = {}
        for item in frozen:
            key = optkey(item["OptionId"], item["ConflictMargin"])
            content_sha = item.get("Snapshot", {}).get("ContentSha256")
            if not isinstance(content_sha, str) or not SHA.fullmatch(content_sha.lower()):
                raise ValueError("Missing valid fit snapshot content SHA")
            if key in fits:
                raise ValueError("Duplicate snapshot key")
            fits[key] = content_sha.lower()
        rows = {}
        for item in source:
            key = optkey(item["OptionId"], item["ConflictMargin"],
                         item["CandidateGrouping"])
            if key in rows:
                raise ValueError("Duplicate measured configuration")
            true_count = item["TrueAcceptedSourceObservations"]
            false_count = item["FalseAcceptedSourceObservations"]
            eligible = item["EligibleSourceObservationsBeforeBlocking"]
            if not all(type(x) is int and x >= 0
                       for x in (true_count, false_count, eligible)):
                raise ValueError("Invalid TP/FP/FN input counts")
            if true_count > eligible:
                raise ValueError("TP exceeds preblocking eligible population")
            fit_key = key[:2]
            if fit_key not in fits:
                raise ValueError("Every measured configuration must have a frozen fit")
            reasons = item.get("ReasonsByResolveStatus")
            if not isinstance(reasons, dict) or not all(type(v) is int and v >= 0 for v in reasons.values()):
                raise ValueError("Missing measured reasons for conflict/pending")
            rows[key] = {
                "TP": true_count, "FP": false_count, "FN": eligible - true_count,
                "Conflicts": item["Conflicts"],
                "Unresolved": item["Unresolved"],
                "ReasonsByResolveStatus": reasons,
                "SnapshotContentSha256": fits[fit_key],
            }
        if len(rows) != 135:
            raise ValueError("Missing or duplicate physical configuration keys")
        pareto = study.get("Pareto")
        if not isinstance(pareto, dict) or not isinstance(pareto.get("Table"), list):
            raise ValueError("Missing final Pareto decision")
        decisions = {}
        for entry in pareto["Table"]:
            opt = entry.get("Option") or {}
            name = opt.get("OptionId")
            if not isinstance(name, str):
                raise ValueError("Pareto option ID missing")
            # Only final decision/status labels, not runtimes or plan SHA.
            selected = {}
            for source_label, obj in (("row", entry), ("option", opt)):
                for field in (
                    "Status", "Outcome", "Reason", "RejectionReason",
                    "EligibleForSelection", "Selected", "Acceptable",
                    "Eligible", "ExclusionReason", "OnFrontier",
                    "ExclusionReasons", "DominatedByOptionIds",
                    "EquivalentOptionIds", "EvidenceRejectionReasons",
                    "IdentityClusterUncertaintyAttested",
                    "PhysicalEndToEndAttested"):
                    if field in obj:
                        selected[source_label + "." + field] = obj[field]
            if name in decisions:
                raise ValueError("Duplicate final decision entry")
            decisions[name] = selected
        metadata = {
            "Input": {
                "StudyFingerprintSha256": plan.get("StudyFingerprintSha256"),
                "TrainInputSha256": study.get("TrainInputSha256"),
                "ValidationInputSha256": study.get("ValidationInputSha256"),
                "PhysicalCandidatePairsSha256ByRuleset": study.get("CandidatePairsSha256ByRuleset"),
                "SciencePlanSha256WithoutRuntimeParallelism": science_hash,
                # Frozen manifest must declare the same stable seed and corpus.
                "Seed": ledger.get("Seed", ledger.get("GeneratorSeed")),
                "ManifestGenerationFingerprint": ledger.get("GenerationHypothesesFingerprint"),
            },
            "RuntimeParallelism": parallelism,
            "PlanSha256": study.get("PlanSha256"),
            "Final": {
                "ParetoStatus": pareto.get("Status"),
                "InitialSuggestedOptionId": pareto.get("InitialSuggestedOptionId"),
                "ExploratoryStatus": pareto.get("ExploratoryStatus"),
                "ExploratorySuggestedOptionId": pareto.get("ExploratorySuggestedOptionId"),
                "PerOptionDecision": decisions,
                "Rejections": study.get("Rejections"),
                "TotalCandidateRuleSets": study.get("TotalCandidateRuleSets"),
                "ExploratoryRiskPreferenceStatus":
                    (study.get("ExploratoryRiskPreference") or {}).get("Status"),
                "ExploratoryRiskPreferenceSuggestedOptionId":
                    (study.get("ExploratoryRiskPreference") or {}).get("SuggestedOptionId"),
            },
            "Rows": rows,
            "FittedKeys": fits,
        }
        if len(fits) < 45:
            raise ValueError("Insufficient frozen configurations")
        return metadata


def diff(a, b, prefix, differences, limit=70):
    if len(differences) >= limit:
        return
    if isinstance(a, dict) and isinstance(b, dict):
        keys = sorted(set(a) | set(b), key=str)
        for k in keys:
            if k not in a or k not in b:
                differences.append(prefix + "/" + str(k) + ": missing key")
            else:
                diff(a[k], b[k], prefix + "/" + str(k), differences, limit)
            if len(differences) >= limit:
                return
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            differences.append(prefix + ": list length differs")
        else:
            for i, (x, y) in enumerate(zip(a, b)):
                diff(x, y, prefix + "/" + str(i), differences, limit)
                if len(differences) >= limit:
                    return
    elif a != b:
        # Never log raw values or identity data. Keys are option/margin/ruleset
        # labels and public metric names; sanitize output to avoid log injection.
        differences.append(prefix + ": differs")


def compare(parallel, serial):
    differences = []
    for section in ("Input", "Rows", "FittedKeys", "Final"):
        diff(parallel[section], serial[section], section, differences)
    # Runtime parallelism intentionally excluded; PlanSha256 may differ.
    return differences


def safe_path(label):
    return re.sub(r"[^A-Za-z0-9_.:/-]", "_", label)[:270]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--parallel", type=Path, required=True)
    p.add_argument("--serial", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    parallel = compress(a.parallel)
    serial = compress(a.serial)
    discrepancies = compare(parallel, serial)
    result = {
        "Contract": "V9_PARALLEL_SERIAL_EXACT_PHYSICAL_RESULT_AUDIT_V1",
        "ParallelRun": 42412346250,
        "SerialRun": 42414220551,
        "ParallelSha": "77fae0d9e2c1603c40e26a13987cd1a160ca4295",
        "SerialSha": "8c40aab2a16f6130e6f8cd9ecf0735cf62fd6ff5",
        "ParallelRuntimeRuleSets": parallel["RuntimeParallelism"],
        "SerialRuntimeRuleSets": serial["RuntimeParallelism"],
        "ParallelPlanSha256": parallel["PlanSha256"],
        "SerialPlanSha256": serial["PlanSha256"],
        "SciencePlanWithoutRuntimeMatches":
            parallel["Input"]["SciencePlanSha256WithoutRuntimeParallelism"] ==
            serial["Input"]["SciencePlanSha256WithoutRuntimeParallelism"],
        "ParallelMeasured": len(parallel["Rows"]), "SerialMeasured": len(serial["Rows"]),
        "ParallelFits": len(parallel["FittedKeys"]), "SerialFits": len(serial["FittedKeys"]),
        "Equivalent": not discrepancies,
        "Differences": [safe_path(x) for x in discrepancies],
        "Status": "PASS_EXACT_SCIENTIFIC_RESULTS" if not discrepancies
                  else "BLOCK_MERGES_PHYSICAL_NONDETERMINISM",
    }
    with a.output.open("x", encoding="utf-8") as f:
        json.dump(result, f, indent=2, sort_keys=True)
        f.write("\n")
    print("V9 serial-parallel audit: " + result["Status"] +
          "; measured: %d/%d; fits: %d/%d; differences: %d" %
          (result["ParallelMeasured"], result["SerialMeasured"],
           result["ParallelFits"], result["SerialFits"], len(discrepancies)))
    for d in result["Differences"]:
        print("DIFF: " + d)
    if discrepancies:
        sys.exit(1)


if __name__ == "__main__":
    main()
