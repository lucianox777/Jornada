#!/usr/bin/env python3
"""D: small physical SQL 1/0/8 deterministic-regression evidence (NOT 30k/FDR).

Create three declarations from one freshly generated TRAIN/VALIDATION fixture,
altering ONLY local MaximumParallelRuleSets. Compare the actual .NET-calibrator
scientific digest, fitted snapshots, source outcomes, selected scientific option,
and physical SQL candidate-pair SHA. Never inspect TEST/Gold or publish a fit.
"""
from __future__ import annotations
import argparse
import copy
import hashlib
import json
from pathlib import Path

MODES = (1, 0, 8)
SCHEMA = "V9_LOCAL_PHYSICAL_DETERMINISM_1_0_8_V1"


def read(path: Path) -> dict:
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate key in immutable local fixture")
            result[key] = value
        return result
    return json.loads(path.read_bytes(), object_pairs_hook=unique,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("nonfinite JSON")))


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def make_plans(source: Path, output: Path) -> dict:
    nested = read(source)
    plan = nested["Calibration"]
    if not isinstance(plan, dict) or not plan.get("StudyFingerprintSha256"):
        raise ValueError("Missing official CSharp adaptive Calibration declaration")
    if not nested.get("Purpose") == "PLUMBING_FIXTURE_NOT_CERTIFICATION":
        raise ValueError("Only small technical fixture, never scientific/TEST/Gold")
    if any(float(o.get("TfWeight", 0)) != 0 for o in plan["ModelOptions"]):
        raise ValueError("No IBGE/positive TF or alternate scientific arm allowed")
    if plan["Execution"]["EndToEndRepetitions"] < 20:
        raise ValueError("Original official execution P95 protocol is missing")
    # This is a determinism fixture. Runtime P95 remains an operational gate in
    # production; disable the expensive timing repetitions in ALL three runs.
    science = copy.deepcopy(plan)
    science["Execution"]["EndToEndRepetitions"] = 0
    # Contract: physical fixture has exactly one blocking catalogue
    # and a 3 x 2 grid; never silently sample a larger search.
    if (len(science["DeclaredSearchSpace"]["Catalogue"]) != 1 or
        len(science["ModelOptions"]) != 3 or
        len(science["ConflictMargins"]) != 2 or
        science["BaselineRuleSets"]):
        raise ValueError("Unexpected calibration fixture dimensions")
    output.mkdir(parents=True, exist_ok=False)
    out = {}
    for mode in MODES:
        run = copy.deepcopy(science)
        run["MaximumParallelRuleSets"] = mode
        path = output / f"plan-{mode}.json"
        with path.open("x", encoding="utf8") as stream:
            json.dump(run, stream, indent=2, sort_keys=True, allow_nan=False)
            stream.write("\n")
        out[str(mode)] = {"planSha256": sha(path), "mode": mode}
    return out


