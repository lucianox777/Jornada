#!/usr/bin/env python3
"""Fail-closed preflight for the V9 30k TF sensitivity experiment.

This does not pretend to score pairs. It freezes the intended comparison
and verifies that the historical TRAIN input is the exact original corpus.
"""
import argparse
import hashlib
import json
from pathlib import Path

EXPECTED_TRAIN_SHA256 = "28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17"
WEIGHTS = ["0", "0.025", "0.05", "0.10", "0.20", "0.40"]
FITTED = "0a3459585fcf3d20fbd0854e977b84a769127a9ab01ab0d72417dff42d0b3e5b.fitted.json"

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--train-csv", type=Path, required=True)
    parser.add_argument("--ibge-reference-id", type=int, required=True)
    parser.add_argument("--candidate-frame", type=Path, help="Previously materialized TRAIN/VALIDATION SQL candidate frame")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.ibge_reference_id <= 0:
        parser.error("IBGE reference version ID must be positive")
    root = Path(__file__).resolve().parents[1] / "data/calibration/v9/mass-support-20261020"
    snapshot = json.loads((root / FITTED).read_text(encoding="utf-8"))
    if snapshot["TfWeight"] != 0 or snapshot["TfCalibration"] is not None:
        raise SystemExit("Unexpected historical baseline: fail closed")
    if str(snapshot["Threshold"]) != "0.8671875" or str(snapshot["ConflictMargin"]) != "0.609375":
        raise SystemExit("Historical decision policy differs: fail closed")
    if not args.train_csv.is_file():
        raise SystemExit("TRAIN CSV missing: cannot certify or simulate")
    with args.train_csv.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    if digest != EXPECTED_TRAIN_SHA256:
        raise SystemExit(f"TRAIN CSV fingerprint mismatch: {digest}")
    if args.candidate_frame is None or not args.candidate_frame.is_file():
        raise SystemExit("Candidate frame missing: cannot compute conditional TRAIN U or TF decisions")
    if args.candidate_frame.stat().st_size == 0:
        raise SystemExit("Candidate frame empty: cannot evaluate TF decisions")
    with args.candidate_frame.open("rb") as source:
        frame_digest = hashlib.file_digest(source, "sha256").hexdigest()
    payload = {
        "status": "PREFLIGHT_ONLY_NOT_SCORED_NOT_CERTIFIED",
        "train_sha256": digest,
        "ibge_reference_version_id": args.ibge_reference_id,
        "historical_fitted_snapshot": FITTED,
        "candidate_frame": str(args.candidate_frame),
        "candidate_frame_sha256": frame_digest,
        "tf_weights": WEIGHTS,
        "smoothing_alpha": "0.1",
        "decision_threshold": "0.8671875",
        "conflict_margin": "0.609375",
        "requirements": [
            "Bind the explicitly pinned published IBGE reference to the exact blocking frame",
            "Fit conditional U on TRAIN candidate pairs only",
            "Score all six weights on the same untouched selection frame",
            "Do not reuse exposed external validation for TF selection",
            "Report per-weight TP, FP, recall, FDR, coverage and changed decisions",
            "Do not activate or certify any model from this experiment"
        ],
    }
    evidence_paths = (args.train_csv, args.candidate_frame, root / FITTED)
    if args.output.resolve() in {source.resolve() for source in evidence_paths}:
        raise SystemExit("Output must not overwrite any input evidence")
    if args.output.is_symlink():
        raise SystemExit("Output symlink forbidden: immutable evidence required")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, indent=2, ensure_ascii=False) + "\n")
    print(f"Verified historical input and wrote preflight: {args.output}")
    print("No TF scores calculated; scoring runner still required.")

if __name__ == "__main__":
    main()
