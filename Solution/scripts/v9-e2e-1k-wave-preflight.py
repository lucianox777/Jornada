#!/usr/bin/env python3
"""Verify private synthetic wave packages before the single DEV FULL.

This is a fixture admission check, not a generation or scoring routine.
Only aggregate counts and hashes leave the private input directory.
"""
import argparse
from collections import defaultdict
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import zipfile

SPEC = importlib.util.spec_from_file_location(
    "bridge_preflight", Path(__file__).with_name("v9-e2e-ibge-bridge-preflight.py"))
BRIDGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BRIDGE)
require = BRIDGE.require
HEX = re.compile(r"[0-9A-Fa-f]{64}")
SOURCE = re.compile(r"SYNTH-[0-9A-F]{32}")
DELIVERY = re.compile(r"SYNTH-DEL-[0-9A-F]{32}")
ROUTES = dict(zip(("G0", "G1", "G2", "G3"), BRIDGE.EXPECTED_GESTORES))


def digest_bytes(raw):
    return hashlib.sha256(raw).hexdigest().upper()


def read_private(path, bound):
    require(path.is_file() and not path.is_symlink(), "Invalid private input")
    with path.open("rb") as stream:
        raw = stream.read(bound + 1)
    require(0 < len(raw) <= bound, "Private input size")
    return raw


def parse(raw):
    return json.loads(raw, object_pairs_hook=BRIDGE.no_duplicate_keys,
                      parse_constant=BRIDGE.reject_nonfinite)


