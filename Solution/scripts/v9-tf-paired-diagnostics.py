#!/usr/bin/env python3
"""Fail-closed contract for V9 shared-surname diagnostic comparisons.

This prepares a frozen pairing manifest, NOT a physical result. Each physical
runner must attest identical candidate-pair SHA for diagnostic and control.
"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path

GRID = Path(__file__).with_name("v9-tf-option-grid.py")
spec = importlib.util.spec_from_file_location("v9_tf_grid", GRID)
grid = importlib.util.module_from_spec(spec)
spec.loader.exec_module(grid)


def paired_manifest(candidate_pairs_sha256: str, *, alpha=0.5, threshold=0.95):
    if (not isinstance(candidate_pairs_sha256, str)
            or len(candidate_pairs_sha256) != 64
            or any(c not in "0123456789abcdef" for c in candidate_pairs_sha256)):
        raise ValueError("Exact lowercase SHA256 of frozen candidate pairs required")
    options = grid.option_grid(alpha=alpha, threshold=threshold)
    eligible = {x["OptionId"]: x for x in options
                if not x["OptionId"].startswith("TF_SHARED_PL_ML_")}
    diagnostics = [x for x in options if x["OptionId"].startswith("TF_SHARED_PL_ML_")]
    if len(eligible) != 41 or len(diagnostics) != 4:
        raise ValueError("Invalid TF selection/diagnostic partition")
    pairs = []
    for diagnostic in diagnostics:
        control_id = diagnostic["OptionId"].replace(
            "TF_SHARED_PL_ML_", "TF_TWO_PL_ML_", 1)
        control = eligible.get(control_id)
        if control is None or diagnostic["UnitTfOptions"] != control["UnitTfOptions"]:
            raise ValueError("Missing identical bilateral TF control")
        if (diagnostic.get("SharedSurnameMode") != 1
                or control.get("SharedSurnameMode", 0) != 0):
            raise ValueError("Diagnostic differs beyond shared-surname mode")
        pairs.append({
            "DiagnosticOptionId": diagnostic["OptionId"],
            "BilateralOptionId": control_id,
            "CandidatePairsSha256": candidate_pairs_sha256,
            "EligibleForAutomaticSelection": False,
            "RequiredPhysicalMetrics": [
                "SharedSurnameCandidatePairs", "Recall", "Fdr",
                "FalseNewIdentityCreations", "PendingDecisions"],
        })
    return {
        "ContractVersion": "V9_TF_SHARED_SURNAME_PAIRED_DIAGNOSTIC_V1",
        "CandidatePairsSha256": candidate_pairs_sha256,
        "EligibleTfModels": 41,
        "DiagnosticModels": 4,
        "TestUsed": False,
        "PhysicallyExecuted": False,
        "Pairs": pairs,
    }


def verify_receipts(manifest: dict, receipts: dict):
    """Fail closed on incomplete, unpaired, or numerically invalid physical results.

    Physical runner is responsible for producing receipts; this verifier never
    claims the preregistration itself is physical evidence.
    """
    if not isinstance(manifest, dict) or manifest.get("PhysicallyExecuted") is not False:
        raise ValueError("Expected preregistered, not pre-certified, manifest")
    expected = manifest["Pairs"]
    if not isinstance(receipts, dict) or set(receipts) != {
            p["DiagnosticOptionId"] for p in expected}:
        raise ValueError("Missing/extra diagnostic physical receipts")
    count_fields = ("SharedSurnameCandidatePairs", "FalseNewIdentityCreations",
                    "PendingDecisions")
    ratio_fields = ("Recall", "Fdr")
    for pair in expected:
        record = receipts[pair["DiagnosticOptionId"]]
        if (not isinstance(record, dict)
                or record.get("DiagnosticOptionId") != pair["DiagnosticOptionId"]
                or record.get("BilateralOptionId") != pair["BilateralOptionId"]
                or record.get("DiagnosticCandidatePairsSha256") != pair["CandidatePairsSha256"]
                or record.get("BilateralCandidatePairsSha256") != pair["CandidatePairsSha256"]):
            raise ValueError("Physical diagnostic/control candidates differ")
        for name in count_fields:
            value = record.get(name)
            if type(value) is not int or value < 0:
                raise ValueError(f"Missing/invalid physical count: {name}")
        for name in ratio_fields:
            value = record.get(name)
            if (type(value) not in (float, int) or not math.isfinite(value)
                    or not 0 <= value <= 1):
                raise ValueError(f"Missing/invalid physical ratio: {name}")
    return True
