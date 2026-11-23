#!/usr/bin/env python3
"""Fail-closed DEV preflight for ZIPs from the existing IBGE-derived C# generator.

This validates aggregate provenance and package structure only. Never publish
the generated population, ZIPs or truth sidecars as GitHub Actions artifacts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import zipfile

EXPECTED_GESTORES = ("SEHAB", "SMADS", "SMDET", "SMS")
GENERATOR = "JORNADA_SYNTH_CORPUS_CSHARP_V1"
PROFILE = "DEMOGRAPHIC_PRIMARY_V1"
SEED = 355
PEOPLE = 1000


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def no_duplicate_keys(pairs):
    data = {}
    for key, value in pairs:
        require(key not in data, "Duplicate JSON key in synthetic E2E manifest")
        data[key] = value
    return data


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=no_duplicate_keys)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def verify(root: Path) -> dict:
    require(root.is_dir() and not root.is_symlink(), "Missing or symlinked IBGE DEV root")
    corpus_dir = root / "corpus"
    ingestion_dir = root / "ingestion"
    for item in (corpus_dir, ingestion_dir):
        require(item.is_dir() and not item.is_symlink(), "Invalid generator output directory")
    corpus = read_json(corpus_dir / "generation-manifest.json")
    truth = read_json(corpus_dir / "gabarito.json")
    bridge_file = ingestion_dir / "bridge-manifest.json"
    bridge = read_json(bridge_file)
    require(corpus.get("generator_version") == GENERATOR, "Unexpected C# generator version")
    require(corpus.get("population_profile") == PROFILE, "Must reuse demographic IBGE model")
    require(type(corpus.get("seed")) is int and corpus["seed"] == SEED, "Unexpected DEV seed")
    require(truth.get("generator_version") == GENERATOR
            and truth.get("population_profile") == PROFILE
            and truth.get("seed") == SEED
            and truth.get("people") == PEOPLE, "Invalid corpus truth provenance")
    reference = corpus.get("reference_code")
    require(isinstance(reference, str) and reference.strip(), "Missing published reference ID")
    fingerprint = corpus.get("input_fingerprint_sha256")
    require(isinstance(fingerprint, str) and len(fingerprint) == 64
            and all(c in "0123456789ABCDEF" for c in fingerprint.upper()),
            "Invalid source fingerprint")
    require(truth.get("input_fingerprint_sha256", "").upper() == fingerprint.upper(),
            "Corpus truth source drift")
    require(bridge.get("corpusInputFingerprintSha256") == fingerprint.upper(),
            "Bridge/corpus source drift")
    require(bridge.get("generatorVersion") == GENERATOR
            and bridge.get("bridgeVersion") == "SYNTHETIC_INGESTION_BRIDGE_V1",
            "Unexpected bridge version")
    source = bridge.get("sourceObservationCount")
    materialized = bridge.get("materializedObservationCount")
    excluded = bridge.get("excludedObservationCount")
    require(type(source) is int and PEOPLE <= source <= PEOPLE * 4
            and source == truth.get("observations")
            and type(materialized) is int and materialized == source
            and type(excluded) is int and excluded == 0,
            "Lossy bridge or nonmatching identity counts")
    sidecar = bridge.get("truthSidecar") or {}
    require(sidecar.get("path") == "bridge-truth.jsonl"
            and sidecar.get("containsSyntheticGroundTruth") is True
            and sidecar.get("allowedForScoring") is False,
            "Ground truth must remain separate and inaccessible to scoring")
    truth_file = ingestion_dir / "bridge-truth.jsonl"
    require(truth_file.is_file() and not truth_file.is_symlink()
            and sha256(truth_file) == sidecar.get("sha256"),
            "Missing or modified sidecar truth")
    sidecar_lines = truth_file.read_text(encoding="utf-8").splitlines()
    require(len(sidecar_lines) == source, "Ground-truth row count mismatch")
    truth_by_opaque = {}
    base_ids = set()
    observation_ids = set()
    for raw_row in sidecar_lines:
        row = json.loads(raw_row, object_pairs_hook=no_duplicate_keys)
        require(isinstance(row, dict) and row.get("Status") == "MATERIALIZADA"
                and row.get("ExclusionReason") is None
                and row.get("Partition") in ("TRAIN", "VALIDATION", "TEST"),
                "Invalid synthetic bridge truth semantics")
        require(all(isinstance(row.get(key), str) and row[key].strip()
                    for key in ("BasePersonId", "ObservationId", "OpaquePersonId",
                                "GestorCodigo", "PackageFileName")),
                "Incomplete bridge truth row")
        opaque = row["OpaquePersonId"]
        observation = row["ObservationId"]
        require(opaque.startswith("SYNTH-") and opaque not in truth_by_opaque
                and observation not in observation_ids,
                "Duplicated source observation or opaque identity")
        truth_by_opaque[opaque] = (row["GestorCodigo"], row["PackageFileName"])
        observation_ids.add(observation)
        base_ids.add(row["BasePersonId"])
    require(len(base_ids) == PEOPLE,
            "Synthetic generator lost or duplicated base identities")

    packages = bridge.get("packages")
    require(isinstance(packages, list) and len(packages) == 4,
            "Expected four manager-specific packages")
    expected_sha = {}
    seen_opaque = set()
    total = 0
    for entry in packages:
        require(isinstance(entry, dict), "Invalid package entry")
        gestor = entry.get("GestorCodigo")
        filename = entry.get("FileName")
        count = entry.get("PeopleCount")
        digest = entry.get("sha256")
        require(gestor in EXPECTED_GESTORES and gestor not in expected_sha,
                "Missing or repeated manager")
        require(isinstance(filename, str) and filename == Path(filename).name
                and filename.endswith(".zip") and "/" not in filename
                and "\\" not in filename, "Unsafe package path")
        require(type(count) is int and 1 <= count <= PEOPLE, "Invalid per-manager count")
        require(isinstance(digest, str) and len(digest) == 64
                and all(c in "0123456789ABCDEF" for c in digest.upper()),
                "Invalid ZIP digest")
        package = ingestion_dir / filename
        require(package.is_file() and not package.is_symlink()
                and sha256(package) == digest.upper(), "ZIP bytes differ from bridge receipt")
        with zipfile.ZipFile(package, "r") as archive:
            require(archive.namelist() == ["manifest.json", "pessoas.jsonl", "registros.jsonl"],
                    "Unexpected ZIP members, possible truth leak")
            require(not archive.read("registros.jsonl"), "Person-only bridge may not fabricate facts")
            rows = archive.read("pessoas.jsonl").splitlines()
            require(len(rows) == count, "ZIP materialized person count mismatch")
            for row in rows:
                obj = json.loads(row, object_pairs_hook=no_duplicate_keys)
                require(isinstance(obj, dict) and isinstance(obj.get("idPessoaEntrega"), str)
                        and obj["idPessoaEntrega"].startswith("SYNTH-"),
                        "Invalid opaque synthetic person")
                opaque = obj["idPessoaEntrega"]
                require(opaque not in seen_opaque
                        and obj.get("codigoPessoaOrigem") == opaque
                        and truth_by_opaque.get(opaque) == (gestor, filename),
                        "Person ZIP does not match independent bridge truth sidecar")
                seen_opaque.add(opaque)
                require(not ({"basePersonId", "observationId", "partition", "truth"} & obj.keys()),
                        "Ground truth leaked into ingestion ZIP")
        total += count
        expected_sha[gestor] = digest.upper()
    require(set(expected_sha) == set(EXPECTED_GESTORES) and total == materialized
            and seen_opaque == set(truth_by_opaque),
            "Incomplete manager coverage or non-bijective synthetic handoff")
    return {
        "schema": "V9_E2E_IBGE_BRIDGE_PREFLIGHT_V1",
        "status": "PASS",
        "scope": "DEV_SYNTHETIC_INGESTION_ONLY_NOT_V9_CERTIFICATION",
        "generatorVersion": GENERATOR,
        "populationProfile": PROFILE,
        "seed": SEED,
        "baseIdentities": PEOPLE,
        "sourceObservations": source,
        "materializedObservations": materialized,
        "excludedObservations": 0,
        "packages": len(packages),
        "gestores": list(EXPECTED_GESTORES),
        "corpusInputFingerprintSha256": fingerprint.upper(),
        "bridgeManifestSha256": sha256(bridge_file),
        "truthSha256": sha256(truth_file),
        "packageSha256": expected_sha,
        "scientificValidation": False,
        "activated": False,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args()
    receipt = verify(args.root)
    require(not args.receipt.exists() and not args.receipt.is_symlink(),
            "Refuse to overwrite an existing receipt")
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print("IBGE-derived C# synthetic bridge preflight: PASS (only aggregate counts).")


if __name__ == "__main__":
    main()