def audit(output: Path, expected_sha: str) -> dict:
    if len(expected_sha) != 40 or any(ch not in "0123456789abcdef" for ch in expected_sha):
        raise ValueError("Exact source SHA is required")
    reports = []
    for mode in MODES:
        directory = output / f"results-{mode}"
        plan_path = output / f"plan-{mode}.json"
        plan = read(plan_path)
        runtime = read(directory / "runtime-diagnostics.json")
        digest = read(directory / "scientific-result-digest.json")
        study = read(directory / "study.json")
        if runtime.get("configuredParallelism") != mode or runtime.get("effectiveParallelism") is None:
            raise ValueError("Wrong physical runtime concurrency witness")
        if not runtime.get("peakWorkingSetBytes") or runtime.get("totalMilliseconds") is None:
            raise ValueError("Missing physical runtime telemetry")
        if study.get("SourceMeasurements") is None or not study["SourceMeasurements"]:
            raise ValueError("No measured TRAIN/VALIDATION source outcomes")
        if not study.get("FrozenConfigurations") or not study.get("CandidatePairsSha256ByRuleset"):
            raise ValueError("No real fitted snapshot or SQL candidate-pair fingerprint")
        if digest.get("schema") != "V9_SCIENTIFIC_RESULT_DIGEST_V1":
            raise ValueError("Missing canonical scientific digest")
        # Independently compare raw measured scientific counts and frozen-fit
        # digests. A broken digest implementation must NOT hide a real drift.
        fields = ("OptionId", "ConflictMargin", "CandidateGrouping",
                  "EligibleSourceObservationsBeforeBlocking",
                  "TrueAcceptedSourceObservations", "FalseAcceptedSourceObservations",
                  "Conflicts", "Unresolved", "ReasonsByResolveStatus",
                  "BonferroniComparedConfigurations", "ConditionalClusterCpFdrUpper")
        measures = []
        for row in study["SourceMeasurements"]:
            if any(key not in row for key in fields):
                raise ValueError("Missing scientific metric in physical study")
            measures.append({key: row[key] for key in fields})
        measures.sort(key=lambda x: (x["OptionId"], x["ConflictMargin"], x["CandidateGrouping"]))
        fits = []
        for fitted in study["FrozenConfigurations"]:
            snapshot = fitted.get("Snapshot")
            if not isinstance(snapshot, dict) or not snapshot.get("ContentSha256"):
                raise ValueError("Missing immutable fitted snapshot SHA")
            fits.append((fitted["OptionId"], fitted["ConflictMargin"], snapshot["ContentSha256"]))
        fits.sort()
        def canonical_hash(value):
            payload = json.dumps(value, separators=(",", ":"), sort_keys=True,
                                 allow_nan=False).encode("utf8")
            return hashlib.sha256(payload).hexdigest()
        proof = {"mode": mode, "digest": digest["sha256"],
                 "scientificRowsSha256": canonical_hash(measures),
                 "fittedSnapshotIndexSha256": canonical_hash(fits),
                 "train": study["TrainInputSha256"], "validation": study["ValidationInputSha256"],
                 "scientificOption": study["Pareto"]["ExploratorySuggestedOptionId"],
                 "candidatePairs": study["CandidatePairsSha256ByRuleset"],
                 "measured": len(study["SourceMeasurements"]),
                 "fitted": len(study["FrozenConfigurations"]),
                 "physicalStudySha256": sha(directory / "study.json"),
                 "runtimeTelemetrySha256": sha(directory / "runtime-diagnostics.json"),
                 "planSha256": sha(plan_path)}
        if (proof["train"] != digest["TrainInputSha256"] or
            proof["validation"] != digest["ValidationInputSha256"]):
            raise ValueError("Digest and physical study do not bind to same frame")
        if plan["MaximumParallelRuleSets"] != mode:
            raise ValueError("Mode switched after pre-declaration")
        reports.append(proof)
    constant = ("digest", "scientificRowsSha256", "fittedSnapshotIndexSha256",
                "train", "validation", "scientificOption",
                "candidatePairs", "measured", "fitted")
    for key in constant:
        if any(report[key] != reports[0][key] for report in reports[1:]):
            raise ValueError(f"Physical scientific difference in 1/0/8: {key}")
    receipt = {
        "schema": SCHEMA, "headSha": expected_sha,
        "status": "SAME_HOST_KNOWN_SEED42_1000_PHYSICAL_PARITY_NOT_CONFIRMATORY",
        "comparison": "ALL_3_CSHARP_SQL_STUDIES_ON_SAME_SYNTHETIC_FRAME",
        "scientificDigestSha256": reports[0]["digest"],
        "physicalModes": reports,
        "testUsed": False, "goldUsed": False,
        "statisticallyCertified": False, "operationallyActivated": False,
    }
    path = output / "determinism-physical-receipt.json"
    with path.open("x", encoding="utf8") as stream:
        json.dump(receipt, stream, indent=2, sort_keys=True, allow_nan=False)
        stream.write("\n")
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    prepare = sub.add_parser("prepare")
    prepare.add_argument("--source", type=Path, required=True)
    prepare.add_argument("--output", type=Path, required=True)
    compare = sub.add_parser("compare")
    compare.add_argument("--output", type=Path, required=True)
    compare.add_argument("--head", required=True)
    args = parser.parse_args()
    if args.command == "prepare":
        result = make_plans(args.source, args.output)
        print(json.dumps({"status": "PLANS_PREDECLARED_NOT_EXECUTED",
                          "modes": result}, sort_keys=True))
    else:
        result = audit(args.output, args.head)
        print(json.dumps({"status": result["status"],
                          "scientificDigestSha256": result["scientificDigestSha256"],
                          "headSha": result["headSha"]}, sort_keys=True))


if __name__ == "__main__":
    main()
