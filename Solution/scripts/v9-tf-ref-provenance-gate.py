#!/usr/bin/env python3
"""Verify synthetic 30k generated files against the immutable reference-layer ledger.

No SQL writes, no source names in output and no approval of a model.
Called BEFORE the non-operational ref schema migration in disposable Harness.
"""
import argparse
import importlib.util
from pathlib import Path

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("v9_pin_for_ref", HERE / "v9-tf-pin-source.py")
pin_source = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pin_source)


def validate(reference: dict, pinned: dict, generated: dict, bootstrap: dict) -> None:
    if (reference.get("schema_version") != "JORNADA_V9_SYNTHETIC_30K_REF_EVIDENCE_V1"
        or reference.get("origin") != "SINTETICO_IBGE"
        or reference.get("population") != 30000
        or reference.get("seed") != 42
        or reference.get("generator_version") != "JORNADA_SYNTH_CORPUS_CSHARP_V1"
        or reference.get("error_profile") != "clean"
        or reference.get("restrictions", {}).get("not_operational") is not True
        or reference.get("restrictions", {}).get("raw_files_in_git") is not False):
        raise ValueError("Invalid synthetic reference-layer registry contract")
    publication = reference["ibge_reference"]
    if (pinned.get("PinnedIbgeReferenceVersionId") != publication["version_id_in_disposable_harness"]
        or pinned.get("PinnedIbgePublishedSha256", "").upper() != publication["published_sha256"]
        or pinned.get("PinnedIbgeMaterializedFrequencySha256", "").upper()
        != publication["materialized_tf_sha256"]):
        raise ValueError("Pinned IBGE reference differs from synthetic ref registry")
    if (bootstrap["CorpusFingerprint"].upper()
        != reference["generated_files"][0]["sha256"]
        or bootstrap["GenerationHypothesesFingerprint"].upper() != reference["generation_manifest_sha256"]
        or bootstrap["GeneratorVersion"] != reference["generator_version"]
        or bootstrap["GeneratorSeed"] != 42 or bootstrap["People"] != 30000
        or bootstrap["Provenance"] != "SINTETICO_IBGE"
        or pinned.get("PinnedIbgeBootstrap") != bootstrap):
        raise ValueError("Synthetic bootstrap differs from ref registry and live corpus")
    manifest_outputs = {x["path"]: x["sha256"].upper() for x in generated["outputs"]}
    reference_outputs = {x["file"]: x["sha256"] for x in reference["generated_files"]}
    if (manifest_outputs != reference_outputs
        or generated.get("input_fingerprint_sha256") != reference["generator_input_sha256"]
        or generated.get("reference_code") != publication["code"]
        or generated.get("error_profile") != reference["error_profile"]
        or generated.get("seed") != 42):
        raise ValueError("Generated synthetic evidence differs from ref registry")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--pin", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--write-candidate", type=Path, help="Create new candidate registry from authenticated physical bytes; never overwrite")
    args = parser.parse_args()
    generated = pin_source.parse_json(args.corpus_root / "generation-manifest.json")
    bootstrap = pin_source.corpus_bootstrap(args.corpus_root)
    pinned = pin_source.pinned_reference(pin_source.parse_json(args.pin))
    reference = pin_source.parse_json(args.reference)
    if args.write_candidate:
        import copy
        import json
        import re
        if not re.fullmatch(r"manifest-v[2-9][0-9]*\\.json", args.write_candidate.name):
            raise ValueError("Candidate must be named manifest-v2.json or higher")
        candidate = copy.deepcopy(reference)
        candidate["generation_manifest_sha256"] = pin_source.file_sha256(args.corpus_root / "generation-manifest.json").upper()
        candidate["generator_input_sha256"] = generated["input_fingerprint_sha256"].upper()
        candidate["generated_files"] = [
            {"file": name, "sha256": pin_source.file_sha256(args.corpus_root / name).upper()}
            for name in ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json")
        ]
        candidate["evidence"] = {
            "status": "CANDIDATE_REQUIRES_INDEPENDENT_PHYSICAL_REVIEW",
            "generator_commit_sha": "RECORD_VERIFIED_COMMIT_BEFORE_APPROVAL",
            "original_registry": "v9-synthetic-seed42-30000/manifest.json"
        }
        validate(candidate, pinned, generated, bootstrap)
        with args.write_candidate.open("x", encoding="utf-8") as stream:
            json.dump(candidate, stream, indent=2, ensure_ascii=False, allow_nan=False)
            stream.write("\\n")
        print("PASS: authenticated candidate created; not approved or active.")
        return
    validate(reference, pinned, generated, bootstrap)
    print("PASS: synthetic ref metadata, 30k hashes and immutable IBGE pin coincide; no names or SQL writes.")


if __name__ == "__main__":
    main()