def check_wave(directory, report, fingerprint, key_sha, people):
    require(directory.is_dir() and not directory.is_symlink(), "Invalid wave directory")
    manifest_raw = read_private(directory / "bridge-manifest.json", BRIDGE.MAX_METADATA_BYTES)
    truth_raw = read_private(directory / "bridge-truth.jsonl", BRIDGE.MAX_TRUTH_BYTES)
    manifest = parse(manifest_raw)
    sidecar = manifest["truthSidecar"]
    require(digest_bytes(manifest_raw) == report["manifestSha256"].upper()
            and digest_bytes(truth_raw) == report["truthSha256"].upper()
            and sidecar["sha256"] == digest_bytes(truth_raw)
            and sidecar["path"] == "bridge-truth.jsonl"
            and sidecar["containsSyntheticGroundTruth"] is True
            and sidecar["allowedForScoring"] is False
            and manifest["bridgeVersion"] == "SYNTHETIC_INGESTION_BRIDGE_WAVES_V1"
            and manifest["generatorVersion"] == BRIDGE.GENERATOR
            and manifest["corpusInputFingerprintSha256"] == fingerprint
            and manifest["pseudonymizationKeySha256"].upper() == key_sha,
            "Wave provenance mismatch")
    truth = {}
    seen_observations = set()
    for line in truth_raw.splitlines():
        row = parse(line)
        manager, opaque, base = row["GestorCodigo"], row["OpaquePersonId"], row["BasePersonId"]
        require(row["Status"] == "MATERIALIZADA" and row["ExclusionReason"] is None
                and SOURCE.fullmatch(opaque) and opaque not in truth
                and row["ObservationId"] not in seen_observations
                and base in people and row["Partition"] == people[base]
                and ROUTES.get(row["SyntheticGestor"]) == manager,
                "Invalid or repeated wave truth")
        seen_observations.add(row["ObservationId"])
        truth[opaque] = (manager, row["PackageFileName"], base)
    total = len(truth)
    require(0 < total <= len(people) * 4
            and manifest["materializedObservationCount"] == total
            and manifest["sourceObservationCount"] == total
            and manifest["excludedObservationCount"] == 0
            and report["materializedObservationCount"] == total
            and report["sourceObservationCount"] == total
            and report["excludedObservationCount"] == 0
            and report["newSourceCount"] + report["updatedSourceCount"] == total,
            "Wave census mismatch")
    seen = set()
    managers = set()
    packages = []
    private = []
    for entry in manifest["packages"]:
        manager, filename, count = entry["GestorCodigo"], entry["FileName"], entry["PeopleCount"]
        require(manager in ROUTES.values() and manager not in managers
                and isinstance(filename, str) and filename == Path(filename).name
                and chr(92) not in filename and filename.endswith(".zip")
                and type(count) is int and 0 < count <= len(people),
                "Invalid package identity")
        managers.add(manager)
        raw = read_private(directory / filename, BRIDGE.MAX_PACKAGE_BYTES)
        sha = digest_bytes(raw)
        require(sha == entry["sha256"].upper(), "ZIP digest mismatch")
        import io
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            members = z.infolist()
            require([x.filename for x in members] ==
                    ["manifest.json", "pessoas.jsonl", "registros.jsonl"]
                    and all(not (x.flag_bits & 1) for x in members)
                    and members[0].file_size <= BRIDGE.MAX_MANIFEST_MEMBER_BYTES
                    and members[1].file_size <= BRIDGE.MAX_PERSON_MEMBER_BYTES
                    and members[2].file_size == 0, "Unexpected package members")
            require(not z.read("registros.jsonl"), "Unexpected facts")
            rows = z.read("pessoas.jsonl").splitlines()
            require(len(rows) == count, "Package count mismatch")
            source_ids = []
            for raw_row in rows:
                row = parse(raw_row)
                opaque, delivery = row["codigoPessoaOrigem"], row["idPessoaEntrega"]
                require(SOURCE.fullmatch(opaque) and DELIVERY.fullmatch(delivery)
                        and opaque not in seen and opaque in truth
                        and truth[opaque][:2] == (manager, filename)
                        and not ({"basePersonId", "observationId", "partition", "truth"} & row.keys()),
                        "Package/sidecar bijection mismatch")
                seen.add(opaque)
                source_ids.append(opaque)
                # Retain only booleans and digest of payload excluding delivery ID.
                cpf = row.get("cpf")
                require(cpf is None or isinstance(cpf, str), "Invalid CPF field type")
                content = {k: v for k, v in row.items() if k != "idPessoaEntrega"}
                private.append((manager, opaque, truth[opaque][2], delivery,
                                cpf is not None,
                                digest_bytes(json.dumps(content, sort_keys=True).encode())))
        packages.append({"manager": manager, "fileName": filename, "zipSha256": sha,
                         "zipBytes": len(raw), "versions": count,
                         "sourceSetSha256Utf16le": digest_bytes(
                             "".join(x + "\n" for x in sorted(source_ids)).encode("utf-16le"))})
    require(seen == set(truth), "Incomplete wave package coverage")
    expected = {(x["gestorCodigo"], x["fileName"], x["sha256"].upper(), x["peopleCount"])
                for x in report["packages"]}
    actual = {(x["manager"], x["fileName"], x["zipSha256"], x["versions"]) for x in packages}
    require(expected == actual and len(report["packages"]) == len(packages),
            "Wave report package mismatch")
    return private, packages


def characterize(versions, people):
    origin_base = {}
    base_manager = {}
    origins = defaultdict(list)
    deliveries = set()
    prior = {}
    for wave in versions:
        for manager, source, base, delivery, has_cpf, content in wave:
            origin = (manager, source)
            require(origin_base.setdefault(origin, base) == base
                    and base_manager.setdefault((base, manager), source) == source
                    and delivery not in deliveries, "Stable source identity drift")
            require(prior.get(origin) != content, "Repeated unchanged source")
            prior[origin] = content
            deliveries.add(delivery)
            origins[origin].append(has_cpf)
    require(set(origin_base.values()) == set(people), "Incomplete 1k population")
    cpf_origins = {origin for origin, flags in origins.items() if any(flags)}
    cpf_by_base = defaultdict(set)
    for origin in cpf_origins:
        cpf_by_base[origin_base[origin]].add(origin)
    cross = sum(any(other != origin for other in cpf_by_base[origin_base[origin]])
                for origin, flags in origins.items() if not all(flags))
    multi = sum(sum(flags) >= 2 for flags in origins.values())
    require(cross > 0 and multi > 0, "Missing CPF cross-origin or CPF multiversion fixture")
    return {"baseIdentities": len(people), "sourceVersions": len(deliveries),
            "persistentOrigins": len(origins), "cpfMultiversionOrigins": multi,
            "nonCpfOriginsWithOtherCpfOrigin": cross,
            "cpfVersions": sum(sum(flags) for flags in origins.values()),
            "nonCpfVersions": sum(len(flags) - sum(flags) for flags in origins.values())}


