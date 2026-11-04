#!/usr/bin/env python3
"""Fail-closed preflight for a NEW physical seed-43 TF transfer; never runs SQL/TEST.

The historical seed-42 pin and its registered corpus cannot be reused as
seed-43 corpus provenance. This tool only verifies a separately generated
corpus, an IBGE reference pin, and already-frozen C# snapshot bytes.
"""
import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("v9_tf_seed43_pin_source", HERE / "v9-tf-pin-source.py")
pin_source = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pin_source)

VERSION = "V9_TF_SEED43_FROZEN_TRANSFER_PREFLIGHT_V1"
EXPECTED_OPTIONS = {"T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95"}
FILES = {"pessoas_verdade.csv", "observacoes.csv", "gabarito.json"}


def sha256(path):
    return pin_source.file_sha256(path).lower()


def digest(value, length=64):
    return isinstance(value, str) and len(value) == length and all(c in "0123456789abcdefABCDEF" for c in value)


def check_plan(plan, snapshots):
    required = {"schema_version", "source_seed", "target_seed", "transfer_mode",
                "source_commit_sha", "source_study_sha256", "source_run_id",
                "frozen_models", "target_fdr_max", "test_sealed", "margins"}
    if not isinstance(plan, dict) or set(plan) != required:
        raise ValueError("Plan fields must be complete and frozen before target corpus")
    if (plan["schema_version"] != VERSION or type(plan["source_seed"]) is not int
        or plan["source_seed"] != 42 or type(plan["target_seed"]) is not int
        or plan["target_seed"] != 43 or plan["transfer_mode"] != "FROZEN_NO_REFIT"
        or type(plan["source_run_id"]) is not int or plan["source_run_id"] < 1
        or not digest(plan["source_commit_sha"], 40)
        or not digest(plan["source_study_sha256"])
        or type(plan["target_fdr_max"]) is not float
        or plan["target_fdr_max"] != 0.01
        or plan["test_sealed"] is not True
        or plan["margins"] != [0.5, 2, 4]):
        raise ValueError("Invalid frozen transfer protocol or operational FDR target")
    models = plan["frozen_models"]
    if not isinstance(models, list) or len(models) != 2 or any(
        not isinstance(m, dict) or set(m) != {"option_id", "snapshot_sha256"}
        or m["option_id"] not in EXPECTED_OPTIONS or not digest(m["snapshot_sha256"])
        for m in models
    ) or {m["option_id"] for m in models} != EXPECTED_OPTIONS:
        raise ValueError("Frozen TF0 and ONE_MF snapshots must both be declared")
    for item in models:
        expected = item["snapshot_sha256"].lower()
        path = snapshots / (expected + ".fitted.json")
        if not path.is_file() or sha256(path) != expected:
            raise ValueError("Frozen source snapshot missing or SHA-256 mismatch")


