#!/usr/bin/env python3
"""DEV HTTP->Bronze/Silver lineage receipt; excludes V9 and scientific TEST.

Only the canonical HTTP E2E shell may assert the SQL-ledger checks: this
module checks consistency of the resulting aggregate receipts against the
independently validated ZIP bridge preflight. No personal rows are output.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

MANAGERS = ("SEHAB", "SMADS", "SMDET", "SMS")
METHOD = "SHA256_UTF16LE_LF_SORTED_OPAQUE_SOURCE_V1"
SCHEMA = "V9_E2E_IBGE_HTTP_1K_V2"
PROOF_FIELDS = {
    "gestor", "deliveryIdSha256", "packageSha256", "zipBytes",
    "silverPersons", "silverSourceSetSha256Utf16le", "httpStatus",
    "bronzePayloadAndBytesVerified", "lotesProcessadosVerified",
}
MAX_FILE_BYTES = 1024 * 1024


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9A-F]{64}", value) is not None


def hex_sha(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{40}", value) is not None


def json_object(pairs):
    obj = {}
    for key, value in pairs:
        require(key not in obj, "duplicate HTTP lineage JSON property")
        obj[key] = value
    return obj


def reject_constant(value):
    raise ValueError("nonfinite HTTP lineage value")


def load(path):
    require(path.is_file() and not path.is_symlink(), "missing or linked HTTP lineage file")
    require(0 < path.stat().st_size <= MAX_FILE_BYTES,
            "HTTP lineage input size invalid")
    return json.loads(path.read_bytes(), object_pairs_hook=json_object,
                      parse_constant=reject_constant)


def verify(preflight, summary, expected_commit):
    """Fail closed on all four ZIP receipts and SQL metadata proof claims."""
    require(hex_sha(expected_commit), "invalid expected checkout SHA")
    require(isinstance(preflight, dict) and isinstance(summary, dict),
            "invalid HTTP lineage objects")
    require(preflight.get("schema") == "V9_E2E_IBGE_BRIDGE_PREFLIGHT_V1"
            and preflight.get("status") == "PASS"
            and preflight.get("seed") == 355
            and preflight.get("baseIdentities") == 1000
            and type(preflight.get("sourceObservations")) is int
            and 1000 <= preflight["sourceObservations"] <= 4000
            and preflight.get("materializedObservations") == preflight["sourceObservations"]
            and preflight.get("excludedObservations") == 0
            and preflight.get("packages") == 4
            and preflight.get("sourceIdentitySetDigestMethod") == METHOD
            and all(digest(preflight.get(key)) for key in (
                "corpusInputFingerprintSha256", "bridgeManifestSha256",
                "truthSha256", "sourceSetSha256Utf16le")),
            "unverified IBGE bridge preflight")
    packages = preflight.get("packageSha256")
    sources = preflight.get("sourceIdentitySetSha256Utf16le")
    require(isinstance(packages, dict) and isinstance(sources, dict)
            and set(packages) == set(sources) == set(MANAGERS)
            and all(digest(packages[k]) and digest(sources[k]) for k in MANAGERS),
            "unverified manager ZIP or source SHA")

    require(summary.get("schema") == SCHEMA
            and summary.get("status") == "PASS"
            and summary.get("scope")
            == "SYNTHETIC_DEV_HTTP_BRONZE_SILVER_ONLY_NOT_V9_CERTIFICATION"
            and summary.get("commitSha") == expected_commit
            and summary.get("seed") == 355
            and summary.get("baseIdentities") == 1000
            and summary.get("generatorVersion") == preflight.get("generatorVersion")
            and summary.get("populationProfile") == preflight.get("populationProfile")
            and summary.get("generatorFingerprintSha256")
            == preflight["corpusInputFingerprintSha256"]
            and summary.get("bridgeManifestSha256") == preflight["bridgeManifestSha256"]
            and summary.get("groundTruthSidecarSha256") == preflight["truthSha256"]
            and summary.get("sourceSetSha256Utf16le") == preflight["sourceSetSha256Utf16le"]
            and summary.get("silverSourceIdentityDigestMethod") == METHOD
            and summary.get("scientificValidation") is False
            and summary.get("activated") is False
            and summary.get("v9RunnerVerified") is False
            and summary.get("E2E_NUCLEO_PASS") is False
            and summary.get("E2E_INTEGRAL_PASS") is False,
            "HTTP receipt drift, failed guard or V9 claim")
    for key, expected in (
        ("sourceObservations", preflight["sourceObservations"]),
        ("materializedObservations", preflight["materializedObservations"]),
        ("silverPersons", preflight["materializedObservations"]),
        ("excludedObservations", 0),
        ("deliveries", 4),
        ("bronzeFiles", 4),
        ("silverSourceIdentitySetsVerified", 4),
        ("httpZipBronzePayloadsVerified", 4),
        ("bronzeLotesTerminalVerified", 4),
    ):
        require(type(summary.get(key)) is int and summary[key] == expected,
                "HTTP counts or proof gates invalid: " + key)
    proofs = summary.get("deliveryProofs")
    require(isinstance(proofs, list) and len(proofs) == 4,
            "missing manager-specific HTTP->Bronze provenance")
    seen, delivery_ids = set(), set()
    observed_total = 0
    for proof in proofs:
        require(isinstance(proof, dict) and set(proof) == PROOF_FIELDS,
                "unexpected HTTP lineage proof fields")
        manager = proof.get("gestor")
        require(manager in MANAGERS and manager not in seen,
                "duplicated or unknown HTTP manager")
        seen.add(manager)
        delivery_id_hash = proof["deliveryIdSha256"]
        require(digest(delivery_id_hash) and delivery_id_hash not in delivery_ids,
                "missing or duplicate delivery ID fingerprint")
        delivery_ids.add(delivery_id_hash)
        require(proof["packageSha256"] == packages[manager]
                and proof["silverSourceSetSha256Utf16le"] == sources[manager],
                "HTTP ZIP or Silver manager digest differs from bridge")
        require(type(proof["zipBytes"]) is int and proof["zipBytes"] > 0
                and type(proof["silverPersons"]) is int and proof["silverPersons"] > 0
                and type(proof["httpStatus"]) is int and proof["httpStatus"] == 202
                and proof["bronzePayloadAndBytesVerified"] is True
                and proof["lotesProcessadosVerified"] is True,
                "HTTP/Silver/SQL ledger is incomplete")
        observed_total += proof["silverPersons"]
    require(seen == set(MANAGERS)
            and observed_total == preflight["materializedObservations"],
            "manager proofs omit or repeat source observations")
    return True


def build(preflight, proofs, commit):
    require(isinstance(preflight, dict) and isinstance(proofs, list),
            "invalid builder inputs")
    summary = {
        "schema": SCHEMA, "status": "PASS",
        "scope": "SYNTHETIC_DEV_HTTP_BRONZE_SILVER_ONLY_NOT_V9_CERTIFICATION",
        "commitSha": commit, "seed": 355, "baseIdentities": 1000,
        "generatorVersion": preflight.get("generatorVersion"),
        "populationProfile": preflight.get("populationProfile"),
        "generatorFingerprintSha256": preflight.get("corpusInputFingerprintSha256"),
        "bridgeManifestSha256": preflight.get("bridgeManifestSha256"),
        "groundTruthSidecarSha256": preflight.get("truthSha256"),
        "sourceSetSha256Utf16le": preflight.get("sourceSetSha256Utf16le"),
        "sourceObservations": preflight.get("sourceObservations"),
        "materializedObservations": sum(p.get("silverPersons", 0) for p in proofs),
        "excludedObservations": 0,
        "deliveries": len(proofs), "bronzeFiles": len(proofs),
        "silverPersons": sum(p.get("silverPersons", 0) for p in proofs),
        "silverSourceIdentitySetsVerified": len(proofs),
        "silverSourceIdentityDigestMethod": METHOD,
        "httpZipBronzePayloadsVerified": len(proofs),
        "bronzeLotesTerminalVerified": len(proofs),
        "deliveryProofs": sorted(proofs, key=lambda p: p["gestor"]),
        "scientificValidation": False, "activated": False,
        "v9RunnerVerified": False,
        "E2E_NUCLEO_PASS": False, "E2E_INTEGRAL_PASS": False,
    }
    verify(preflight, summary, commit)
    return summary


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preflight", type=Path, required=True)
    parser.add_argument("--proofs", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    paths = (args.preflight, args.proofs, args.receipt)
    require(not args.receipt.exists() and not args.receipt.is_symlink(),
            "HTTP lineage receipt already exists")
    require(len({p.resolve() for p in paths}) == 3,
            "HTTP lineage output aliases input")
    preflight = load(args.preflight)
    proof_lines = args.proofs.read_bytes().splitlines()
    require(0 < len(proof_lines) <= 4, "unexpected proof line count")
    proofs = [json.loads(line, object_pairs_hook=json_object,
                         parse_constant=reject_constant)
              for line in proof_lines]
    summary = build(preflight, proofs, args.expected_sha)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    with args.receipt.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(summary, indent=2, sort_keys=True) + "\n")
    print("IBGE HTTP->Bronze/Silver payload provenance: PASS (DEV only, no V9).")


if __name__ == "__main__":
    main()
