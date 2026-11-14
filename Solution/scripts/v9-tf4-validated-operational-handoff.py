#!/usr/bin/env python3
"""Independent verification of VALIDATION's *one* final operational V9 JSON.

This is a byte-exact V9 SQL payload_fitted contract, not a proof that the
legacy production Runner.RunAsync has been upgraded to the V9 runtime.
No model is selected here: it must already have been selected by VALIDATION.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re

SHA = re.compile(r"^[0-9a-f]{64}$")
CONTRACT = "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1"
SCHEMA = "JORNADA_V9_VALIDATED_OPERATIONAL_MODEL_FILE_V1"
SUFFIX = ".operational.json"
MAX_BYTES = 67_108_864


def require(ok, reason):
    if not ok:
        raise ValueError("V9_VALIDATED_OPERATIONAL_MODEL_FAIL_CLOSED: " + reason)


def read_json(path, limit=4 * 1024 * 1024):
    require(path.is_file() and not path.is_symlink(), "missing or symlinked JSON: " + path.name)
    require(0 < path.stat().st_size <= limit, "invalid file size: " + path.name)
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify(validation_path, reference_path, directory, expected_contract):
    require(expected_contract == CONTRACT, "unexpected operational contract")
    result = read_json(validation_path, limit=512 * 1024 * 1024)
    require(result.get("refitted") is False
            and result.get("testUsed") is False
            and result.get("certified") is False
            and result.get("activated") is False, "validation status not frozen")
    winner = result.get("selectedValidationSnapshotSha256")
    status = result.get("status")
    if status == "NO_FINALIST_PASSES_DECLARED_SCIENTIFIC_GATES":
        require(winner is None
                and result.get("selectedValidationOperationalModelFileName") is None
                and not reference_path.exists()
                and (not directory.exists() or not any(directory.iterdir())),
                "no-winner campaign emitted a production model")
        print("V9_VALIDATION_NO_WINNER_NO_OPERATIONAL_JSON")
        return None
    require(status == "VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED",
            "scientific winner not approved by VALIDATION")
    require(isinstance(winner, str) and SHA.fullmatch(winner), "winner SHA invalid")
    name = winner + SUFFIX
    require(result.get("selectedValidationOperationalModelFileName") == name
            and result.get("selectedValidationOperationalModelContract") == CONTRACT,
            "validation result is not bound to final operational filename")
    reference = read_json(reference_path)
    require(reference.get("schema") == SCHEMA
            and reference.get("fileName") == name
            and reference.get("winnerSnapshotSha256") == winner
            and reference.get("operationalModelContract") == CONTRACT
            and reference.get("snapshotSchemaVersion") == "V9_FITTED_SNAPSHOT_V3"
            and reference.get("jointCells") == 81
            and reference.get("tfSlots") == 4
            and type(reference.get("blockingPasses")) is int
            and reference["blockingPasses"] > 0
            and reference.get("byteExactRoundTrip") is True
            and all(reference.get(x) is False for x in
                    ("refitted", "testUsed", "certified", "activated", "published")),
            "operational reference mismatches selected VALIDATION winner")
    require(directory.is_dir() and not directory.is_symlink(),
            "operational directory is absent or symlinked")
    entries = list(directory.iterdir())
    require(len(entries) == 1 and entries[0].name == name
            and entries[0].is_file() and not entries[0].is_symlink(),
            "one and only one SHA-addressed operational JSON is required")
    model = entries[0]
    require(0 < model.stat().st_size <= MAX_BYTES, "invalid operational payload size")
    require(sha256(model) == winner, "operational JSON bytes differ from winning model SHA")
    raw = read_json(model, limit=MAX_BYTES)
    require(isinstance(raw, dict)
            and raw.get("SchemaVersion") == "V9_FITTED_SNAPSHOT_V3"
            and isinstance(raw.get("JointCells"), list)
            and len(raw["JointCells"]) == 81
            and isinstance(raw.get("UnitTfCalibrations"), list)
            and len(raw["UnitTfCalibrations"]) == 4
            and len(set((str(x.get("Side")), str(x.get("Unit")))
                        for x in raw["UnitTfCalibrations"])) == 4
            and isinstance(raw.get("BlockingPasses"), list)
            and len(raw["BlockingPasses"]) == reference["blockingPasses"]
            and raw.get("CandidateGrouping", {}).get("CandidateGrouping")
                == "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            and raw.get("JointSupport") is not None
            and raw.get("GoldParentSnapshotSha256") is None
            and raw.get("FitFingerprintSha256")
                == reference.get("fitFingerprintSha256")
            and raw.get("ProjectionFingerprintSha256")
                == reference.get("projectionFingerprintSha256"),
            "operational JSON lost TF4, 81 cells, grouping, blocking or identity")
    print("V9_VALIDATION_OPERATIONAL_JSON_BYTES_VERIFIED", name)
    return name


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["verify"])
    parser.add_argument("--validation", required=True, type=Path)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--expected-contract", default=CONTRACT)
    args = parser.parse_args()
    verify(args.validation, args.reference, args.directory, args.expected_contract)


if __name__ == "__main__":
    main()
