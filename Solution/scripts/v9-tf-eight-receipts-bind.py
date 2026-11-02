#!/usr/bin/env python3
"""Bind eight physical TF model receipts to one immutable candidate universe.

No score is computed here. Physical runner must emit one receipt per model,
each carrying the actual candidate CSV SHA and exact 30k source count.
"""
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location(
    "v9_tf_input_preflight", HERE / "v9-tf-physical-input-preflight.py")
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


def bind_receipts(candidate_csv: Path, receipts: dict, source_count: int = 30000):
    contract = preflight.preflight(candidate_csv, source_count=source_count)
    expected = {}
    for pair in contract["Pairs"]:
        expected[pair["DiagnosticOptionId"]] = pair["BilateralOptionId"]
        expected[pair["BilateralOptionId"]] = pair["DiagnosticOptionId"]
    if not isinstance(receipts, dict) or set(receipts) != set(expected):
        raise ValueError("Exactly eight physical TF model receipts required")
    digest = contract["CandidatePairsSha256"]
    for option_id in expected:
        row = receipts[option_id]
        if (not isinstance(row, dict)
                or row.get("OptionId") != option_id
                or row.get("CandidatePairsSha256") != digest
                or type(row.get("SourceRecords")) is not int
                or row["SourceRecords"] != source_count
                or row.get("TestUsed") is not False
                or row.get("PhysicallyExecuted") is not True):
            raise ValueError("TF physical receipt missing or not bound to candidate SHA")
        for key in ("PhysicalRunSha256", "FittedModelSha256"):
            value = row.get(key)
            if (not isinstance(value, str) or len(value) != 64
                    or any(ch not in "0123456789abcdef" for ch in value)):
                raise ValueError("Missing physical evidence hash: " + key)
    return {"ContractVersion": "V9_TF_30K_EIGHT_RECEIPTS_BOUND_V1",
            "CandidatePairsSha256": digest, "SourceRecords": source_count,
            "PhysicalReceiptsVerified": 8, "TestUsed": False,
            "Receipts": receipts}


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-csv", type=Path, required=True)
    parser.add_argument("--receipts", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    def unique(pairs):
        obj = {}
        for key, value in pairs:
            if key in obj:
                raise ValueError("Duplicate JSON key: " + key)
            obj[key] = value
        return obj
    def reject(token):
        raise ValueError("Nonfinite JSON constant: " + token)
    receipts = json.loads(args.receipts.read_text(encoding="utf-8"),
                          object_pairs_hook=unique, parse_constant=reject)
    result = bind_receipts(args.candidate_csv, receipts)
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, allow_nan=False)
        out.write("\n")


if __name__ == "__main__":
    main()
