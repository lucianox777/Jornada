#!/usr/bin/env python3
"""Fail-closed DEV preflight for ZIPs from the existing IBGE-derived C# generator.

This validates aggregate provenance and package structure only. Never publish
the generated population, ZIPs or truth sidecars as GitHub Actions artifacts.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
import zipfile

EXPECTED_GESTORES = ("SEHAB", "SMADS", "SMDET", "SMS")
GENERATOR = "JORNADA_SYNTH_CORPUS_CSHARP_V1"
PROFILE = "DEMOGRAPHIC_PRIMARY_V1"
SEED = 355
PEOPLE = 1000
# Ephemeral 1k DEV packages only; do not silently accept unbounded/untrusted
# compressed inputs as evidence or consume arbitrary memory while parsing.
MAX_METADATA_BYTES = 8 * 1024 * 1024
MAX_TRUTH_BYTES = 32 * 1024 * 1024
MAX_SOURCE_CSV_BYTES = 32 * 1024 * 1024
MAX_PACKAGE_BYTES = 64 * 1024 * 1024
MAX_PERSON_MEMBER_BYTES = 16 * 1024 * 1024
MAX_MANIFEST_MEMBER_BYTES = 1024 * 1024


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def no_duplicate_keys(pairs):
    data = {}
    for key, value in pairs:
        require(key not in data, "Duplicate JSON key in synthetic E2E manifest")
        data[key] = value
    return data


def reject_nonfinite(value):
    raise ValueError(f"Non-finite numeric token in synthetic DEV evidence: {value}")


def read_json(path: Path):
    require(path.is_file() and not path.is_symlink(), "Missing or symlinked synthetic manifest")
    require(path.stat().st_size <= MAX_METADATA_BYTES, "Oversized synthetic manifest")
    return json.loads(path.read_text(encoding="utf-8"),
                      object_pairs_hook=no_duplicate_keys,
                      parse_constant=reject_nonfinite)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()



def source_observation_truth(corpus_dir: Path, corpus: dict):
    """Read separately emitted generator CSVs; never return person-level values."""
    outputs = corpus.get("outputs")
    required_names = {"pessoas_verdade.csv", "observacoes.csv", "gabarito.json"}
    require(isinstance(outputs, list) and len(outputs) == len(required_names),
            "Generator output manifest must enumerate the three source files")
    seen = set()
    for entry in outputs:
        require(isinstance(entry, dict), "Invalid generator output entry")
        name, digest = entry.get("path"), entry.get("sha256")
        require(isinstance(name, str) and name in required_names and name not in seen,
                "Missing, repeated or unrecognized generator output")
        require(isinstance(digest, str) and len(digest) == 64
                and all(c in "0123456789ABCDEF" for c in digest.upper()),
                "Invalid generator output digest")
        path = corpus_dir / name
        require(path.is_file() and not path.is_symlink()
                and path.stat().st_size <= MAX_SOURCE_CSV_BYTES
                and sha256(path) == digest.upper(),
                "Generator output bytes not bound to source manifest")
        seen.add(name)
    require(seen == required_names, "Incomplete generator output manifest")

    def rows(name, headers):
        path = corpus_dir / name
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            require(reader.fieldnames is not None
                    and len(set(reader.fieldnames)) == len(reader.fieldnames)
                    and set(headers).issubset(reader.fieldnames),
                    "Invalid or ambiguous generator CSV header")
            yield from reader

    people = {}
    for row in rows("pessoas_verdade.csv", ("base_person_id", "particao")):
        person, partition = row["base_person_id"], row["particao"]
        require(person and person not in people
                and partition in ("TRAIN", "VALIDATION", "TEST"),
                "Invalid or duplicated source person")
        people[person] = partition
        require(len(people) <= PEOPLE, "Source person count exceeds 1k DEV")
    require(len(people) == PEOPLE, "Source generator person count differs from 1k")

    observations = {}
    for row in rows("observacoes.csv",
                    ("observacao_id", "base_person_id", "particao", "gestor")):
        observation, person = row["observacao_id"], row["base_person_id"]
        partition, gestor = row["particao"], row["gestor"]
        require(observation and observation not in observations
                and person in people and partition == people[person]
                and gestor in ("G0", "G1", "G2", "G3"),
                "Invalid source observation/person/partition mapping")
        observations[observation] = (person, partition, gestor)
        require(len(observations) <= PEOPLE * 4,
                "Source observation count exceeds declared DEV maximum")
    return observations


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
    csv_observations = source_observation_truth(corpus_dir, corpus)
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
            and truth_file.stat().st_size <= MAX_TRUTH_BYTES
            and sha256(truth_file) == sidecar.get("sha256"),
            "Missing or modified sidecar truth")
    sidecar_lines = truth_file.read_text(encoding="utf-8").splitlines()
    require(len(sidecar_lines) == source, "Ground-truth row count mismatch")
    truth_by_opaque = {}
    truth_source_observations = {}
    base_ids = set()
    observation_ids = set()
    for raw_row in sidecar_lines:
        row = json.loads(raw_row, object_pairs_hook=no_duplicate_keys,
                         parse_constant=reject_nonfinite)
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
        truth_source_observations[observation] = (
            row["BasePersonId"], row["Partition"], row.get("SyntheticGestor"))
        observation_ids.add(observation)
        base_ids.add(row["BasePersonId"])
    require(len(base_ids) == PEOPLE,
            "Synthetic generator lost or duplicated base identities")
    require(len(csv_observations) == source
            and csv_observations == truth_source_observations,
            "Source CSV and bridge truth sidecar differ")

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
                and package.stat().st_size <= MAX_PACKAGE_BYTES
                and sha256(package) == digest.upper(), "ZIP bytes differ from bridge receipt")
        with zipfile.ZipFile(package, "r") as archive:
            info = archive.infolist()
            require([entry.filename for entry in info]
                    == ["manifest.json", "pessoas.jsonl", "registros.jsonl"],
                    "Unexpected ZIP members, possible truth leak")
            require(all(not (entry.flag_bits & 1) for entry in info),
                    "Encrypted synthetic ZIP member forbidden")
            require(info[0].file_size <= MAX_MANIFEST_MEMBER_BYTES
                    and info[1].file_size <= MAX_PERSON_MEMBER_BYTES
                    and info[2].file_size == 0,
                    "Synthetic ZIP member exceeds its approved bound")
            require(not archive.read("registros.jsonl"), "Person-only bridge may not fabricate facts")
            rows = archive.read("pessoas.jsonl").splitlines()
            require(len(rows) == count, "ZIP materialized person count mismatch")
            for row in rows:
                obj = json.loads(row, object_pairs_hook=no_duplicate_keys,
                                 parse_constant=reject_nonfinite)
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
    # Private sidecar to SQL parity: SQL Server HASHBYTES hashes NVARCHAR(MAX)
    # as UTF-16LE without BOM; no names or opaque IDs leave the ephemeral job.
    source_ids = {gestor: [] for gestor in EXPECTED_GESTORES}
    for opaque, (gestor, _) in truth_by_opaque.items():
        require(gestor in source_ids, "Unexpected synthetic manager in private sidecar")
        source_ids[gestor].append(opaque)
    identity_sha = {
        gestor: hashlib.sha256(
            "".join(opaque + "\n" for opaque in sorted(ids)).encode("utf-16le")
        ).hexdigest().upper()
        for gestor, ids in source_ids.items()
    }
    require(all(source_ids.values()), "Empty per-manager source identity set")
    # ZIP-derived complete source-set fingerprint, checked against the
    # independently stored truth sidecar above. No source IDs leave the receipt.
    all_source_set_sha = hashlib.sha256(
        "".join(opaque + "\n" for opaque in sorted(seen_opaque)).encode("utf-16le")
    ).hexdigest().upper()
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
        "sourceIdentitySetSha256Utf16le": identity_sha,
        "sourceSetSha256Utf16le": all_source_set_sha,
        "sourceIdentitySetDigestMethod": "SHA256_UTF16LE_LF_SORTED_OPAQUE_SOURCE_V1",
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
