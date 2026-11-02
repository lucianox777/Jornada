#!/usr/bin/env python3
"""Preflight for physical 30k TF diagnostic execution, not a synthetic runner.

Takes the actual candidate-pair CSV emitted by the physical blocking stage.
The same immutable SHA is pinned for all eight model evaluations.
"""
import csv
import hashlib
import importlib.util
import json
from pathlib import Path

GRID = Path(__file__).with_name("v9-tf-option-grid.py")
spec = importlib.util.spec_from_file_location("v9_tf_grid", GRID)
grid = importlib.util.module_from_spec(spec)
spec.loader.exec_module(grid)


def preflight(candidate_csv: Path, *, source_count: int, alpha=.5, threshold=.95):
    if type(source_count) is not int or source_count != 30000:
        raise ValueError("Physical TF diagnostics require exactly 30k source records")
    digest = hashlib.sha256(candidate_csv.read_bytes()).hexdigest()
    with candidate_csv.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames or set(reader.fieldnames) != {
                "LeftStableId", "RightStableId", "SharedSurname"}:
            raise ValueError("Physical candidate CSV must have exact stable-ID schema")
        seen = set()
        shared = 0
        source_ids = set()
        for row in reader:
            left, right = row["LeftStableId"], row["RightStableId"]
            if not left or not right or left == right:
                raise ValueError("Missing, self or invalid candidate pair")
            if row["SharedSurname"] not in ("0", "1"):
                raise ValueError("SharedSurname must be explicit 0 or 1")
            pair = tuple(sorted((left, right)))
            if pair in seen:
                raise ValueError("Repeated candidate pair")
            seen.add(pair)
            source_ids.update(pair)
            shared += row["SharedSurname"] == "1"
    if not seen:
        raise ValueError("No physically produced candidate pairs")
    if len(source_ids) > source_count:
        raise ValueError("Candidate pairs reference more stable IDs than declared sources")
    options = grid.option_grid(alpha=alpha, threshold=threshold)
    by_id = {x["OptionId"]: x for x in options}
    diagnostics = [x for x in options if x["OptionId"].startswith("TF_SHARED_PL_ML_")]
    if len(options) != 45 or len(diagnostics) != 4:
        raise ValueError("Wrong preregistered TF family")
    evaluations = []
    for diagnostic in diagnostics:
        control_id = diagnostic["OptionId"].replace(
            "TF_SHARED_PL_ML_", "TF_TWO_PL_ML_", 1)
        control = by_id.get(control_id)
        if (control is None or control["UnitTfOptions"] != diagnostic["UnitTfOptions"]
                or control.get("SharedSurnameMode", 0) != 0
                or diagnostic.get("SharedSurnameMode") != 1):
            raise ValueError("Missing identical bilateral control")
        evaluations.append({
            "DiagnosticOptionId": diagnostic["OptionId"],
            "BilateralOptionId": control_id,
            "CandidatePairsSha256": digest,
            "CandidatePairs": len(seen),
            "SharedSurnameCandidatePairs": shared,
            "EligibleForAutomaticSelection": False})
    return {"ContractVersion": "V9_TF_30K_PHYSICAL_INPUT_PREFLIGHT_V1",
            "SourceRecords": source_count, "CandidatePairsSha256": digest,
            "PhysicallyExecuted": False, "TestUsed": False,
            "RequiredModelEvaluations": 8, "Pairs": evaluations}


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-csv", required=True, type=Path)
    parser.add_argument("--source-count", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = preflight(args.candidate_csv, source_count=args.source_count)
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, allow_nan=False)
        out.write("\n")


if __name__ == "__main__":
    main()