def verify(root):
    corpus, ingestion = root / "corpus", root / "ingestion"
    for directory in (root, corpus, ingestion):
        require(directory.is_dir() and not directory.is_symlink(), "Invalid corpus root")
    source = BRIDGE.read_json(corpus / "generation-manifest.json")
    require(source["generator_version"] == BRIDGE.GENERATOR
            and source["population_profile"] == BRIDGE.PROFILE
            and source["seed"] == BRIDGE.SEED, "Wrong 1k corpus provenance")
    source_rows = BRIDGE.source_observation_truth(corpus, source)
    people = {}
    for base, partition, _ in source_rows.values():
        require(people.setdefault(base, partition) == partition, "Partition drift")
    require(len(people) == BRIDGE.PEOPLE, "Wrong base population")
    header = BRIDGE.read_json(ingestion / "waves-manifest.json")
    fingerprint = source["input_fingerprint_sha256"].upper()
    key_sha = header["pseudonymizationKeySha256"].upper()
    require(HEX.fullmatch(fingerprint) and HEX.fullmatch(key_sha)
            and header["seed"] == BRIDGE.SEED
            and header["corpusInputFingerprintSha256"].upper() == fingerprint
            and header["scenarioVersion"] == "SYNTHETIC_INGESTION_WAVES_V1"
            and header["bridgeVersion"] == "SYNTHETIC_INGESTION_BRIDGE_WAVES_V1"
            and 2 <= len(header["waves"]) <= 4
            and header["scenario"]["waveCount"] == len(header["waves"]),
            "Wrong wave scenario provenance")
    versions, packages = [], []
    for i, report in enumerate(header["waves"], 1):
        require(report["wave"] == i, "Wave order mismatch")
        rows, items = check_wave(ingestion / f"wave-{i:02}", report, fingerprint, key_sha, people)
        versions.append(rows)
        packages.append({"wave": i, "packages": items})
    result = characterize(versions, people)
    actual_sources = {(base, manager) for wave in versions
                      for manager, _, base, _, _, _ in wave}
    expected_sources = {(base, ROUTES[gestor]) for base, _, gestor in source_rows.values()}
    require(actual_sources == expected_sources, "Original corpus origin set mismatch")
    require(header["baselineSourcesWithCpf"] + header["baselineSourcesWithoutCpf"]
            == len(expected_sources), "Baseline origin census mismatch")
    return {"schema": "V9_E2E_1K_WAVE_PREFLIGHT_V1", "status": "PASS",
            "scope": "SYNTHETIC_FIXTURE_ONLY_BEFORE_SINGLE_FULL",
            "waveCount": len(versions), "packagesByWave": packages,
            "scientificTest": False, "activated": False, **result}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = verify(args.root)
        with args.receipt.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, sort_keys=True)
    except (ValueError, OSError, KeyError, TypeError, AttributeError, zipfile.BadZipFile):
        raise SystemExit("V9_E2E_1K_WAVE_PREFLIGHT_REJECTED") from None
    print("V9_E2E_1K_WAVE_PREFLIGHT_PASS_NOT_FULL_E2E")


if __name__ == "__main__":
    main()