def check_corpus(root, registry):
    manifest_path = root / "generation-manifest.json"
    manifest = pin_source.parse_json(manifest_path)
    expected = {"schema_version": 1, "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
                "seed": 43, "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
                "error_profile": "clean", "reference_code": "CENSO2022_NOMES_BRASIL_V1"}
    if not isinstance(manifest, dict) or any(
        type(manifest.get(k)) is not type(v) or manifest[k] != v for k, v in expected.items()
    ) or not digest(manifest.get("input_fingerprint_sha256")):
        raise ValueError("Seed-43 corpus manifest violates generator/profile contract")
    cap = manifest.get("observation_multiplicity")
    if (not isinstance(cap, dict)
        or cap.get("contract_version") != "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1"
        or type(cap.get("maximum_observations_per_base_identity")) is not int
        or cap["maximum_observations_per_base_identity"] != 4):
        raise ValueError("Missing source-identity observation cap")
    items = manifest.get("outputs")
    if not isinstance(items, list) or len(items) != 3:
        raise ValueError("Invalid generated output set")
    by_path = {}
    for item in items:
        if (not isinstance(item, dict) or set(item) != {"path", "sha256"}
            or item["path"] not in FILES or item["path"] in by_path
            or not digest(item["sha256"])):
            raise ValueError("Invalid, repeated or unrecognized corpus hash")
        name = item["path"]
        if sha256(root / name) != item["sha256"].lower():
            raise ValueError("Generated corpus bytes differ from manifest")
        by_path[name] = item["sha256"].lower()
    if set(by_path) != FILES:
        raise ValueError("Missing corpus output")
    if (registry.get("seed") != 42 or registry.get("population") != 30000
        or registry.get("generation_manifest_sha256", "").lower() == sha256(manifest_path)
        or registry.get("generator_input_sha256", "").lower()
           == manifest["input_fingerprint_sha256"].lower()
        or any(item["sha256"].lower() == by_path.get(item["file"])
               for item in registry.get("generated_files", []) if item.get("file") in FILES)):
        raise ValueError("Seed-42 registry cannot be reused as seed-43 corpus provenance")

    # Only partition membership and stable base IDs are inspected. No TEST
    # labels, resolved outcomes, training data, or names are exported.
    seen, counts = set(), {"TRAIN": 0, "VALIDATION": 0, "TEST": 0}
    with (root / "pessoas_verdade.csv").open(encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames or not {"base_person_id", "particao"}.issubset(reader.fieldnames):
            raise ValueError("Missing base-person partition columns")
        for row in reader:
            identity, split = row["base_person_id"], row["particao"]
            if not identity or identity in seen or split not in counts:
                raise ValueError("Duplicate base identity or mixed partitions")
            seen.add(identity)
            counts[split] += 1
    if len(seen) != 30000 or not all(counts.values()):
        raise ValueError("30k independent base IDs and three nonempty partitions required")
    bootstrap = {"Provenance": "SINTETICO_IBGE", "People": 30000,
                 "CorpusFingerprint": by_path["pessoas_verdade.csv"],
                 "GeneratorVersion": manifest["generator_version"],
                 "GeneratorSeed": 43,
                 "GenerationHypothesesFingerprint": sha256(manifest_path)}
    return bootstrap, counts, by_path


def verify(plan_path, corpus_root, registry_path, pin_path, snapshots, source_study):
    plan = pin_source.parse_json(plan_path)
    check_plan(plan, snapshots)
    if not source_study.is_file() or sha256(source_study) != plan["source_study_sha256"].lower():
        raise ValueError("Seed-42 source study missing or SHA-256 mismatch")
    registry = pin_source.parse_json(registry_path)
    bootstrap, counts, output_shas = check_corpus(corpus_root, registry)
    pinned = pin_source.pinned_reference(pin_source.parse_json(pin_path))
    source_ibge = registry.get("ibge_reference")
    if (not isinstance(source_ibge, dict)
        or pinned["PinnedIbgeReferenceVersionId"] != source_ibge.get("version_id_in_disposable_harness")
        or pinned["PinnedIbgePublishedSha256"].lower() != source_ibge.get("published_sha256", "").lower()
        or pinned["PinnedIbgeMaterializedFrequencySha256"].lower() != source_ibge.get("materialized_tf_sha256", "").lower()
        or pinned["PinnedIbgeBootstrap"] != bootstrap):
        raise ValueError("IBGE reference differs or target seed-43 bootstrap was reused/tampered")
    return {
        "version": VERSION, "status": "PREFLIGHT_ONLY_NO_SQL_TF_RESULTS_NO_CERTIFICATION",
        "transfer_mode": plan["transfer_mode"], "source_seed": 42, "target_seed": 43,
        "plan_sha256": sha256(plan_path),
        "generator_manifest_sha256": bootstrap["GenerationHypothesesFingerprint"],
        "generated_output_sha256": output_shas, "source_snapshots": plan["frozen_models"],
        "ibge_published_sha256": pinned["PinnedIbgePublishedSha256"],
        "ibge_tf_sha256": pinned["PinnedIbgeMaterializedFrequencySha256"],
        "base_identities_by_partition": counts, "test_outcomes_inspected": False,
        "physical_sql_executed": False, "iid_independently_validated": False,
        "operational_authorization": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("plan", "corpus-root", "seed42-registry", "seed43-pin", "frozen-snapshots", "source-study", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    a = parser.parse_args()
    report = verify(a.plan, a.corpus_root, a.seed42_registry, a.seed43_pin, a.frozen_snapshots, a.source_study)
    with a.output.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, sort_keys=True, allow_nan=False)
        stream.write("\n")
    print("PASS: independent seed-43 provenance + frozen TF0/ONE_MF snapshots; no physical run or TEST.")


if __name__ == "__main__":
    main()
