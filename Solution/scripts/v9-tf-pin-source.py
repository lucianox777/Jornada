#!/usr/bin/env python3
"""Authenticates the real generated 30k corpus and builds/verifies its TF pin.

No calibration, SQL reads, source records or personal names are exported.
"""
import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON field: " + key)
        result[key] = value
    return result


def reject_nonfinite(value):
    raise ValueError("Nonfinite JSON value: " + value)


def parse_json(path):
    return json.loads(Path(path).read_bytes(), object_pairs_hook=unique_object,
                      parse_constant=reject_nonfinite)


def file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def is_sha256(value):
    return isinstance(value, str) and len(value) == 64 and all(
        c in "0123456789abcdefABCDEF" for c in value)


def corpus_bootstrap(root):
    root = Path(root)
    manifest_path = root / "generation-manifest.json"
    manifest = parse_json(manifest_path)
    required = {
        "schema_version": 1,
        "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
        "seed": 42,
        "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
        "error_profile": "clean",
        "reference_code": "CENSO2022_NOMES_BRASIL_V1",
    }
    if not isinstance(manifest, dict) or any(
        type(manifest.get(k)) is not type(v) or manifest[k] != v for k, v in required.items()
    ):
        raise ValueError("Unsupported 30k generation manifest or profile")
    cap = manifest.get("observation_multiplicity")
    if not isinstance(cap, dict) or cap.get("contract_version") != "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1" or (
        type(cap.get("maximum_observations_per_base_identity")) is not int
        or cap["maximum_observations_per_base_identity"] != 4
    ):
        raise ValueError("Missing canonical base multiplicity contract")
    if not is_sha256(manifest.get("input_fingerprint_sha256")):
        raise ValueError("Missing generator input fingerprint")
    outputs = manifest.get("outputs")
    if not isinstance(outputs, list) or len(outputs) != 3:
        raise ValueError("Incomplete 30k generation output manifest")
    by_path = {}
    for item in outputs:
        if not isinstance(item, dict) or set(item) != {"path", "sha256"}:
            raise ValueError("Invalid generation output entry")
        name, digest = item["path"], item["sha256"]
        if name in by_path or name not in {
            "pessoas_verdade.csv", "observacoes.csv", "gabarito.json"
        } or not is_sha256(digest):
            raise ValueError("Duplicated or unrecognized generation output")
        if file_sha256(root / name).lower() != digest.lower():
            raise ValueError("Generated output hash mismatch: " + name)
        by_path[name] = digest.lower()
    if len(by_path) != 3:
        raise ValueError("Missing generated output")

    truth = parse_json(root / "gabarito.json")
    if (type(truth.get("people")) is not int or truth["people"] != 30000
            or truth.get("generator_version") != manifest["generator_version"]
            or truth.get("seed") != 42 or truth.get("error_profile") != "clean"
            or truth.get("population_profile") != "DEMOGRAPHIC_PRIMARY_V1"
            or truth.get("input_fingerprint_sha256", "").lower()
            != manifest["input_fingerprint_sha256"].lower()):
        raise ValueError("Corpus truth disagrees with generated manifest")

    count, identities, partitions = 0, set(), set()
    with (root / "pessoas_verdade.csv").open("r", newline="", encoding="utf-8") as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames or not {"base_person_id", "particao"}.issubset(reader.fieldnames):
            raise ValueError("Missing base-person columns")
        for row in reader:
            identity, partition = row["base_person_id"], row["particao"]
            if not identity or identity in identities or partition not in {
                "TRAIN", "VALIDATION", "TEST"
            }:
                raise ValueError("Invalid independent base identity or partition")
            identities.add(identity)
            partitions.add(partition)
            count += 1
    if count != 30000 or partitions != {"TRAIN", "VALIDATION", "TEST"}:
        raise ValueError("Corpus must contain 30000 distinct base people and all partitions")

    # These are two distinct facts: the source-person population bytes, and
    # the entire generator hypotheses/results declaration (which binds the
    # reference, inputs, profile, seed and hashes of ALL generated outputs).
    return {
        "Provenance": "SINTETICO_IBGE",
        "People": count,
        "CorpusFingerprint": by_path["pessoas_verdade.csv"],
        "GeneratorVersion": manifest["generator_version"],
        "GeneratorSeed": manifest["seed"],
        "GenerationHypothesesFingerprint": file_sha256(manifest_path),
    }


def pinned_reference(raw):
    spec = importlib.util.spec_from_file_location(
        "v9_tf_grid_pin", HERE / "v9-tf-option-grid.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    return helper.pinned_reference(raw)


def verify_pin(expected, actual):
    left = pinned_reference(expected)
    right = pinned_reference(actual)
    if left != right:
        raise ValueError("Pinned IBGE reference diverges after SQL/corpus reconstruction")
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("bootstrap", "verify"))
    parser.add_argument("--root", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--expected", type=Path)
    parser.add_argument("--actual", type=Path)
    args = parser.parse_args()
    if args.mode == "bootstrap":
        if args.root is None or args.output is None or args.expected or args.actual:
            parser.error("bootstrap requires --root and --output only")
        bootstrap = corpus_bootstrap(args.root)
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(bootstrap, stream, indent=2, allow_nan=False)
            stream.write("\n")
        print("PASS: corpus 30k source, generation hashes and bootstrap authenticated; no calibration.")
    else:
        if args.expected is None or args.actual is None or args.root or args.output:
            parser.error("verify requires --expected and --actual only")
        verify_pin(parse_json(args.expected), parse_json(args.actual))
        print("PASS: all four reconstructed SQL/IBGE/bootstrap fields match the pinned evidence.")


if __name__ == "__main__":
    main()
