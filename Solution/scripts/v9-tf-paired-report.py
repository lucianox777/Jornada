#!/usr/bin/env python3
"""Summarize already measured V9 TF diagnostic/control receipts; no simulated metrics."""
import json
import math
from pathlib import Path


FIELDS = ("SharedSurnameCandidatePairs", "Recall", "Fdr",
          "FalseNewIdentityCreations", "PendingDecisions")


def _metrics(raw):
    if not isinstance(raw, dict) or set(FIELDS) - set(raw):
        raise ValueError("Missing paired physical metrics")
    for name in ("SharedSurnameCandidatePairs", "FalseNewIdentityCreations",
                 "PendingDecisions"):
        if type(raw[name]) is not int or raw[name] < 0:
            raise ValueError("Invalid physical count: " + name)
    for name in ("Recall", "Fdr"):
        value = raw[name]
        if type(value) not in (float, int) or not math.isfinite(value) or not 0 <= value <= 1:
            raise ValueError("Invalid physical ratio: " + name)
    return {name: raw[name] for name in FIELDS}


def report(manifest, receipts):
    if manifest.get("ContractVersion") != "V9_TF_SHARED_SURNAME_PAIRED_DIAGNOSTIC_V1":
        raise ValueError("Unsupported paired contract")
    if (manifest.get("PhysicallyExecuted") is not False
            or manifest.get("TestUsed") is not False
            or manifest.get("EligibleTfModels") != 41
            or manifest.get("DiagnosticModels") != 4):
        raise ValueError("Unfrozen or ineligible TF diagnostic manifest")
    pairs = manifest.get("Pairs")
    if not isinstance(pairs, list) or len(pairs) != 4:
        raise ValueError("Exactly four diagnostics required")
    if len({p["DiagnosticOptionId"] for p in pairs}) != 4 or len({p["BilateralOptionId"] for p in pairs}) != 4:
        raise ValueError("Duplicate diagnostic or bilateral model")
    if not isinstance(receipts, dict) or set(receipts) != {
            p["DiagnosticOptionId"] for p in pairs}:
        raise ValueError("Missing/extra physical receipts")
    rows = []
    for pair in pairs:
        row = receipts[pair["DiagnosticOptionId"]]
        if (row.get("DiagnosticOptionId") != pair["DiagnosticOptionId"]
                or row.get("BilateralOptionId") != pair["BilateralOptionId"]):
            raise ValueError("Mismatched TF pair")
        sha = pair["CandidatePairsSha256"]
        if sha != manifest.get("CandidatePairsSha256"):
            raise ValueError("Pair candidate SHA differs from frozen manifest")
        if (row.get("DiagnosticCandidatePairsSha256") != sha
                or row.get("BilateralCandidatePairsSha256") != sha):
            raise ValueError("Unpaired physical candidate sets")
        diagnostic = _metrics(row.get("Diagnostic"))
        control = _metrics(row.get("Bilateral"))
        rows.append({
            "DiagnosticOptionId": pair["DiagnosticOptionId"],
            "BilateralOptionId": pair["BilateralOptionId"],
            "CandidatePairsSha256": sha,
            "Diagnostic": diagnostic, "Bilateral": control,
            "DeltaDiagnosticMinusBilateral": {
                name: diagnostic[name] - control[name] for name in FIELDS},
        })
    return {"ContractVersion": "V9_TF_SHARED_SURNAME_PAIRED_REPORT_V1",
            "ReceiptClaimsValidated": True, "PhysicallyMeasured": False,
            "EligibleForAutomaticSelection": False,
            "Pairs": rows}


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--receipts", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    def load(path):
        def unique(pairs):
            obj = {}
            for k, v in pairs:
                if k in obj:
                    raise ValueError("Duplicate JSON key: " + k)
                obj[k] = v
            return obj
        def reject(token):
            raise ValueError("Nonfinite JSON: " + token)
        return json.loads(path.read_text(encoding="utf-8"),
                          object_pairs_hook=unique, parse_constant=reject)
    result = report(load(args.manifest), load(args.receipts))
    with args.output.open("x", encoding="utf-8") as output:
        json.dump(result, output, indent=2, allow_nan=False)
        output.write("\n")


if __name__ == "__main__":
    main()
