#!/usr/bin/env python3
"""Fail-closed diagnostic gate: compare receipts only after checking both artifact bytes.

Does not attest that either artifact executed, nor certify production parity.
"""
import argparse
import importlib.util
import json
from pathlib import Path


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def evaluate(scientific_receipt, scientific_artifact, production_receipt, production_artifact):
    if Path(scientific_artifact).resolve() == Path(production_artifact).resolve():
        raise ValueError("Independent runtimes cannot share the same artifact path")
    verifier = load_module("v9_verify", "v9-differential-verify-artifacts.py")
    comparator = load_module("v9_compare", "v9-differential-receipt-compare.py")
    left = verifier.verify(scientific_receipt, scientific_artifact)
    right = verifier.verify(production_receipt, production_artifact)
    comparison = comparator.compare(scientific_receipt, production_receipt)
    return {**comparison, "artifactsVerified": left["verified"] and right["verified"],
            "scientificArtifactSha256": left["artifactSha256"],
            "productionArtifactSha256": right["artifactSha256"],
            "certified": False, "productionParityCertified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("scientific_receipt", "scientific_artifact", "production_receipt", "production_artifact"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    result = evaluate(json.loads(args.scientific_receipt.read_text(encoding="utf-8")),
                      args.scientific_artifact,
                      json.loads(args.production_receipt.read_text(encoding="utf-8")),
                      args.production_artifact)
    print(json.dumps(result, sort_keys=True))
    if not result["equivalent"] or not result["artifactsVerified"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
