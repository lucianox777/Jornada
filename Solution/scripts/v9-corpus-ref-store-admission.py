#!/usr/bin/env python3
"""Combine REF namespace isolation with explicit durable-mount admission.

No mount or attestation is created by this command. This is not a cross-run
durability proof: independent run evidence must be supplied and audited.
"""
import argparse
import importlib.util
import json
from pathlib import Path


def load(filename, name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


namespace = load("v9-corpus-ref-store-path.py", "v9_ref_namespace")
external = load("v9-corpus-external-store-preflight.py", "v9_ref_external")


def admit(ref_root, official_ref, corpus_store, attestation_path, repository):
    isolation = namespace.validate(ref_root, corpus_store, official_ref)
    if attestation_path.is_symlink() or not attestation_path.is_file():
        raise ValueError("Missing or unsafe durable store attestation")
    attestation = json.loads(attestation_path.read_text(encoding="utf-8"))
    mount = external.validate(ref_root, attestation, repository)
    return {"status": "REF_CORPUS_STORE_ADMITTED_WITH_OPERATOR_ATTESTATION",
            "isolation": isolation, "mount": mount,
            "warning": "Attestation is operator-supplied; cross-run evidence must be independently audited"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ref-root", type=Path, required=True)
    parser.add_argument("--official-ref", type=Path, required=True)
    parser.add_argument("--corpus-store", type=Path, required=True)
    parser.add_argument("--attestation", type=Path, required=True)
    parser.add_argument("--repository", required=True)
    args = parser.parse_args()
    print(json.dumps(admit(args.ref_root, args.official_ref, args.corpus_store,
                           args.attestation, args.repository), sort_keys=True))


if __name__ == "__main__":
    main()
